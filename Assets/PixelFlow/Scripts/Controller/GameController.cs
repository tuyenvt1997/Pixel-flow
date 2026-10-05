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
    /// tap input moves lane-front tanks into the tray, a fixed-interval timer runs
    /// <see cref="ShootingLogic.Step"/>, every shot becomes a projectile, arrivals are queued and destroyed
    /// under a per-frame budget, and the game ends (HUD popup) once the rules report Won/Lost and nothing
    /// is still in flight. Nothing is instantiated or allocated per frame; all handlers are cached delegates.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        /// <summary>
        /// Upper bound of shooting steps run in one frame when the frame time exceeds several fire intervals.
        /// </summary>
        public const int MaxFireStepsPerFrame = 8;

        private const int CellPoolPrewarm = 128;
        private const int DestroyQueueCapacity = 256;
        private const float RaycastDistance = 100f;

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

        [Tooltip("Supply lanes + tray presentation.")]
        [SerializeField] private TankBoardView tankBoard;

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
        [SerializeField] private Rect boardArea = new Rect(-4.5f, -1f, 9f, 9f);

        [Tooltip("Seconds between two shooting steps.")]
        [SerializeField] private float fireInterval = 0.06f;

        [Tooltip("Maximum number of cells destroyed per frame.")]
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
        private float _fireTimer;
        private int _currentIndex;
        private readonly Stopwatch _loadWatch = new Stopwatch();

        /// <summary>
        /// Current game state. <see cref="GameState.Won"/>/<see cref="GameState.Lost"/> are only set once the
        /// rules report them and no projectile or pending destroy is left.
        /// </summary>
        public GameState State { get; private set; }

        /// <summary>
        /// The session of the loaded level, or null before the first load.
        /// </summary>
        public LevelSession Session => _session;

        /// <summary>
        /// Index into the levels array of the level last loaded with <see cref="LoadLevel(int)"/>.
        /// </summary>
        public int CurrentLevelIndex => _currentIndex;

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
        }

        /// <summary>
        /// Loads <c>levels[index]</c>: clears projectiles, pending destroys, tank views and the board, creates a
        /// fresh <see cref="LevelSession"/>, fits and builds the board and tanks, resets the HUD and sets
        /// <see cref="State"/> to <see cref="GameState.Playing"/>. The load time is logged.
        /// </summary>
        /// <param name="index">Index into the levels array.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if index is outside the levels array.</exception>
        public void LoadLevel(int index)
        {
            if (levels == null || index < 0 || index >= levels.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            _currentIndex = index;
            LoadLevel(levels[index]);
        }

        /// <summary>
        /// Loads an arbitrary level (e.g. one created at runtime by a test) with the same steps as
        /// <see cref="LoadLevel(int)"/>. Retry reloads this level; the HUD label shows
        /// <see cref="CurrentLevelIndex"/> + 1.
        /// </summary>
        /// <param name="data">Level to load.</param>
        /// <exception cref="ArgumentNullException">Thrown if data is null.</exception>
        public void LoadLevel(LevelData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            _loadWatch.Restart();

            projectiles.ClearAll();
            _destroyQueue.Clear();
            ReleaseActiveCells();
            if (debris != null)
                debris.ClearParticles();
            tankBoard.Clear();
            gridRenderer.Clear();

            _currentData = data;
            _session = LevelSession.Create(data);
            _layout = BoardLayout.Fit(_session.Width, _session.Height, boardArea);
            gridRenderer.Build(_session.Grid, _session.Palette, _layout);
            tankBoard.Build(_session);
            hud.SetLevel(_currentIndex + 1);
            hud.HideAll();
            _fireTimer = 0f;
            State = GameState.Playing;

            _loadWatch.Stop();
            LastLoadMilliseconds = _loadWatch.Elapsed.TotalMilliseconds;
            Debug.Log($"[PixelFlow] Loaded level '{data.name}' ({_session.Width}x{_session.Height}, " +
                      $"{_session.Grid.RemainingCount} pixels) in {LastLoadMilliseconds:F2} ms.");
        }

        /// <summary>
        /// Moves the front tank of <paramref name="lane"/> into the tray. This is what a tap on a lane-front
        /// tank does once the raycast has resolved the lane.
        /// </summary>
        /// <param name="lane">Supply lane index.</param>
        /// <returns>False if the game is not playing, the lane is invalid or empty, or the tray is full.</returns>
        public bool TryActivateLane(int lane)
        {
            if (State != GameState.Playing || _session == null)
                return false;
            if (lane < 0 || lane >= _session.Supply.LaneCount)
                return false;
            if (_session.Tray.IsFull || _session.Supply.PeekFront(lane) == null)
                return false;

            _session.Supply.TryTakeFront(lane, out ColorTankModel tank);
            return _session.Tray.TryAdd(tank);
        }

        private void Update()
        {
            if (State != GameState.Playing || _session == null)
                return;

            HandleInput();
            RunFireTimer(Time.deltaTime);
            _destroyQueue.Process(destroysPerFrame, _handleDestroy);

            GameState rules = GameRules.Evaluate(_session.Grid, _session.Tray, _session.Supply, _session.Shooting);
            if (rules != GameState.Playing && projectiles.ActiveCount == 0 && _destroyQueue.Count == 0)
            {
                State = rules;
                if (rules == GameState.Won)
                    hud.ShowWin();
                else
                    hud.ShowLose();
            }
        }

        private void HandleInput()
        {
            Pointer pointer = Pointer.current;
            if (pointer == null || cam == null || !pointer.press.wasPressedThisFrame)
                return;

            Ray ray = cam.ScreenPointToRay(pointer.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, RaycastDistance))
                return;
            if (!tankBoard.TryGetTank(hit.collider, out ColorTankModel tank))
                return;

            int lane = _session.Supply.FindLaneWithFront(tank);
            if (lane >= 0)
                TryActivateLane(lane);
        }

        private void RunFireTimer(float deltaTime)
        {
            _fireTimer += deltaTime;
            int steps = 0;
            while (_fireTimer >= fireInterval && steps < MaxFireStepsPerFrame)
            {
                _fireTimer -= fireInterval;
                steps++;
                Fire();
            }

            if (steps == MaxFireStepsPerFrame && _fireTimer > fireInterval)
                _fireTimer = 0f; // drop the backlog after a long frame instead of catching up forever
        }

        private void Fire()
        {
            _shots.Clear();
            _session.Shooting.Step(_shots);
            Color32[] palette = _session.Palette;
            for (int i = 0; i < _shots.Count; i++)
            {
                ShotEvent shot = _shots[i];
                projectiles.Launch(tankBoard.GetMuzzle(shot.Tank), _layout.CellToWorld(shot.Cell), shot.Cell,
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
