using System;
using System.Collections.Generic;
using System.Diagnostics;
using PixelFlow.Core;
using PixelFlow.Data;
using PixelFlow.Performance;
using PixelFlow.View;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

namespace PixelFlow.Controller
{
    /// <summary>
    /// Wires the Core model to the View layer and drives one level at a time:
    /// tapping a lane-front or waiting-slot tank launches it onto the conveyor belt, a belt timer runs
    /// <see cref="BeltShootingLogic.Tick"/> every <c>lapSeconds / L</c> seconds (one lap takes <c>lapSeconds</c> on
    /// every board size) and pushes the belt clock (ticks plus tick phase, <see cref="BeltView.SetBeltClock"/>) the
    /// belt tanks and chevrons are animated with, every shot becomes a projectile, arrivals are queued and destroyed under a per-frame
    /// budget, and the game ends (HUD popup) once the belt rules report Won/Lost and nothing is still in flight.
    /// Nothing is instantiated or allocated per frame; all handlers are cached delegates.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        /// <summary>
        /// Upper bound of belt ticks run in one frame when the frame time exceeds several tick intervals.
        /// </summary>
        public const int MaxTicksPerFrame = 16;

        private const int CellPoolPrewarm = 128;
        private const int DestroyQueueCapacity = 256;
        private const float RaycastDistance = 100f;
        private const float MinLapSeconds = 0.5f;
        private const float MaxBeltPhase = 0.9999f;

        /// <summary>
        /// One arrived projectile waiting for its cell to be destroyed.
        /// </summary>
        private struct PendingDestroy
        {
            public Vector2Int cell;
            public Color32 color;
        }

        [Tooltip("Levels in play order. LoadLevel(index) indexes into this array.")]
        [SerializeField] private LevelData[] levels;

        [Tooltip("Instanced board renderer.")]
        [SerializeField] private PixelGridRenderer gridRenderer;

        [Tooltip("Supply lanes, belt tanks and waiting slots presentation.")]
        [SerializeField] private TankBoardView tankBoard;

        [Tooltip("Conveyor belt track and counter.")]
        [SerializeField] private BeltView beltView;

        [Tooltip("Pooled projectile system.")]
        [SerializeField] private ProjectileSystem projectiles;

        [Tooltip("Prefab of the shrinking cube played when a cell is destroyed.")]
        [SerializeField] private PixelCellView cellViewPrefab;

        [Tooltip("Shared particle burst for destroyed cells.")]
        [SerializeField] private DebrisFx debris;

        [Tooltip("Level label and win/lose popups.")]
        [SerializeField] private GameHudView hud;

        [Tooltip("Camera used for tap raycasts.")]
        [SerializeField] private Camera cam;

        [Tooltip("World-space rectangle (XY plane) the board is fitted into.")]
        [SerializeField] private Rect boardArea = new Rect(-4f, -1.2f, 8f, 8f);

        [Tooltip("Seconds one belt lap takes on every board size; the tick interval is lapSeconds / belt length.")]
        [Min(MinLapSeconds)]
        [SerializeField] private float lapSeconds = 4f;

        [Tooltip("Maximum number of cells destroyed per frame.")]
        [Min(1)]
        [SerializeField] private int destroysPerFrame = 64;

        private ObjectPool<PixelCellView> _cellPool;
        private TimeSlicedQueue<PendingDestroy> _destroyQueue;
        private List<ShotEvent> _shots;
        private readonly List<PixelCellView> _activeCells = new List<PixelCellView>(CellPoolPrewarm);

        private Action<Vector2Int, Color32> _onArrived;
        private Action<PendingDestroy> _handleDestroy;
        private Action<PixelCellView> _onCellFinished;
        private Action _onRetry;
        private Action _onNext;

        private LevelSession _session;
        private LevelData _currentData;
        private BoardLayout _layout;
        private float _tickTimer;
        private float _tickInterval;
        private int _beltTicks;
        private int _currentIndex;
        private GameState _result;
        private readonly Stopwatch _loadWatch = new Stopwatch();

        /// <summary>
        /// Current game state. <see cref="GameState.Won"/>/<see cref="GameState.Lost"/> are only set once the
        /// rules report them and no projectile, pending destroy or cell destroy animation is left.
        /// </summary>
        public GameState State { get; private set; }

        /// <summary>
        /// True between the moment the rules first report Won or Lost and the moment <see cref="State"/> takes that
        /// result (once nothing is in flight any more). The result is latched: the belt no longer ticks and no
        /// launch or tap is accepted.
        /// </summary>
        public bool IsResolving => State == GameState.Playing && _result != GameState.Playing;

        /// <summary>
        /// The session of the loaded level, or null before the first load.
        /// </summary>
        public LevelSession Session => _session;

        /// <summary>
        /// Index into the levels array of the level last loaded with <see cref="LoadLevel(int)"/>.
        /// </summary>
        public int CurrentLevelIndex => _currentIndex;

        /// <summary>
        /// Number of levels in the levels array (0 if none are assigned).
        /// </summary>
        public int LevelCount => levels != null ? levels.Length : 0;

        /// <summary>
        /// The level data of the loaded level (the asset or the runtime instance passed to
        /// <see cref="LoadLevel(LevelData)"/>), or null before the first load.
        /// </summary>
        public LevelData CurrentLevel => _currentData;

        /// <summary>
        /// Number of projectiles currently in flight.
        /// </summary>
        public int ActiveProjectiles => projectiles != null ? projectiles.ActiveCount : 0;

        /// <summary>
        /// Number of arrived projectiles whose cell has not been destroyed yet.
        /// </summary>
        public int PendingDestroys => _destroyQueue != null ? _destroyQueue.Count : 0;

        /// <summary>
        /// Wall-clock duration in milliseconds of the last level load (model + views).
        /// </summary>
        public double LastLoadMilliseconds { get; private set; }

        private void Awake()
        {
            Application.targetFrameRate = 60;

            // [Min] only guards the inspector; also clamp values set by other means (e.g. scripts, YAML).
            lapSeconds = Mathf.Max(lapSeconds, MinLapSeconds);
            destroysPerFrame = Mathf.Max(destroysPerFrame, 1);

            _onArrived = HandleArrived;
            _handleDestroy = HandleDestroy;
            _onCellFinished = HandleCellFinished;
            _onRetry = HandleRetry;
            _onNext = HandleNext;

            Transform cellParent = transform;
            _cellPool = new ObjectPool<PixelCellView>(
                () =>
                {
                    PixelCellView view = Instantiate(cellViewPrefab, cellParent);
                    view.gameObject.SetActive(false);
                    return view;
                },
                v => v.gameObject.SetActive(true),
                v => v.gameObject.SetActive(false),
                CellPoolPrewarm);
            _destroyQueue = new TimeSlicedQueue<PendingDestroy>(DestroyQueueCapacity);
            _shots = new List<ShotEvent>(16);

            projectiles.OnArrived += _onArrived;
            hud.OnRetryClicked += _onRetry;
            hud.OnNextClicked += _onNext;
        }

        private void Start()
        {
            if (_session == null && levels != null && levels.Length > 0)
                LoadLevel(0);
        }

        private void OnDestroy()
        {
            if (projectiles != null)
                projectiles.OnArrived -= _onArrived;
            if (hud != null)
            {
                hud.OnRetryClicked -= _onRetry;
                hud.OnNextClicked -= _onNext;
            }
            if (tankBoard != null)
                tankBoard.Clear();
            if (beltView != null)
                beltView.Clear();
        }

        /// <summary>
        /// Loads <c>levels[index]</c>: creates a fresh <see cref="LevelSession"/> (validating the data) first, then
        /// clears projectiles, pending destroys, tank views, the belt and the board, fits and builds the board, belt and tanks,
        /// resets the HUD and sets <see cref="State"/> to <see cref="GameState.Playing"/>. The load time is logged.
        /// If the level data is invalid nothing changes: the previous level stays loaded and playable.
        /// </summary>
        /// <param name="index">Index into the levels array.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if index is outside the levels array.</exception>
        /// <exception cref="ArgumentException">Thrown if the level data is invalid (see <see cref="LevelSession.Create"/>).</exception>
        public void LoadLevel(int index)
        {
            if (levels == null || index < 0 || index >= levels.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            Load(levels[index], index);
        }

        /// <summary>
        /// Loads an arbitrary level (e.g. one created at runtime by a test) with the same steps as
        /// <see cref="LoadLevel(int)"/>. Retry reloads this level; the HUD label shows
        /// <see cref="CurrentLevelIndex"/> + 1.
        /// </summary>
        /// <param name="data">Level to load.</param>
        /// <exception cref="ArgumentNullException">Thrown if data is null.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown if the level data is invalid (see <see cref="LevelSession.Create"/>); the previous level stays loaded.
        /// </exception>
        public void LoadLevel(LevelData data)
        {
            Load(data, _currentIndex);
        }

        private void Load(LevelData data, int index)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            _loadWatch.Restart();

            // Validate and build the model first: if the data is invalid this throws before anything of the
            // current level is torn down, so the previous level stays loaded and playable.
            LevelSession session = LevelSession.Create(data);

            _currentIndex = index;
            projectiles.ClearAll();
            _destroyQueue.Clear();
            ReleaseActiveCells();
            if (debris != null)
                debris.ClearParticles();
            tankBoard.Clear();
            beltView.Clear();
            gridRenderer.Clear();

            _currentData = data;
            _session = session;
            _layout = BoardLayout.Fit(_session.Width, _session.Height, boardArea);
            BeltPath path = _session.Belt.Path;
            _tickInterval = lapSeconds / path.Length;
            gridRenderer.Build(_session.Grid, _session.Palette, _layout);
            beltView.Build(path, _layout);
            tankBoard.Build(_session, beltView);
            hud.SetLevel(_currentIndex + 1);
            hud.HideAll();
            _tickTimer = 0f;
            _beltTicks = 0;
            _result = GameState.Playing;
            State = GameState.Playing;

            _loadWatch.Stop();
            LastLoadMilliseconds = _loadWatch.Elapsed.TotalMilliseconds;
            Debug.Log($"[PixelFlow] Loaded level '{data.name}' ({_session.Width}x{_session.Height}, " +
                      $"{_session.Grid.RemainingCount} pixels) in {LastLoadMilliseconds:F2} ms.");
        }

        /// <summary>
        /// Launches the front tank of <paramref name="lane"/> onto the belt (entrance queue). This is what a tap on
        /// a lane-front tank does once the raycast has resolved the lane. The belt capacity is checked before the
        /// tank leaves its lane, so a rejected launch changes nothing.
        /// </summary>
        /// <param name="lane">Supply lane index.</param>
        /// <returns>
        /// False if the game is not playing or is resolving (see <see cref="IsResolving"/>),
        /// the lane is invalid or empty, or the belt is full.
        /// </returns>
        public bool TryLaunchFromLane(int lane)
        {
            if (State != GameState.Playing || IsResolving || _session == null)
                return false;
            if (lane < 0 || lane >= _session.Supply.LaneCount)
                return false;
            if (_session.Belt.IsFull || _session.Supply.PeekFront(lane) == null)
                return false;

            _session.Supply.TryTakeFront(lane, out ColorTankModel tank);
            return _session.Belt.TryLaunch(tank);
        }

        /// <summary>
        /// Relaunches the tank in waiting slot <paramref name="slot"/> onto the belt (entrance queue); the tanks
        /// after it shift one slot left. This is what a tap on a waiting-slot tank does. The belt capacity is
        /// checked before the tank leaves its slot, so a rejected launch changes nothing.
        /// </summary>
        /// <param name="slot">Waiting slot index (0 = leftmost).</param>
        /// <returns>
        /// False if the game is not playing or is resolving (see <see cref="IsResolving"/>),
        /// the slot is empty or invalid, or the belt is full.
        /// </returns>
        public bool TryLaunchFromSlot(int slot)
        {
            if (State != GameState.Playing || IsResolving || _session == null)
                return false;
            if (slot < 0 || slot >= _session.Tray.Count || _session.Belt.IsFull)
                return false;

            ColorTankModel tank = _session.Tray[slot];
            _session.Tray.TryRemove(tank);
            return _session.Belt.TryLaunch(tank);
        }

        private void Update()
        {
            if (State != GameState.Playing || _session == null)
                return;

            // Once the rules report a result it is latched: the belt stops and input is ignored while the
            // projectiles, the destroy queue and the shrinking cells drain.
            if (_result == GameState.Playing)
            {
                HandleInput();
                RunBeltTimer(Time.deltaTime);
                PushBeltClock();
                if (_result == GameState.Playing)
                    _result = EvaluateRules();
            }

            _destroyQueue.Process(destroysPerFrame, _handleDestroy);

            if (_result != GameState.Playing && projectiles.ActiveCount == 0 && _destroyQueue.Count == 0 &&
                _activeCells.Count == 0)
            {
                State = _result;
                if (_result == GameState.Won)
                    hud.ShowWin();
                else
                    hud.ShowLose();
            }
        }

        private GameState EvaluateRules()
        {
            return GameRules.Evaluate(_session.Grid, _session.Belt, _session.Tray, _session.Supply,
                _session.BeltShooting);
        }

        private void HandleInput()
        {
            Pointer pointer = Pointer.current;
            if (pointer == null || cam == null || !pointer.press.wasPressedThisFrame)
                return;

            Ray ray = cam.ScreenPointToRay(pointer.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, RaycastDistance))
                return;
            if (!tankBoard.TryGetTank(hit.collider, out ColorTankModel tank, out TankLocation where))
                return;

            if (where == TankLocation.LaneFront)
            {
                int lane = _session.Supply.FindLaneWithFront(tank);
                if (lane >= 0)
                    TryLaunchFromLane(lane);
            }
            else if (where == TankLocation.Slot)
            {
                int slot = _session.Tray.IndexOf(tank);
                if (slot >= 0)
                    TryLaunchFromSlot(slot);
            }
        }

        private void RunBeltTimer(float deltaTime)
        {
            _tickTimer += deltaTime;
            int ticks = 0;
            while (_tickTimer >= _tickInterval && ticks < MaxTicksPerFrame)
            {
                _tickTimer -= _tickInterval;
                ticks++;
                if (++_beltTicks >= _session.Belt.Path.Length)
                    _beltTicks = 0;
                Tick();

                // Latch the result on the tick that produced it, so later catch-up ticks of this frame cannot
                // change it (e.g. a lap overflowing after the board was cleared).
                _result = EvaluateRules();
                if (_result != GameState.Playing)
                    return;
            }

            if (ticks == MaxTicksPerFrame && _tickTimer > _tickInterval)
                _tickTimer = 0f; // drop the backlog after a long frame instead of catching up forever
        }

        /// <summary>
        /// Gives the views the belt clock: completed ticks (modulo the belt length) plus the elapsed fraction of the
        /// current tick interval, clamped below 1 so a backlog left by a latching tick cannot run a tank past its
        /// model position. Called every frame the belt runs; once the result is latched it is no longer called, which
        /// freezes the belt tanks and chevrons.
        /// </summary>
        private void PushBeltClock()
        {
            float phase = Mathf.Clamp(_tickTimer / _tickInterval, 0f, MaxBeltPhase);
            beltView.SetBeltClock(_beltTicks + phase);
        }

        private void Tick()
        {
            _shots.Clear();
            _session.BeltShooting.Tick(_shots);
            Color32[] palette = _session.Palette;
            for (int i = 0; i < _shots.Count; i++)
            {
                ShotEvent shot = _shots[i];
                // The tank fired from shot.BeltPosition; it may already have advanced, lapped into a slot or depleted.
                projectiles.Launch(beltView.PositionToWorld(shot.BeltPosition), _layout.CellToWorld(shot.Cell), shot.Cell,
                    palette[shot.ColorId]);
            }
        }

        private void HandleArrived(Vector2Int cell, Color32 color)
        {
            _destroyQueue.Enqueue(new PendingDestroy { cell = cell, color = color });
        }

        private void HandleDestroy(PendingDestroy item)
        {
            gridRenderer.HideCell(item.cell);
            PixelCellView view = _cellPool.Get();
            _activeCells.Add(view);
            view.Play(_layout.CellToWorld(item.cell), item.color, _layout.CellSize * PixelGridRenderer.CubeScaleFactor,
                debris, _onCellFinished);
        }

        private void HandleCellFinished(PixelCellView view)
        {
            int index = _activeCells.IndexOf(view);
            if (index < 0)
                return;

            int last = _activeCells.Count - 1;
            _activeCells[index] = _activeCells[last];
            _activeCells.RemoveAt(last);
            _cellPool.Release(view);
        }

        private void ReleaseActiveCells()
        {
            // Inactive views stop updating; Play() fully resets a view when it is reused.
            for (int i = 0; i < _activeCells.Count; i++)
                _cellPool.Release(_activeCells[i]);
            _activeCells.Clear();
        }

        private void HandleRetry()
        {
            if (_currentData != null)
                LoadLevel(_currentData);
        }

        private void HandleNext()
        {
            if (levels == null || levels.Length == 0)
                return;
            LoadLevel((_currentIndex + 1) % levels.Length);
        }
    }
}
