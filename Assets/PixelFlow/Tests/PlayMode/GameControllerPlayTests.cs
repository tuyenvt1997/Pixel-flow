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
    /// End-to-end tests of the generated Game scene under the conveyor-belt rules: auto-play to a win (starter
    /// level 1 and the sun level), cycling through all levels with Next, an overflow loss (and the latched result
    /// while shots drain), launch capacity, tapping
    /// lane-front and waiting-slot tanks, retry with tanks on the belt, the level load time budget and a mid-game
    /// capture of the belt layout.
    /// </summary>
    public sealed class GameControllerPlayTests
    {
        private const string ScenePath = "Assets/PixelFlow/Scenes/Game.unity";
        private const int SunCells = 64 * 64;

        /// <summary>Number of levels in the Game scene: five starter levels followed by the sun level.</summary>
        private const int LevelCount = 6;

        /// <summary>Index of the 64x64 sun level (the last level).</summary>
        private const int SunIndex = 5;

        /// <summary>Index of starter level 3 (16x16), used for the belt layout capture.</summary>
        private const int Level3Index = 2;

        /// <summary>Board size (width = height) of each level in play order.</summary>
        private static readonly int[] LevelSizes = { 8, 12, 16, 24, 32, 64 };

        private const string BoardShaderName = "PixelFlow/InstancedColor";

        /// <summary>Seconds to wait after a tap so lane/slot move tweens (0.18 s) have finished.</summary>
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
        /// Restores the time scale changed by the auto-play tests, and removes the touchscreen and restores the
        /// input settings changed by the tap tests.
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
        /// with synthesized touches: tapping a non-front tank does nothing, tapping the front of lane 0 launches that
        /// tank onto the belt, and once the belt plus its entrance queue holds <c>slotCount</c> tanks a lane-front
        /// tap is ignored and the tank stays in its lane.
        /// </summary>
        [UnityTest]
        public IEnumerator Tap_LaneFront_LaunchesOntoBelt()
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
            Assert.AreEqual(0, OnBelt(s), "Tapping a non-front tank launched a tank.");
            Assert.AreEqual(8, s.Supply.TotalRemaining);
            Assert.AreSame(lane1Front, s.Supply.PeekFront(1));

            // Front of lane 0: launched onto the belt (entrance queue or belt).
            ColorTankModel lane0Front = s.Supply.PeekFront(0);
            yield return Tap(cam, board.LanePosition(0, 0));
            Assert.AreEqual(1, OnBelt(s), "Tapping the lane-0 front tank did not launch it.");
            Assert.AreEqual(7, s.Supply.TotalRemaining);
            Assert.AreNotSame(lane0Front, s.Supply.PeekFront(0));
            yield return new WaitForSeconds(TweenSettleSeconds);

            // Fill the belt by tapping lane fronts (alternating lanes so both stay non-empty).
            for (int i = 1; i < 5; i++)
            {
                int lane = i % 2;
                yield return Tap(cam, board.LanePosition(lane, 0));
                Assert.AreEqual(i + 1, OnBelt(s), $"Tap {i} on lane {lane} front did not launch a tank.");
                yield return new WaitForSeconds(TweenSettleSeconds);
            }
            Assert.IsTrue(s.Belt.IsFull);
            Assert.AreEqual(GameState.Playing, _controller.State);

            // Belt full: a lane-front tap is ignored and the tank stays in its lane.
            ColorTankModel front = s.Supply.PeekFront(1);
            Assert.IsNotNull(front);
            yield return Tap(cam, board.LanePosition(1, 0));
            Assert.AreEqual(5, OnBelt(s));
            Assert.AreEqual(3, s.Supply.TotalRemaining, "A tank left its lane while the belt was full.");
            Assert.AreSame(front, s.Supply.PeekFront(1));

            Object.Destroy(level);
        }

        /// <summary>
        /// A tank that finished its lap waits in slot 0; a synthesized tap on it removes it from the slot and
        /// launches it onto the belt again.
        /// </summary>
        [UnityTest]
        public IEnumerator Tap_SlotTank_Relaunches()
        {
            yield return LoadGameScene();
            LevelData level = CreateOverflowLevel();
            _controller.LoadLevel(level);
            TankBoardView board = Object.FindFirstObjectByType<TankBoardView>();
            Camera cam = Camera.main;
            SetUpTouchscreen();
            LevelSession s = _controller.Session;

            ColorTankModel tank = s.Supply.PeekFront(0);
            Assert.IsTrue(_controller.TryLaunchFromLane(0));
            yield return WaitForSlotCount(s, 1);
            Assert.AreSame(tank, s.Tray[0]);
            yield return new WaitForSeconds(TweenSettleSeconds);

            yield return Tap(cam, board.SlotPosition(0));
            Assert.AreEqual(0, s.Tray.Count, "The tapped slot tank is still in its slot.");
            Assert.AreEqual(1, OnBelt(s), "The tapped slot tank was not launched.");
            Assert.AreEqual(GameState.Playing, _controller.State);

            Object.Destroy(level);
        }

        /// <summary>
        /// With the belt full (capacity 1, one tank riding), launching the waiting-slot tank is rejected and the
        /// tank stays in its slot.
        /// </summary>
        [UnityTest]
        public IEnumerator TryLaunchFromSlot_BeltFull_ReturnsFalseAndKeepsSlot()
        {
            yield return LoadGameScene();
            LevelData level = CreateOverflowLevel();
            _controller.LoadLevel(level);
            LevelSession s = _controller.Session;

            ColorTankModel first = s.Supply.PeekFront(0);
            Assert.IsTrue(_controller.TryLaunchFromLane(0));
            Assert.IsFalse(_controller.TryLaunchFromLane(0), "The belt accepted a second tank at capacity 1.");
            yield return WaitForSlotCount(s, 1);

            Assert.IsTrue(_controller.TryLaunchFromLane(0));
            Assert.IsTrue(s.Belt.IsFull);
            Assert.IsFalse(_controller.TryLaunchFromSlot(0), "Slot tank launched onto a full belt.");
            Assert.AreEqual(1, s.Tray.Count);
            Assert.AreSame(first, s.Tray[0]);
            Assert.AreEqual(1, OnBelt(s));

            Object.Destroy(level);
        }

        /// <summary>
        /// One waiting slot and two tanks whose colour has no front: the first finishes its lap into the slot, the
        /// second finishes its lap with every slot full, so the game is lost (overflow) and the lose popup shows;
        /// retry restores a playable level.
        /// </summary>
        [UnityTest]
        public IEnumerator Overflow_ReachesLost()
        {
            yield return LoadGameScene();
            LevelData level = CreateOverflowLevel();
            _controller.LoadLevel(level);
            LevelSession s = _controller.Session;
            Time.timeScale = 4f;

            Assert.IsTrue(_controller.TryLaunchFromLane(0));
            yield return WaitForSlotCount(s, 1);
            Assert.AreEqual(GameState.Playing, _controller.State, "Lost before the second tank was launched.");
            Assert.IsTrue(_controller.TryLaunchFromLane(0));

            float start = Time.realtimeSinceStartup;
            while (_controller.State == GameState.Playing)
            {
                Assert.Less(Time.realtimeSinceStartup - start, 10f, "Lost state not reached.");
                yield return null;
            }

            Assert.AreEqual(GameState.Lost, _controller.State);
            Assert.IsTrue(s.BeltShooting.Overflowed, "The loss was not an overflow.");
            Assert.IsTrue(Panel("LosePanel").activeSelf, "Lose popup not shown.");
            Assert.IsFalse(Panel("WinPanel").activeSelf, "Win popup shown on a loss.");

            // The overflowed tank has no slot: its view shrinks away and returns to the pool; only the slot tank's
            // view is left.
            TankBoardView board = Object.FindFirstObjectByType<TankBoardView>();
            yield return new WaitForSeconds(ColorTankView.DefaultDepleteDuration + 0.1f);
            Assert.AreEqual(1, board.ActiveViewCount, "The overflowed tank's view was not released.");

            RetryButton().onClick.Invoke();
            Assert.AreEqual(GameState.Playing, _controller.State);
            Assert.IsFalse(Panel("LosePanel").activeSelf, "Lose popup still shown after retry.");
            Assert.AreEqual(0, _controller.Session.Tray.Count);
            Assert.AreEqual(0, OnBelt(_controller.Session));
            Assert.AreEqual(2, _controller.Session.Supply.TotalRemaining);

            Object.Destroy(level);
        }

        /// <summary>
        /// Two waiting slots hold colour-1 tanks; then a colour-1 tank overflows while a long-lived colour-0 tank
        /// keeps firing, so projectiles are still in flight when the rules report Lost. From that moment the result
        /// is latched: the belt no longer advances, lane and slot launches are rejected although the belt has room,
        /// the lose popup still appears once everything has drained, and the overflowed tank's view is released.
        /// </summary>
        [UnityTest]
        public IEnumerator Overflow_WhileShooting_FreezesBeltAndRejectsLaunches()
        {
            yield return LoadGameScene();
            LevelData level = CreateResolvingLevel();
            _controller.LoadLevel(level);
            TankBoardView board = Object.FindFirstObjectByType<TankBoardView>();
            LevelSession s = _controller.Session;
            Time.timeScale = 4f;

            Assert.IsTrue(_controller.TryLaunchFromLane(0));
            Assert.IsTrue(_controller.TryLaunchFromLane(0));
            yield return WaitForSlotCount(s, 2);

            ColorTankModel overflowing = s.Supply.PeekFront(0);
            Assert.IsTrue(_controller.TryLaunchFromLane(0));
            ColorTankModel shooter = s.Supply.PeekFront(0);
            Assert.IsTrue(_controller.TryLaunchFromLane(0));
            Assert.AreEqual(1, overflowing.ColorId, "The first rider must be a colour-1 tank that never fires.");
            Assert.AreEqual(0, shooter.ColorId, "The second rider must be the colour-0 shooter.");

            float start = Time.realtimeSinceStartup;
            while (!s.BeltShooting.Overflowed)
            {
                Assert.Less(Time.realtimeSinceStartup - start, 10f, "Overflow not reached.");
                yield return null;
            }

            Assert.AreEqual(GameState.Playing, _controller.State, "Nothing in flight at the overflow; test is void.");
            Assert.IsTrue(_controller.IsResolving, "The overflow result was not latched.");
            Assert.Greater(_controller.ActiveProjectiles + _controller.PendingDestroys, 0);
            Assert.AreEqual(1, s.Belt.Count, "Only the shooter rides after the overflow.");
            Assert.IsTrue(s.Belt.TryGetPosition(shooter, out int frozenPosition));
            int remaining = s.Grid.RemainingCount;
            int ammo = shooter.Ammo;

            Assert.IsFalse(s.Belt.IsFull);
            Assert.IsFalse(_controller.TryLaunchFromLane(0), "Lane launch accepted while resolving.");
            Assert.IsFalse(_controller.TryLaunchFromSlot(0), "Slot launch accepted while resolving.");
            Assert.AreEqual(1, s.Supply.TotalRemaining);
            Assert.AreEqual(2, s.Tray.Count);

            // At timeScale 4 a 20x20 belt ticks every 12.5 ms of real time; give it many tick intervals.
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(s.Belt.TryGetPosition(shooter, out int position));
            Assert.AreEqual(frozenPosition, position, "The belt kept advancing after the overflow.");
            Assert.AreEqual(remaining, s.Grid.RemainingCount, "The belt kept firing after the overflow.");
            Assert.AreEqual(ammo, shooter.Ammo);

            start = Time.realtimeSinceStartup;
            while (_controller.State == GameState.Playing)
            {
                Assert.Less(Time.realtimeSinceStartup - start, 10f, "Lost state not reached.");
                yield return null;
            }

            Assert.AreEqual(GameState.Lost, _controller.State);
            Assert.IsFalse(_controller.IsResolving);
            Assert.IsTrue(Panel("LosePanel").activeSelf, "Lose popup not shown.");
            Assert.IsFalse(Panel("WinPanel").activeSelf, "Win popup shown on a loss.");
            Assert.IsTrue(s.Belt.TryGetPosition(shooter, out position));
            Assert.AreEqual(frozenPosition, position, "The belt advanced while draining.");

            // Views left: two slot tanks, the frozen shooter and the lane tank; the overflowed one is released.
            yield return new WaitForSeconds(ColorTankView.DefaultDepleteDuration + 0.1f);
            Assert.AreEqual(4, board.ActiveViewCount, "The overflowed tank's view was not released.");

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
            _controller.LoadLevel(SunIndex);
            TankBoardView board = Object.FindFirstObjectByType<TankBoardView>();
            LevelSession before = _controller.Session;
            int views = board.ActiveViewCount;

            LevelData bad = CreateOverflowLevel();
            bad.cells[0] = 7; // colour 7 is outside the 2-colour palette
            Assert.Throws<System.ArgumentException>(() => _controller.LoadLevel(bad));

            Assert.AreSame(before, _controller.Session, "Session replaced by a failed load.");
            Assert.AreEqual(GameState.Playing, _controller.State);
            Assert.AreEqual(SunIndex, _controller.CurrentLevelIndex);
            Assert.AreEqual(SunCells, _grid.InstanceCount, "Board cleared by a failed load.");
            Assert.AreEqual(views, board.ActiveViewCount, "Tank views cleared by a failed load.");

            Assert.IsTrue(_controller.TryLaunchFromLane(0), "Previous level no longer playable.");
            float start = Time.realtimeSinceStartup;
            while (_controller.Session.Grid.RemainingCount == SunCells)
            {
                Assert.Less(Time.realtimeSinceStartup - start, 6f, "No pixel destroyed after the failed load.");
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
            Assert.IsTrue(_controller.TryLaunchFromLane(0));

            PixelCellView cell = null;
            float start = Time.realtimeSinceStartup;
            while (cell == null)
            {
                Assert.Less(Time.realtimeSinceStartup - start, 6f, "No destroy animation started.");
                yield return null;
                cell = Object.FindFirstObjectByType<PixelCellView>(FindObjectsInactive.Exclude);
            }

            Material material = cell.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.AreEqual(BoardShaderName, material.shader.name);
        }

        /// <summary>
        /// Starter level 1 (the 8x8 heart, loaded on scene start) is won with the <c>AutoPlayer</c> policy driven
        /// through the controller's launch hooks, and a mid-game capture is written to Logs/level1_capture.png.
        /// </summary>
        [UnityTest]
        public IEnumerator Level1_AutoPlay_ReachesWon()
        {
            yield return LoadGameScene();
            Assert.AreEqual(0, _controller.CurrentLevelIndex);
            Assert.AreEqual(LevelSizes[0], _controller.Session.Width);
            int cells = _controller.Session.Grid.RemainingCount;
            Assert.AreEqual(CountPixels(_controller.CurrentLevel), cells);
            Assert.Less(cells, 64, "Level 1 should have few pixels.");
            Time.timeScale = 4f;

            float start = Time.realtimeSinceStartup;
            bool captured = false;
            while (_controller.State == GameState.Playing)
            {
                Assert.Less(Time.realtimeSinceStartup - start, 60f, "Level 1 was not won within 60 s.");

                LaunchLikeAutoPlayer(_controller);

                LevelSession s = _controller.Session;
                if (!captured && s.Grid.RemainingCount < cells * 3 / 4 && _controller.ActiveProjectiles > 0)
                {
                    CaptureCamera("level1_capture.png");
                    captured = true;
                }

                yield return null;
            }

            Assert.AreEqual(GameState.Won, _controller.State);
            Assert.AreEqual(0, _controller.Session.Grid.RemainingCount);
            Assert.IsTrue(captured, "No mid-game capture was taken.");
            Assert.IsTrue(Panel("WinPanel").activeSelf, "Win popup not shown.");
            Debug.Log($"[PlayTests] Level 1 won in {Time.realtimeSinceStartup - start:F1} s real time.");
        }

        /// <summary>
        /// The Next button walks through all six levels in order and wraps around: the level index goes
        /// 0, 1, ..., 5, 0 and each time the board holds exactly that level's non-empty pixels.
        /// </summary>
        [UnityTest]
        public IEnumerator Next_CyclesThroughAllSixLevels()
        {
            yield return LoadGameScene();
            Assert.AreEqual(LevelCount, _controller.LevelCount, "Game scene level count.");
            Assert.AreEqual(0, _controller.CurrentLevelIndex);
            Button next = NextButton();

            for (int step = 1; step <= LevelCount; step++)
            {
                next.onClick.Invoke();
                int expected = step % LevelCount;
                Assert.AreEqual(expected, _controller.CurrentLevelIndex, $"Index after Next #{step}.");
                Assert.AreEqual(GameState.Playing, _controller.State);
                Assert.AreEqual(LevelSizes[expected], _controller.Session.Width, $"Width of level {expected}.");
                int pixels = CountPixels(_controller.CurrentLevel);
                Assert.AreEqual(pixels, _grid.InstanceCount, $"Board instances of level {expected}.");
                Assert.AreEqual(pixels, _controller.Session.Grid.RemainingCount);
                yield return null;
            }
        }

        /// <summary>
        /// Plays the 64x64 sun level (the last level) with the <c>AutoPlayer</c> policy driven through the
        /// controller's launch hooks until it is won, and writes a mid-game capture to Logs/game_capture.png.
        /// </summary>
        [UnityTest]
        public IEnumerator SunLevel_AutoPlay_ReachesWon()
        {
            yield return LoadGameScene();
            _controller.LoadLevel(SunIndex);
            Assert.AreEqual(SunCells, _controller.Session.Grid.RemainingCount);
            Time.timeScale = 8f;

            float start = Time.realtimeSinceStartup;
            bool captured = false;
            while (_controller.State == GameState.Playing)
            {
                Assert.Less(Time.realtimeSinceStartup - start, 240f, "The sun level was not won within 240 s.");

                LaunchLikeAutoPlayer(_controller);

                LevelSession s = _controller.Session;
                if (!captured && s.Grid.RemainingCount < SunCells * 3 / 4 && _controller.ActiveProjectiles > 0)
                {
                    CaptureCamera("game_capture.png");
                    captured = true;
                }

                yield return null;
            }

            Assert.AreEqual(GameState.Won, _controller.State);
            Assert.AreEqual(0, _controller.Session.Grid.RemainingCount);
            Assert.IsTrue(Panel("WinPanel").activeSelf, "Win popup not shown.");
            Assert.IsFalse(Panel("LosePanel").activeSelf, "Lose popup shown on a win.");
            Debug.Log($"[PlayTests] Sun level won in {Time.realtimeSinceStartup - start:F1} s real time.");
        }

        /// <summary>
        /// Starter level 3 (16x16) is played with the <c>AutoPlayer</c> policy until a quarter of its pixels are
        /// gone with at least three tanks on the belt and projectiles in flight; the game camera is then captured to
        /// Logs/belt_capture.png for a visual check of the belt layout (track, chevrons, counter, slots, lanes).
        /// </summary>
        [UnityTest]
        public IEnumerator Level3_MidGame_CapturesBelt()
        {
            yield return LoadGameScene();
            _controller.LoadLevel(Level3Index);
            int cells = _controller.Session.Grid.RemainingCount;

            float start = Time.realtimeSinceStartup;
            while (true)
            {
                Assert.AreEqual(GameState.Playing, _controller.State, "Level 3 ended before the capture.");
                Assert.Less(Time.realtimeSinceStartup - start, 60f, "No capture moment within 60 s.");

                LaunchLikeAutoPlayer(_controller);

                LevelSession s = _controller.Session;
                if (s.Grid.RemainingCount < cells * 3 / 4 && s.Belt.Count >= 3 && _controller.ActiveProjectiles > 0)
                    break;

                yield return null;
            }

            CaptureCamera("belt_capture.png");
            Assert.IsTrue(File.Exists(Path.Combine(Application.dataPath, "..", "Logs", "belt_capture.png")));
        }

        /// <summary>
        /// Retry while tanks ride the belt and projectiles are in flight: the belt and its queue are empty, only the
        /// supply tanks have views, no projectile or pending destroy survives and the board is fully rebuilt and
        /// stays intact (no old projectile hides a new cell).
        /// </summary>
        [UnityTest]
        public IEnumerator Retry_WithTanksOnBelt_ClearsEverything()
        {
            yield return LoadGameScene();
            _controller.LoadLevel(SunIndex);
            TankBoardView board = Object.FindFirstObjectByType<TankBoardView>();
            int pixels = CountPixels(_controller.CurrentLevel);

            for (int lane = 0; lane < 3; lane++)
                Assert.IsTrue(_controller.TryLaunchFromLane(lane));

            float start = Time.realtimeSinceStartup;
            while (_controller.ActiveProjectiles == 0 || _controller.Session.Belt.Count < 2 ||
                   _controller.Session.Grid.RemainingCount > SunCells - 20)
            {
                Assert.Less(Time.realtimeSinceStartup - start, 10f, "No projectiles in flight from the belt.");
                yield return null;
            }
            Assert.Greater(_controller.ActiveProjectiles, 0);

            RetryButton().onClick.Invoke();
            AssertFreshSunLevel(board, pixels);

            for (int i = 0; i < 30; i++)
                yield return null;

            AssertFreshSunLevel(board, pixels);
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
        /// Levels load (model + board + belt + tanks + HUD) in well under a second: level 1 on scene start, and the
        /// 64x64 sun level both on its first load and on reload.
        /// </summary>
        [UnityTest]
        public IEnumerator LevelLoad_Under1000ms()
        {
            yield return LoadGameScene();
            double onStart = _controller.LastLoadMilliseconds;

            _controller.LoadLevel(SunIndex);
            double first = _controller.LastLoadMilliseconds;
            _controller.LoadLevel(SunIndex);
            double reload = _controller.LastLoadMilliseconds;

            Debug.Log($"[PlayTests] Load: level 1 on start {onStart:F2} ms, sun level first {first:F2} ms, reload {reload:F2} ms.");
            Assert.Less(onStart, 1000.0);
            Assert.Less(first, 1000.0);
            Assert.Less(reload, 1000.0);
            yield return null;
        }

        private void AssertFreshSunLevel(TankBoardView board, int pixels)
        {
            LevelSession s = _controller.Session;
            Assert.AreEqual(0, s.Belt.Count, "Tanks left on the belt after retry.");
            Assert.AreEqual(0, s.Belt.QueuedCount, "Tanks left in the entrance queue after retry.");
            Assert.AreEqual(0, s.Tray.Count, "Tanks left in the waiting slots after retry.");
            Assert.AreEqual(s.Supply.TotalRemaining, board.ActiveViewCount, "Tank views other than the supply.");
            Assert.AreEqual(0, _controller.ActiveProjectiles);
            Assert.AreEqual(0, _controller.PendingDestroys);
            Assert.AreEqual(pixels, _grid.InstanceCount);
            Assert.AreEqual(pixels, s.Grid.RemainingCount);
        }

        /// <summary>
        /// One frame of the <c>AutoPlayer</c> policy through the controller hooks: if the belt has room, relaunch
        /// the first waiting-slot tank whose colour has a front, otherwise launch the lane-front tank with the
        /// smallest id.
        /// </summary>
        private static void LaunchLikeAutoPlayer(GameController controller)
        {
            LevelSession s = controller.Session;
            if (s.Belt.IsFull)
                return;

            for (int slot = 0; slot < s.Tray.Count; slot++)
            {
                if (s.Grid.HasAnyFront(s.Tray[slot].ColorId))
                {
                    controller.TryLaunchFromSlot(slot);
                    return;
                }
            }

            int lane = SmallestFrontLane(s);
            if (lane >= 0)
                controller.TryLaunchFromLane(lane);
        }

        private static int OnBelt(LevelSession s)
        {
            return s.Belt.Count + s.Belt.QueuedCount;
        }

        private IEnumerator WaitForSlotCount(LevelSession s, int count)
        {
            float start = Time.realtimeSinceStartup;
            while (s.Tray.Count < count)
            {
                Assert.Less(Time.realtimeSinceStartup - start, 10f, $"No tank reached waiting slot {count - 1}.");
                Assert.AreEqual(GameState.Playing, _controller.State, "Game ended while waiting for a lap.");
                yield return null;
            }
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
        /// 20x50 board of colour 0 with two lanes of four long-lived colour-0 tanks (ammo 200 each), so belt tanks
        /// keep riding (the game stays Playing) while the tap test runs.
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

        /// <summary>
        /// 3x3 board of colour 0 with a colour-1 centre (never a front), one waiting slot (belt capacity 1) and one
        /// lane of two colour-1 tanks: neither can ever fire, so each finishes its lap with ammo left.
        /// </summary>
        private static LevelData CreateOverflowLevel()
        {
            var level = ScriptableObject.CreateInstance<LevelData>();
            level.name = "OverflowTestLevel";
            level.width = 3;
            level.height = 3;
            level.palette = new Color32[] { new Color32(220, 60, 60, 255), new Color32(60, 120, 220, 255) };
            level.cells = new byte[] { 0, 0, 0, 0, 1, 0, 0, 0, 0 };
            level.tanks = new ColorTankData[2];
            for (int i = 0; i < level.tanks.Length; i++)
                level.tanks[i] = new ColorTankData { colorId = 1, ammo = 1 };
            level.laneCount = 1;
            level.slotCount = 1;
            return level;
        }

        /// <summary>
        /// 20x20 board of colour 0 (no colour-1 cell), two waiting slots (belt capacity 2) and one lane of, front
        /// first: two colour-1 tanks (to fill the slots), a colour-1 tank (to overflow), a colour-0 tank with 300
        /// ammo (fires every tick, never clears the board) and a spare colour-1 tank (keeps the supply non-empty).
        /// </summary>
        private static LevelData CreateResolvingLevel()
        {
            var level = ScriptableObject.CreateInstance<LevelData>();
            level.name = "ResolvingTestLevel";
            level.width = 20;
            level.height = 20;
            level.palette = new Color32[] { new Color32(220, 60, 60, 255), new Color32(60, 120, 220, 255) };
            level.cells = new byte[level.width * level.height];
            level.tanks = new[]
            {
                new ColorTankData { colorId = 1, ammo = 1 },
                new ColorTankData { colorId = 1, ammo = 1 },
                new ColorTankData { colorId = 1, ammo = 1 },
                new ColorTankData { colorId = 0, ammo = 300 },
                new ColorTankData { colorId = 1, ammo = 1 },
            };
            level.laneCount = 1;
            level.slotCount = 2;
            return level;
        }

        private GameObject Panel(string name)
        {
            Transform t = _hud.transform.Find(name);
            Assert.IsNotNull(t, $"HUD child '{name}' missing.");
            return t.gameObject;
        }

        private Button NextButton()
        {
            Transform t = _hud.transform.Find("WinPanel/NextButton");
            Assert.IsNotNull(t, "Next button missing.");
            return t.GetComponent<Button>();
        }

        private static int CountPixels(LevelData level)
        {
            Assert.IsNotNull(level, "No current level data.");
            int n = 0;
            foreach (byte c in level.cells)
            {
                if (c != LevelData.EmptyCell)
                    n++;
            }
            return n;
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

        private void CaptureCamera(string fileName)
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

                string path = Path.Combine(Application.dataPath, "..", "Logs", fileName);
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
