using System.Collections;
using System.IO;
using NUnit.Framework;
using PixelFlow.Controller;
using PixelFlow.Core;
using PixelFlow.Data;
using PixelFlow.View;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace PixelFlow.Tests
{
    /// <summary>
    /// End-to-end tests of the generated Game scene: auto-play to a win, a forced loss, retry while projectiles
    /// are in flight, and the level load time budget.
    /// </summary>
    public sealed class GameControllerPlayTests
    {
        private const string ScenePath = "Assets/PixelFlow/Scenes/Game.unity";
        private const int Level001Cells = 64 * 64;

        private const string BoardShaderName = "PixelFlow/InstancedColor";

        /// <summary>Seconds to wait after a tap so lane/tray move tweens (0.18 s) have finished.</summary>
        private const float TweenSettleSeconds = 0.3f;

        private GameController _controller;
        private GameHudView _hud;
        private PixelGridRenderer _grid;

        private Touchscreen _touchscreen;
        private int _touchId;
        private bool _inputSettingsChanged;
        private InputSettings.BackgroundBehavior _savedBackgroundBehavior;
        private InputSettings.EditorInputBehaviorInPlayMode _savedEditorBehavior;

        /// <summary>
        /// Restores the time scale changed by the auto-play test, and removes the touchscreen and restores the
        /// input settings changed by the tap test.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;

            if (_touchscreen != null)
            {
                InputSystem.RemoveDevice(_touchscreen);
                _touchscreen = null;
            }

            if (_inputSettingsChanged)
            {
                InputSystem.settings.backgroundBehavior = _savedBackgroundBehavior;
                InputSystem.settings.editorInputBehaviorInPlayMode = _savedEditorBehavior;
                _inputSettingsChanged = false;
            }
        }

        /// <summary>
        /// Drives the real input path (<c>Pointer.current</c> press, camera raycast, tank lookup, lane-front check)
        /// with synthesized touches on a test touchscreen: tapping a non-front tank does nothing, tapping the front
        /// of lane 0 moves that tank into the tray, and once the tray is full a lane-front tap is ignored.
        /// </summary>
        [UnityTest]
        public IEnumerator Tap_LaneFront_MovesTankToTray_NonFrontAndFullTrayIgnored()
        {
            yield return LoadGameScene();
            LevelData level = CreateTapLevel();
            _controller.LoadLevel(level);
            TankBoardView board = Object.FindFirstObjectByType<TankBoardView>();
            Camera cam = Camera.main;
            Assert.IsNotNull(board, "TankBoardView missing.");
            Assert.IsNotNull(cam, "Main camera missing.");
            SetUpTouchscreen();
            LevelSession s = _controller.Session;
            yield return new WaitForSeconds(TweenSettleSeconds);

            // Non-front tank (lane 1, row 1): ignored.
            ColorTankModel lane1Front = s.Supply.PeekFront(1);
            yield return Tap(cam, board.LanePosition(1, 1));
            Assert.AreEqual(0, s.Tray.Count, "Tapping a non-front tank moved a tank into the tray.");
            Assert.AreEqual(8, s.Supply.TotalRemaining);
            Assert.AreSame(lane1Front, s.Supply.PeekFront(1));

            // Front of lane 0: moves into slot 0.
            ColorTankModel lane0Front = s.Supply.PeekFront(0);
            yield return Tap(cam, board.LanePosition(0, 0));
            Assert.AreEqual(1, s.Tray.Count, "Tapping the lane-0 front tank did not move it into the tray.");
            Assert.AreSame(lane0Front, s.Tray[0]);
            Assert.AreEqual(7, s.Supply.TotalRemaining);
            yield return new WaitForSeconds(TweenSettleSeconds);

            // Fill the tray by tapping lane fronts (alternating lanes so both stay non-empty).
            for (int i = 1; i < 5; i++)
            {
                int lane = i % 2;
                yield return Tap(cam, board.LanePosition(lane, 0));
                Assert.AreEqual(i + 1, s.Tray.Count, $"Tap {i} on lane {lane} front did not add a tank.");
                yield return new WaitForSeconds(TweenSettleSeconds);
            }
            Assert.IsTrue(s.Tray.IsFull);
            Assert.AreEqual(GameState.Playing, _controller.State, "Tray tanks must still be able to fire.");

            // Tray full: a lane-front tap is ignored and the tank stays in its lane.
            ColorTankModel front = s.Supply.PeekFront(1);
            Assert.IsNotNull(front);
            yield return Tap(cam, board.LanePosition(1, 0));
            Assert.AreEqual(5, s.Tray.Count);
            Assert.AreEqual(3, s.Supply.TotalRemaining, "A tank left its lane while the tray was full.");
            Assert.AreSame(front, s.Supply.PeekFront(1));

            Object.Destroy(level);
        }

        /// <summary>
        /// A tank that has just entered the tray (its view still tweening away from the lane) reports its muzzle at
        /// its tray slot, so a shot fired in that frame starts from the slot rather than from the supply lane.
        /// </summary>
        [UnityTest]
        public IEnumerator TankJustAddedToTray_MuzzleIsAtSlot()
        {
            yield return LoadGameScene();
            LevelData level = CreateTapLevel();
            _controller.LoadLevel(level);
            TankBoardView board = Object.FindFirstObjectByType<TankBoardView>();
            yield return new WaitForSeconds(TweenSettleSeconds);

            ColorTankModel tank = _controller.Session.Supply.PeekFront(0);
            Vector3 lanePos = board.LanePosition(0, 0);
            Vector3 muzzleOffset = board.GetMuzzle(tank) - lanePos;

            Assert.IsTrue(_controller.TryActivateLane(0));
            Vector3 muzzle = board.GetMuzzle(tank);
            Vector3 expected = board.SlotPosition(0) + muzzleOffset;
            Assert.Less(Vector3.Distance(expected, muzzle), 1e-3f, $"Muzzle {muzzle} is not at slot 0 ({expected}).");
            Assert.Greater(Vector3.Distance(lanePos + muzzleOffset, muzzle), 1f, "Muzzle still at the lane.");

            Object.Destroy(level);
        }

        /// <summary>
        /// Loading invalid level data throws without touching the current level: the same session, board, tank
        /// views and level index remain, and the level keeps playing.
        /// </summary>
        [UnityTest]
        public IEnumerator LoadLevel_InvalidData_LeavesPreviousLevelPlayable()
        {
            yield return LoadGameScene();
            TankBoardView board = Object.FindFirstObjectByType<TankBoardView>();
            LevelSession before = _controller.Session;
            int views = board.ActiveViewCount;

            LevelData bad = CreateBlockedLevel();
            bad.cells = new byte[] { 0, 7 }; // colour 7 is outside the 2-colour palette
            Assert.Throws<System.ArgumentException>(() => _controller.LoadLevel(bad));

            Assert.AreSame(before, _controller.Session, "Session replaced by a failed load.");
            Assert.AreEqual(GameState.Playing, _controller.State);
            Assert.AreEqual(0, _controller.CurrentLevelIndex);
            Assert.AreEqual(Level001Cells, _grid.InstanceCount, "Board cleared by a failed load.");
            Assert.AreEqual(views, board.ActiveViewCount, "Tank views cleared by a failed load.");

            Assert.IsTrue(_controller.TryActivateLane(0), "Previous level no longer playable.");
            float start = Time.realtimeSinceStartup;
            while (_controller.Session.Grid.RemainingCount == Level001Cells)
            {
                Assert.Less(Time.realtimeSinceStartup - start, 5f, "No pixel destroyed after the failed load.");
                yield return null;
            }

            Object.Destroy(bad);
        }

        /// <summary>
        /// The pooled destroy-animation cube uses the board's shader, so a cell does not change brightness when the
        /// instanced cell is hidden and the cube takes over.
        /// </summary>
        [UnityTest]
        public IEnumerator CellDestroyCube_UsesBoardShader()
        {
            yield return LoadGameScene();
            Assert.IsTrue(_controller.TryActivateLane(0));

            PixelCellView cell = null;
            float start = Time.realtimeSinceStartup;
            while (cell == null)
            {
                Assert.Less(Time.realtimeSinceStartup - start, 5f, "No destroy animation started.");
                yield return null;
                cell = Object.FindFirstObjectByType<PixelCellView>(FindObjectsInactive.Exclude);
            }

            Material material = cell.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.AreEqual(BoardShaderName, material.shader.name);
        }

        /// <summary>
        /// Plays Level_001 with the "smallest lane-front id first" strategy until it is won, and writes a
        /// mid-game capture of the game camera to Logs/game_capture.png.
        /// </summary>
        [UnityTest]
        public IEnumerator Level001_AutoPlay_ReachesWon()
        {
            yield return LoadGameScene();
            Time.timeScale = 8f;

            float start = Time.realtimeSinceStartup;
            bool captured = false;
            while (_controller.State == GameState.Playing)
            {
                Assert.Less(Time.realtimeSinceStartup - start, 180f, "Level_001 was not won within 180 s.");

                LevelSession s = _controller.Session;
                if (!s.Shooting.CanAnyTankFire() && !s.Tray.IsFull)
                    _controller.TryActivateLane(SmallestFrontLane(s));

                if (!captured && s.Grid.RemainingCount < Level001Cells * 3 / 4 && _controller.ActiveProjectiles > 0)
                {
                    CaptureCamera();
                    captured = true;
                }

                yield return null;
            }

            Assert.AreEqual(GameState.Won, _controller.State);
            Assert.AreEqual(0, _controller.Session.Grid.RemainingCount);
            Assert.IsTrue(Panel("WinPanel").activeSelf, "Win popup not shown.");
            Assert.IsFalse(Panel("LosePanel").activeSelf, "Lose popup shown on a win.");
            Debug.Log($"[PlayTests] Level_001 won in {Time.realtimeSinceStartup - start:F1} s real time.");
        }

        /// <summary>
        /// Fills the tray with five tanks whose colour is not exposed: the game must end in a loss with the
        /// lose popup shown; retry restores a playable level.
        /// </summary>
        [UnityTest]
        public IEnumerator FillTrayWithUnfireableTanks_ReachesLost()
        {
            yield return LoadGameScene();
            LevelData level = CreateBlockedLevel();
            _controller.LoadLevel(level);

            for (int i = 0; i < 5; i++)
                Assert.IsTrue(_controller.TryActivateLane(0), $"Activation {i} failed.");
            Assert.IsFalse(_controller.TryActivateLane(0), "Tray accepted a sixth tank.");

            float start = Time.realtimeSinceStartup;
            while (_controller.State == GameState.Playing)
            {
                Assert.Less(Time.realtimeSinceStartup - start, 5f, "Lost state not reached.");
                yield return null;
            }

            Assert.AreEqual(GameState.Lost, _controller.State);
            Assert.IsTrue(Panel("LosePanel").activeSelf, "Lose popup not shown.");
            Assert.IsFalse(Panel("WinPanel").activeSelf, "Win popup shown on a loss.");

            RetryButton().onClick.Invoke();
            Assert.AreEqual(GameState.Playing, _controller.State);
            Assert.IsFalse(Panel("LosePanel").activeSelf, "Lose popup still shown after retry.");
            Assert.AreEqual(0, _controller.Session.Tray.Count);
            Assert.AreEqual(6, _controller.Session.Supply.TotalRemaining);

            Object.Destroy(level);
        }

        /// <summary>
        /// Retry while projectiles are in flight: no projectile or pending destroy survives, the board is fully
        /// rebuilt and stays intact (no old projectile hides a new cell).
        /// </summary>
        [UnityTest]
        public IEnumerator Retry_MidFlight_ClearsProjectilesAndQueue()
        {
            yield return LoadGameScene();

            for (int lane = 0; lane < 3; lane++)
                Assert.IsTrue(_controller.TryActivateLane(lane));

            float start = Time.realtimeSinceStartup;
            while (_controller.ActiveProjectiles == 0 || _controller.Session.Grid.RemainingCount > Level001Cells - 20)
            {
                Assert.Less(Time.realtimeSinceStartup - start, 10f, "No projectiles in flight.");
                yield return null;
            }
            Assert.Greater(_controller.ActiveProjectiles, 0);

            RetryButton().onClick.Invoke();

            Assert.AreEqual(0, _controller.ActiveProjectiles);
            Assert.AreEqual(0, _controller.PendingDestroys);
            Assert.AreEqual(Level001Cells, _grid.InstanceCount);
            Assert.AreEqual(Level001Cells, _controller.Session.Grid.RemainingCount);
            Assert.AreEqual(0, _controller.Session.Tray.Count);

            for (int i = 0; i < 30; i++)
                yield return null;

            Assert.AreEqual(0, _controller.ActiveProjectiles);
            Assert.AreEqual(0, _controller.PendingDestroys);
            Assert.AreEqual(Level001Cells, _controller.Session.Grid.RemainingCount);
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    Matrix4x4 m = _grid.GetInstanceMatrix(new Vector2Int(x, y));
                    Assert.Greater(m.GetColumn(0).magnitude, 0f, $"Cell ({x},{y}) hidden after retry.");
                }
            }

            var playingCells = Object.FindObjectsByType<PixelCellView>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Assert.AreEqual(0, playingCells.Length, "Destroy animations from the previous attempt still active.");
        }

        /// <summary>
        /// Level_001 loads (model + board + tanks + HUD) in well under a second, both on scene start and on reload.
        /// </summary>
        [UnityTest]
        public IEnumerator LevelLoad_Under1000ms()
        {
            yield return LoadGameScene();
            double first = _controller.LastLoadMilliseconds;

            _controller.LoadLevel(0);
            double reload = _controller.LastLoadMilliseconds;

            Debug.Log($"[PlayTests] Level_001 load: first {first:F2} ms, reload {reload:F2} ms.");
            Assert.Less(first, 1000.0);
            Assert.Less(reload, 1000.0);
            yield return null;
        }

        private IEnumerator LoadGameScene()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(ScenePath);
#endif
            yield return null;
            yield return null;

            _controller = Object.FindFirstObjectByType<GameController>();
            _hud = Object.FindFirstObjectByType<GameHudView>();
            _grid = Object.FindFirstObjectByType<PixelGridRenderer>();
            Assert.IsNotNull(_controller, "GameController missing from Game scene.");
            Assert.IsNotNull(_hud, "GameHudView missing from Game scene.");
            Assert.IsNotNull(_grid, "PixelGridRenderer missing from Game scene.");
            Assert.IsNotNull(_controller.Session, "No level loaded on start.");
            Assert.AreEqual(GameState.Playing, _controller.State);
        }

        /// <summary>
        /// Adds a touchscreen, makes it <c>Pointer.current</c>, and lets input reach the game regardless of
        /// window focus (batchmode has no focused Game view). Undone in <see cref="TearDown"/>.
        /// </summary>
        private void SetUpTouchscreen()
        {
            InputSettings settings = InputSystem.settings;
            _savedBackgroundBehavior = settings.backgroundBehavior;
            _savedEditorBehavior = settings.editorInputBehaviorInPlayMode;
            _inputSettingsChanged = true;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

            _touchscreen = InputSystem.AddDevice<Touchscreen>();
            _touchscreen.MakeCurrent();
        }

        /// <summary>
        /// Presses the touchscreen at the screen point of <paramref name="world"/> for one frame, then releases.
        /// The events are processed by the player loop's input update, so the controller sees the press through
        /// <c>Pointer.current.press.wasPressedThisFrame</c> in its own Update, exactly as with a real finger.
        /// </summary>
        private IEnumerator Tap(Camera cam, Vector3 world)
        {
            Assert.AreSame(_touchscreen, Pointer.current, "Test touchscreen is not Pointer.current.");
            Vector2 screen = cam.WorldToScreenPoint(world);
            int id = ++_touchId;

            InputSystem.QueueStateEvent(_touchscreen,
                new TouchState { touchId = id, phase = UnityEngine.InputSystem.TouchPhase.Began, position = screen });
            yield return null; // press processed before this frame's Update -> GameController.HandleInput
            InputSystem.QueueStateEvent(_touchscreen,
                new TouchState { touchId = id, phase = UnityEngine.InputSystem.TouchPhase.Ended, position = screen });
            yield return null;
        }

        /// <summary>
        /// 20x50 board of colour 0 with two lanes of four long-lived colour-0 tanks (ammo 200 each), so tray tanks
        /// keep firing (the game stays Playing) while the tap test runs.
        /// </summary>
        private static LevelData CreateTapLevel()
        {
            var level = ScriptableObject.CreateInstance<LevelData>();
            level.name = "TapTestLevel";
            level.width = 20;
            level.height = 50;
            level.palette = new Color32[] { new Color32(220, 60, 60, 255) };
            level.cells = new byte[level.width * level.height];
            level.tanks = new ColorTankData[8];
            for (int i = 0; i < level.tanks.Length; i++)
                level.tanks[i] = new ColorTankData { colorId = 0, ammo = 200 };
            level.laneCount = 2;
            level.slotCount = 5;
            return level;
        }

        private GameObject Panel(string name)
        {
            Transform t = _hud.transform.Find(name);
            Assert.IsNotNull(t, $"HUD child '{name}' missing.");
            return t.gameObject;
        }

        private Button RetryButton()
        {
            Transform t = _hud.transform.Find("LosePanel/RetryButton");
            Assert.IsNotNull(t, "Retry button missing.");
            return t.GetComponent<Button>();
        }

        private static int SmallestFrontLane(LevelSession s)
        {
            int best = -1;
            int bestId = int.MaxValue;
            for (int lane = 0; lane < s.Supply.LaneCount; lane++)
            {
                ColorTankModel front = s.Supply.PeekFront(lane);
                if (front != null && front.Id < bestId)
                {
                    bestId = front.Id;
                    best = lane;
                }
            }
            return best;
        }

        /// <summary>
        /// 1x2 board: colour 0 exposed at the bottom, colour 1 above it. One lane holding five colour-1 tanks
        /// (unable to fire while colour 0 covers the column) followed by a colour-0 tank.
        /// </summary>
        private static LevelData CreateBlockedLevel()
        {
            var level = ScriptableObject.CreateInstance<LevelData>();
            level.name = "BlockedTestLevel";
            level.width = 1;
            level.height = 2;
            level.palette = new Color32[] { new Color32(220, 60, 60, 255), new Color32(60, 120, 220, 255) };
            level.cells = new byte[] { 0, 1 };
            level.tanks = new ColorTankData[6];
            for (int i = 0; i < 5; i++)
                level.tanks[i] = new ColorTankData { colorId = 1, ammo = 1 };
            level.tanks[5] = new ColorTankData { colorId = 0, ammo = 1 };
            level.laneCount = 1;
            level.slotCount = 5;
            return level;
        }

        private void CaptureCamera()
        {
            const int width = 540;
            const int height = 960;
            Camera cam = Camera.main;
            Assert.IsNotNull(cam, "Main camera missing.");

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previousTarget = cam.targetTexture;
            try
            {
                cam.targetTexture = rt;
                _grid.Draw();
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request))
                    RenderPipeline.SubmitRenderRequest(cam, request);
                else
                    cam.Render();

                RenderTexture previousActive = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = previousActive;

                string path = Path.Combine(Application.dataPath, "..", "Logs", "game_capture.png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.Destroy(tex);
                Debug.Log($"[PlayTests] Wrote {Path.GetFullPath(path)}.");
            }
            finally
            {
                cam.targetTexture = previousTarget;
                rt.Release();
                Object.Destroy(rt);
            }
        }
    }
}
