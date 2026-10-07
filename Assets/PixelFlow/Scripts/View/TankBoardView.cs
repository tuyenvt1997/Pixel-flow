using System;
using System.Collections.Generic;
using PixelFlow.Core;
using PixelFlow.Performance;
using UnityEngine;

namespace PixelFlow.View
{
    /// <summary>
    /// Where a picked tank currently is, as resolved by <see cref="TankBoardView.TryGetTank"/>.
    /// </summary>
    public enum TankLocation
    {
        /// <summary>
        /// Front tank of a supply lane (tapping it launches it).
        /// </summary>
        LaneFront,

        /// <summary>
        /// Tank waiting in a waiting slot (tapping it relaunches it).
        /// </summary>
        Slot,

        /// <summary>
        /// Anywhere else: deeper in a supply lane, in the entrance queue or on the belt.
        /// </summary>
        Other
    }

    /// <summary>
    /// Presents every tank of a <see cref="LevelSession"/> across the supply lanes, the belt entrance queue, the
    /// belt and the waiting slots, plus one empty frame per waiting slot. Views come from an
    /// <see cref="ObjectPool{T}"/> of <see cref="ColorTankView"/> and are mapped by tank id.
    /// <para>Layout (offsets applied in the root's rotation, root scale ignored):</para>
    /// <list type="bullet">
    /// <item>Supply: lane <c>i</c> is centred around <c>supplyRoot</c> on x at
    /// <c>(i - (laneCount - 1) / 2) * laneSpacing</c>; the lane front (row 0) sits at the root and row <c>r</c>
    /// is <c>r * rowSpacing</c> further down (-y).</item>
    /// <item>Waiting slots: slot <c>s</c> is centred around <c>trayRoot</c> on x at
    /// <c>(s - (capacity - 1) / 2) * slotSpacing</c>.</item>
    /// <item>Entrance queue and belt: positions from <see cref="BeltView"/>; tanks there are scaled to fit the track.</item>
    /// </list>
    /// Model events tween the views: <see cref="SupplyModel.OnLaneChanged"/> and the waiting-slot events move them with
    /// <see cref="ColorTankView.MoveTo"/>; <see cref="BeltModel.OnTankQueued"/> sends a tank to the entrance; belt
    /// tanks are then drawn in <c>LateUpdate</c> at their model position interpolated with the belt clock's tick
    /// phase (<see cref="BeltView.Phase"/>, <see cref="InterpolateBeltPosition"/>), so they move at a constant pace
    /// and hold still while the clock is frozen. A tank
    /// leaving the belt with ammo left goes to its waiting slot (on <see cref="BeltShootingLogic.OnOverflow"/>, when
    /// every slot is full, it plays the depletion instead); a depleted one plays
    /// <see cref="ColorTankView.PlayDeplete"/> where it is and is then returned to the pool. Nothing allocates per frame.
    /// </summary>
    public sealed class TankBoardView : MonoBehaviour
    {
        private const float EnterBlendSeconds = 0.12f;
        private const float SlotFrameDepth = 0.6f;
        private const float BeltTankFill = 0.8f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// One tank riding the belt: its view, the position the model reports and the interpolated display position.
        /// </summary>
        private struct Rider
        {
            public ColorTankModel tank;
            public ColorTankView view;
            public int target;
            public float display;
            public Vector3 blendFrom;
            public float blend;
        }

        [Tooltip("Tank view prefab. Instantiated only by the pool factory.")]
        [SerializeField] private ColorTankView tankPrefab;

        [Tooltip("Empty waiting-slot frame (no collider): a SpriteRenderer (tinted via its colour) or a mesh " +
                 "(tinted via _BaseColor). One is shown per waiting slot.")]
        [SerializeField] private Transform slotFramePrefab;

        [Tooltip("Colour of the empty waiting-slot frames.")]
        [SerializeField] private Color slotFrameColor = new Color(0.17f, 0.18f, 0.3f, 1f);

        [Tooltip("Anchor of the supply lanes (front row, centred between lanes).")]
        [SerializeField] private Transform supplyRoot;

        [Tooltip("Anchor of the waiting slots (centred between slots).")]
        [SerializeField] private Transform trayRoot;

        [Tooltip("Horizontal distance between supply lanes.")]
        [SerializeField] private float laneSpacing = 1.4f;

        [Tooltip("Vertical distance between rows in a supply lane.")]
        [SerializeField] private float rowSpacing = 1.2f;

        [Tooltip("Horizontal distance between waiting slots.")]
        [SerializeField] private float slotSpacing = 1.2f;

        private readonly Dictionary<int, ColorTankView> _views = new Dictionary<int, ColorTankView>();
        private readonly List<ColorTankView> _depleting = new List<ColorTankView>();
        private readonly List<ColorTankModel> _queued = new List<ColorTankModel>();
        private readonly List<Transform> _slotFrames = new List<Transform>();
        private Rider[] _riders = new Rider[0];
        private int _riderCount;

        private ObjectPool<ColorTankView> _pool;
        private LevelSession _session;
        private BeltView _belt;
        private MaterialPropertyBlock _frameProps;

        private Action<int> _onLaneChanged;
        private Action<ColorTankModel, int> _onTankAdded;
        private Action<ColorTankModel, int> _onTankRemoved;
        private Action<ColorTankModel> _onTankQueued;
        private Action<ColorTankModel> _onTankEntered;
        private Action<ColorTankModel, int> _onTankMoved;
        private Action<ColorTankModel, bool> _onTankLeft;
        private Action<ColorTankModel> _onOverflow;
        private Action<ColorTankView> _onDepleteFinished;

        /// <summary>
        /// Belt position at which a tank that has just entered the belt is drawn at the start of its first tick: the
        /// entrance corner, half a position before position 0.
        /// </summary>
        public const float EntrancePositionOffset = -0.5f;

        /// <summary>
        /// Number of tank views currently shown (bound views, including those still playing depletion).
        /// </summary>
        public int ActiveViewCount => _views.Count + _depleting.Count;

        private void Awake()
        {
            EnsureInitialised();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        /// <summary>
        /// Shows <paramref name="session"/>: clears any previous board, takes one pooled view per tank in the
        /// supply lanes and the waiting slots, binds it with its palette colour, snaps it to its layout position,
        /// shows one empty frame per waiting slot, resets the belt counter and subscribes to the supply, waiting-slot,
        /// belt and overflow events.
        /// </summary>
        /// <param name="session">Level session to present.</param>
        /// <param name="belt">Belt view, already built for this session's board.</param>
        /// <exception cref="ArgumentNullException">Thrown if session or belt is null.</exception>
        public void Build(LevelSession session, BeltView belt)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (belt == null)
                throw new ArgumentNullException(nameof(belt));

            EnsureInitialised();
            Clear();
            _session = session;
            _belt = belt;

            // Size the pool and every container for all tanks of the level now, so play never grows one.
            SupplyModel supply = session.Supply;
            SlotQueueManager slots = session.Tray;
            BeltModel beltModel = session.Belt;
            int tankCount = supply.TotalRemaining + slots.Count + beltModel.Count + beltModel.QueuedCount;
            _pool.Prewarm(tankCount);
            if (_depleting.Capacity < tankCount)
                _depleting.Capacity = tankCount;
            if (_queued.Capacity < beltModel.Capacity)
                _queued.Capacity = beltModel.Capacity;
            if (_riders.Length < beltModel.Capacity)
                _riders = new Rider[beltModel.Capacity];

            for (int lane = 0; lane < supply.LaneCount; lane++)
            {
                IReadOnlyList<ColorTankModel> tanks = supply.GetLane(lane);
                for (int row = 0; row < tanks.Count; row++)
                    Spawn(tanks[row]).SnapTo(LanePosition(lane, row));
            }

            for (int slot = 0; slot < slots.Count; slot++)
                Spawn(slots[slot]).SnapTo(SlotPosition(slot));

            ShowSlotFrames(slots.Capacity);
            UpdateCounter();

            supply.OnLaneChanged += _onLaneChanged;
            slots.OnTankAdded += _onTankAdded;
            slots.OnTankRemoved += _onTankRemoved;
            beltModel.OnTankQueued += _onTankQueued;
            beltModel.OnTankEntered += _onTankEntered;
            beltModel.OnTankMoved += _onTankMoved;
            beltModel.OnTankLeft += _onTankLeft;
            session.BeltShooting.OnOverflow += _onOverflow;
        }

        /// <summary>
        /// Unsubscribes from the current session, returns every view (including depleting ones) to the pool and
        /// hides the slot frames. Safe to call when nothing is built.
        /// </summary>
        public void Clear()
        {
            Unsubscribe();
            _session = null;
            _belt = null;
            _queued.Clear();
            for (int i = 0; i < _riderCount; i++)
                _riders[i] = default;
            _riderCount = 0;

            for (int i = 0; i < _slotFrames.Count; i++)
                _slotFrames[i].gameObject.SetActive(false);

            if (_pool == null)
                return;

            foreach (ColorTankView view in _views.Values)
            {
                view.Unbind();
                _pool.Release(view);
            }
            _views.Clear();

            for (int i = 0; i < _depleting.Count; i++)
            {
                ColorTankView view = _depleting[i];
                view.Unbind();
                _pool.Release(view);
            }
            _depleting.Clear();
        }

        /// <summary>
        /// Resolves a raycast hit to a tank model and where that tank is. Returns true only for a view that is
        /// currently bound and not depleting; the caller decides whether the tank may be tapped.
        /// </summary>
        /// <param name="hit">Collider returned by the raycast (may belong to a child of the tank view).</param>
        /// <param name="tank">The tank model on success, otherwise null.</param>
        /// <param name="where">
        /// <see cref="TankLocation.LaneFront"/> for a supply-lane front, <see cref="TankLocation.Slot"/> for a
        /// waiting-slot tank, otherwise <see cref="TankLocation.Other"/>.
        /// </param>
        /// <returns>True if <paramref name="hit"/> belongs to a live tank view of this board.</returns>
        public bool TryGetTank(Collider hit, out ColorTankModel tank, out TankLocation where)
        {
            tank = null;
            where = TankLocation.Other;
            if (hit == null || _session == null)
                return false;

            ColorTankView view = hit.GetComponentInParent<ColorTankView>();
            if (view == null || view.Model == null)
                return false;

            if (!_views.TryGetValue(view.Model.Id, out ColorTankView mapped) || mapped != view)
                return false;

            tank = view.Model;
            if (_session.Supply.FindLaneWithFront(tank) >= 0)
                where = TankLocation.LaneFront;
            else if (_session.Tray.IndexOf(tank) >= 0)
                where = TankLocation.Slot;
            return true;
        }

        /// <summary>
        /// World-space muzzle of <paramref name="tank"/>. For a tank on the belt this is its belt world position
        /// (<see cref="BeltView.PositionToWorld"/> of its animated position). Otherwise it is the view's muzzle at
        /// its tween destination (<see cref="ColorTankView.TargetMuzzlePosition"/>), also for a tank still playing
        /// its depletion animation. Returns the slot root position (or this transform's position) if the tank has
        /// no view.
        /// </summary>
        /// <param name="tank">Tank model.</param>
        /// <returns>The muzzle position.</returns>
        public Vector3 GetMuzzle(ColorTankModel tank)
        {
            if (tank != null)
            {
                int rider = FindRider(tank);
                if (rider >= 0)
                    return _belt.PositionToWorld(_riders[rider].display);

                if (_views.TryGetValue(tank.Id, out ColorTankView view))
                    return view.TargetMuzzlePosition;

                for (int i = 0; i < _depleting.Count; i++)
                {
                    if (_depleting[i].Model == tank)
                        return _depleting[i].TargetMuzzlePosition;
                }
            }

            return trayRoot != null ? trayRoot.position : transform.position;
        }

        /// <summary>
        /// World-space position of row <paramref name="row"/> in supply lane <paramref name="lane"/>.
        /// </summary>
        /// <param name="lane">Lane index.</param>
        /// <param name="row">Row index (0 = lane front).</param>
        /// <returns>Layout position.</returns>
        public Vector3 LanePosition(int lane, int row)
        {
            int laneCount = _session != null ? _session.Supply.LaneCount : 1;
            var offset = new Vector3((lane - (laneCount - 1) * 0.5f) * laneSpacing, -row * rowSpacing, 0f);
            return RootPoint(supplyRoot, offset);
        }

        /// <summary>
        /// World-space position of waiting slot <paramref name="slot"/>.
        /// </summary>
        /// <param name="slot">Slot index (0 = leftmost).</param>
        /// <returns>Layout position.</returns>
        public Vector3 SlotPosition(int slot)
        {
            int capacity = _session != null ? _session.Tray.Capacity : 1;
            var offset = new Vector3((slot - (capacity - 1) * 0.5f) * slotSpacing, 0f, 0f);
            return RootPoint(trayRoot, offset);
        }

        /// <summary>
        /// Fractional belt position a belt tank is drawn at, interpolated from the tick clock: a tank the model
        /// reports at <paramref name="target"/> (it moved there on the last tick) is drawn <paramref name="phase"/> of
        /// the way from <c>target - 1</c> to <c>target</c>, so it reaches its position exactly when the next tick
        /// fires from there, and then continues seamlessly from it. A tank that has just entered
        /// (<paramref name="target"/> 0) waits at the entrance corner (<see cref="EntrancePositionOffset"/>) until the
        /// interpolation passes it. The result never exceeds <paramref name="target"/>, is non-decreasing in
        /// <paramref name="phase"/> and depends on nothing else, so a frozen clock holds the tank still.
        /// </summary>
        /// <param name="target">Belt position last reported by the model for the tank.</param>
        /// <param name="phase">Elapsed fraction of the current tick interval; clamped to <c>[0, 1]</c>.</param>
        /// <returns>The display position.</returns>
        public static float InterpolateBeltPosition(int target, float phase)
        {
            float position = target - 1f + Mathf.Clamp01(phase);
            if (target == 0 && position < EntrancePositionOffset)
                position = EntrancePositionOffset;
            return position;
        }

        /// <summary>
        /// The fractional belt position <paramref name="tank"/> is currently drawn at (see
        /// <see cref="InterpolateBeltPosition"/>), as of the last <c>LateUpdate</c> or entry.
        /// </summary>
        /// <param name="tank">Tank model.</param>
        /// <param name="position">The display position on success, otherwise 0.</param>
        /// <returns>True if <paramref name="tank"/> rides the belt.</returns>
        public bool TryGetBeltDisplayPosition(ColorTankModel tank, out float position)
        {
            int rider = FindRider(tank);
            position = rider >= 0 ? _riders[rider].display : 0f;
            return rider >= 0;
        }

        // LateUpdate: the controller has run this frame's ticks and pushed the belt clock in its Update.
        private void LateUpdate()
        {
            if (_riderCount == 0 || _belt == null)
                return;

            float dt = Time.deltaTime;
            float phase = _belt.Phase;
            for (int i = 0; i < _riderCount; i++)
            {
                ref Rider r = ref _riders[i];
                r.display = InterpolateBeltPosition(r.target, phase);

                Vector3 world = _belt.PositionToWorld(r.display);
                if (r.blend < 1f)
                {
                    r.blend = Mathf.Min(1f, r.blend + dt / EnterBlendSeconds);
                    world = Vector3.LerpUnclamped(r.blendFrom, world, r.blend);
                }

                r.view.SnapTo(world);
            }
        }

        private Vector3 RootPoint(Transform root, Vector3 offset)
        {
            Transform r = root != null ? root : transform;
            return r.position + r.rotation * offset;
        }

        private ColorTankView Spawn(ColorTankModel tank)
        {
            ColorTankView view = _pool.Get();
            view.Bind(tank, _session.Palette[tank.ColorId]);
            _views[tank.Id] = view;
            return view;
        }

        private float BeltTankScale => _belt != null ? _belt.TrackWidth * BeltTankFill : 1f;

        private void ShowSlotFrames(int count)
        {
            if (slotFramePrefab == null)
                return;

            if (_frameProps == null)
                _frameProps = new MaterialPropertyBlock();
            _frameProps.SetColor(BaseColorId, slotFrameColor);

            // Frames are created at level load only, when a level has more slots than any before it.
            while (_slotFrames.Count < count)
            {
                Transform frame = Instantiate(slotFramePrefab, transform);
                // Sprite frames are tinted through the sprite colour (a property block would fight the sprite's
                // own texture binding); mesh frames through _BaseColor.
                if (frame.TryGetComponent(out SpriteRenderer sprite))
                    sprite.color = slotFrameColor;
                else if (frame.TryGetComponent(out Renderer renderer))
                    renderer.SetPropertyBlock(_frameProps);
                _slotFrames.Add(frame);
            }

            for (int i = 0; i < _slotFrames.Count; i++)
            {
                bool shown = i < count;
                Transform frame = _slotFrames[i];
                frame.gameObject.SetActive(shown);
                if (shown)
                    frame.position = SlotPosition(i) + new Vector3(0f, 0f, SlotFrameDepth);
            }
        }

        private void UpdateCounter()
        {
            BeltModel belt = _session.Belt;
            _belt.SetCounter(belt.Count + belt.QueuedCount, belt.Capacity);
        }

        private int FindRider(ColorTankModel tank)
        {
            for (int i = 0; i < _riderCount; i++)
            {
                if (_riders[i].tank == tank)
                    return i;
            }
            return -1;
        }

        private void HandleLaneChanged(int lane)
        {
            IReadOnlyList<ColorTankModel> tanks = _session.Supply.GetLane(lane);
            for (int row = 0; row < tanks.Count; row++)
            {
                if (_views.TryGetValue(tanks[row].Id, out ColorTankView view))
                    view.MoveTo(LanePosition(lane, row));
            }
        }

        private void HandleTankAdded(ColorTankModel tank, int slot)
        {
            if (_views.TryGetValue(tank.Id, out ColorTankView view))
            {
                view.SetScale(1f);
                view.MoveTo(SlotPosition(slot));
            }
        }

        private void HandleTankRemoved(ColorTankModel tank, int oldIndex)
        {
            // A slot tank leaves its slot when it is relaunched (the belt events move it on). Slot tanks do not fire,
            // but a depleted one is still shrunk away for safety.
            if (tank.IsDepleted)
                StartDeplete(tank);

            SlotQueueManager slots = _session.Tray;
            for (int slot = oldIndex; slot < slots.Count; slot++)
            {
                if (_views.TryGetValue(slots[slot].Id, out ColorTankView shifted))
                    shifted.MoveTo(SlotPosition(slot));
            }
        }

        private void HandleTankQueued(ColorTankModel tank)
        {
            _queued.Add(tank);
            if (_views.TryGetValue(tank.Id, out ColorTankView view))
            {
                view.SetScale(BeltTankScale);
                view.MoveTo(_belt.EntrancePosition(_queued.Count - 1));
            }

            UpdateCounter();
        }

        private void HandleTankEntered(ColorTankModel tank)
        {
            int index = _queued.IndexOf(tank);
            if (index >= 0)
                _queued.RemoveAt(index);
            for (int i = 0; i < _queued.Count; i++)
            {
                if (_views.TryGetValue(_queued[i].Id, out ColorTankView waiting))
                    waiting.MoveTo(_belt.EntrancePosition(i));
            }

            if (_views.TryGetValue(tank.Id, out ColorTankView view) && _riderCount < _riders.Length)
            {
                _riders[_riderCount++] = new Rider
                {
                    tank = tank,
                    view = view,
                    target = 0,
                    display = EntrancePositionOffset,
                    blendFrom = view.transform.position,
                    blend = 0f,
                };
            }

            UpdateCounter();
        }

        private void HandleTankMoved(ColorTankModel tank, int position)
        {
            int rider = FindRider(tank);
            if (rider >= 0)
                _riders[rider].target = position;
        }

        private void HandleTankLeft(ColorTankModel tank, bool lapCompleted)
        {
            int rider = FindRider(tank);
            if (rider >= 0)
            {
                _riderCount--;
                _riders[rider] = _riders[_riderCount];
                _riders[_riderCount] = default;
            }

            // A lap-completed tank is moved by the waiting-slot event that follows (or shrinks on overflow, see
            // HandleOverflow); a depleted one shrinks in place.
            if (!lapCompleted)
                StartDeplete(tank);

            UpdateCounter();
        }

        private void HandleOverflow(ColorTankModel tank)
        {
            // No waiting slot took the tank: shrink it where it left the belt and return it to the pool.
            StartDeplete(tank);
        }

        private void StartDeplete(ColorTankModel tank)
        {
            if (!_views.TryGetValue(tank.Id, out ColorTankView view))
                return;

            _views.Remove(tank.Id);
            _depleting.Add(view);
            view.PlayDeplete(_onDepleteFinished);
        }

        private void HandleDepleteFinished(ColorTankView view)
        {
            int index = _depleting.IndexOf(view);
            if (index < 0)
                return;

            _depleting.RemoveAt(index);
            view.Unbind();
            _pool.Release(view);
        }

        private void Unsubscribe()
        {
            if (_session == null)
                return;

            _session.Supply.OnLaneChanged -= _onLaneChanged;
            _session.Tray.OnTankAdded -= _onTankAdded;
            _session.Tray.OnTankRemoved -= _onTankRemoved;
            _session.Belt.OnTankQueued -= _onTankQueued;
            _session.Belt.OnTankEntered -= _onTankEntered;
            _session.Belt.OnTankMoved -= _onTankMoved;
            _session.Belt.OnTankLeft -= _onTankLeft;
            _session.BeltShooting.OnOverflow -= _onOverflow;
        }

        private void EnsureInitialised()
        {
            if (_pool != null)
                return;

            _onLaneChanged = HandleLaneChanged;
            _onTankAdded = HandleTankAdded;
            _onTankRemoved = HandleTankRemoved;
            _onTankQueued = HandleTankQueued;
            _onTankEntered = HandleTankEntered;
            _onTankMoved = HandleTankMoved;
            _onTankLeft = HandleTankLeft;
            _onOverflow = HandleOverflow;
            _onDepleteFinished = HandleDepleteFinished;

            _pool = new ObjectPool<ColorTankView>(
                () =>
                {
                    ColorTankView created = Instantiate(tankPrefab, transform);
                    created.gameObject.SetActive(false);
                    return created;
                },
                v => v.gameObject.SetActive(true),
                v => v.gameObject.SetActive(false));
        }
    }
}
