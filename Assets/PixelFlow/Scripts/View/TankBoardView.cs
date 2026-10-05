using System;
using System.Collections.Generic;
using PixelFlow.Core;
using PixelFlow.Performance;
using UnityEngine;

namespace PixelFlow.View
{
    /// <summary>
    /// Presents every tank of a <see cref="LevelSession"/>: supply lanes and the tray. Views come from an
    /// <see cref="ObjectPool{T}"/> of <see cref="ColorTankView"/> and are mapped by tank id.
    /// <para>Layout (offsets applied in the root's rotation, root scale ignored):</para>
    /// <list type="bullet">
    /// <item>Supply: lane <c>i</c> is centred around <c>supplyRoot</c> on x at
    /// <c>(i - (laneCount - 1) / 2) * laneSpacing</c>; the lane front (row 0) sits at the root and row <c>r</c>
    /// is <c>r * rowSpacing</c> further down (-y).</item>
    /// <item>Tray: slot <c>s</c> is centred around <c>trayRoot</c> on x at
    /// <c>(s - (capacity - 1) / 2) * slotSpacing</c>.</item>
    /// </list>
    /// <see cref="Build"/> snaps views into place; later model changes (<see cref="SupplyModel.OnLaneChanged"/>,
    /// <see cref="SlotQueueManager.OnTankAdded"/>, <see cref="SlotQueueManager.OnTankRemoved"/>) tween them with
    /// <see cref="ColorTankView.MoveTo"/>. A removed (depleted) tank plays a short scale-down
    /// (<see cref="ColorTankView.PlayDeplete"/>) and is then unbound and returned to the pool; during that
    /// animation it can no longer be picked, but <see cref="GetMuzzle"/> still resolves it so its final shot can
    /// be launched.
    /// </summary>
    public sealed class TankBoardView : MonoBehaviour
    {
        [Tooltip("Tank view prefab. Instantiated only by the pool factory.")]
        [SerializeField] private ColorTankView tankPrefab;

        [Tooltip("Anchor of the supply lanes (front row, centred between lanes).")]
        [SerializeField] private Transform supplyRoot;

        [Tooltip("Anchor of the tray (centred between slots).")]
        [SerializeField] private Transform trayRoot;

        [Tooltip("Horizontal distance between supply lanes.")]
        [SerializeField] private float laneSpacing = 1.4f;

        [Tooltip("Vertical distance between rows in a supply lane.")]
        [SerializeField] private float rowSpacing = 1.2f;

        [Tooltip("Horizontal distance between tray slots.")]
        [SerializeField] private float slotSpacing = 1.2f;

        private readonly Dictionary<int, ColorTankView> _views = new Dictionary<int, ColorTankView>();
        private readonly List<ColorTankView> _depleting = new List<ColorTankView>();

        private ObjectPool<ColorTankView> _pool;
        private LevelSession _session;

        private Action<int> _onLaneChanged;
        private Action<ColorTankModel, int> _onTankAdded;
        private Action<ColorTankModel, int> _onTankRemoved;
        private Action<ColorTankView> _onDepleteFinished;

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
        /// supply lanes and in the tray, binds it with its palette colour, snaps it to its layout position and
        /// subscribes to supply and tray events.
        /// </summary>
        /// <param name="session">Level session to present.</param>
        /// <exception cref="ArgumentNullException">Thrown if session is null.</exception>
        public void Build(LevelSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            EnsureInitialised();
            Clear();
            _session = session;

            SupplyModel supply = session.Supply;
            for (int lane = 0; lane < supply.LaneCount; lane++)
            {
                IReadOnlyList<ColorTankModel> tanks = supply.GetLane(lane);
                for (int row = 0; row < tanks.Count; row++)
                    Spawn(tanks[row]).SnapTo(LanePosition(lane, row));
            }

            SlotQueueManager tray = session.Tray;
            for (int slot = 0; slot < tray.Count; slot++)
                Spawn(tray[slot]).SnapTo(SlotPosition(slot));

            supply.OnLaneChanged += _onLaneChanged;
            tray.OnTankAdded += _onTankAdded;
            tray.OnTankRemoved += _onTankRemoved;
        }

        /// <summary>
        /// Unsubscribes from the current session and returns every view (including depleting ones) to the pool.
        /// Safe to call when nothing is built.
        /// </summary>
        public void Clear()
        {
            Unsubscribe();
            _session = null;

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
        /// Resolves a raycast hit to a tank model. Returns true only for a view that is currently bound and not
        /// depleting; the caller decides whether the tank may be tapped (e.g. lane front only).
        /// </summary>
        /// <param name="hit">Collider returned by the raycast (may belong to a child of the tank view).</param>
        /// <param name="tank">The tank model on success, otherwise null.</param>
        /// <returns>True if <paramref name="hit"/> belongs to a live tank view of this board.</returns>
        public bool TryGetTank(Collider hit, out ColorTankModel tank)
        {
            tank = null;
            if (hit == null)
                return false;

            ColorTankView view = hit.GetComponentInParent<ColorTankView>();
            if (view == null || view.Model == null)
                return false;

            if (!_views.TryGetValue(view.Model.Id, out ColorTankView mapped) || mapped != view)
                return false;

            tank = view.Model;
            return true;
        }

        /// <summary>
        /// World-space muzzle of the view presenting <paramref name="tank"/>. Also resolves a tank that has just
        /// been removed from the tray and is still playing its depletion animation. Returns the tray root
        /// position (or this transform's position) if the tank has no view.
        /// </summary>
        /// <param name="tank">Tank model.</param>
        /// <returns>The muzzle position.</returns>
        public Vector3 GetMuzzle(ColorTankModel tank)
        {
            if (tank != null)
            {
                if (_views.TryGetValue(tank.Id, out ColorTankView view))
                    return view.MuzzlePosition;

                for (int i = 0; i < _depleting.Count; i++)
                {
                    if (_depleting[i].Model == tank)
                        return _depleting[i].MuzzlePosition;
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
        /// World-space position of tray slot <paramref name="slot"/>.
        /// </summary>
        /// <param name="slot">Slot index (0 = leftmost).</param>
        /// <returns>Layout position.</returns>
        public Vector3 SlotPosition(int slot)
        {
            int capacity = _session != null ? _session.Tray.Capacity : 1;
            var offset = new Vector3((slot - (capacity - 1) * 0.5f) * slotSpacing, 0f, 0f);
            return RootPoint(trayRoot, offset);
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
                view.MoveTo(SlotPosition(slot));
        }

        private void HandleTankRemoved(ColorTankModel tank, int oldIndex)
        {
            if (_views.TryGetValue(tank.Id, out ColorTankView view))
            {
                _views.Remove(tank.Id);
                _depleting.Add(view);
                view.PlayDeplete(_onDepleteFinished);
            }

            SlotQueueManager tray = _session.Tray;
            for (int slot = oldIndex; slot < tray.Count; slot++)
            {
                if (_views.TryGetValue(tray[slot].Id, out ColorTankView shifted))
                    shifted.MoveTo(SlotPosition(slot));
            }
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
        }

        private void EnsureInitialised()
        {
            if (_pool != null)
                return;

            _onLaneChanged = HandleLaneChanged;
            _onTankAdded = HandleTankAdded;
            _onTankRemoved = HandleTankRemoved;
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
