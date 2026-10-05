using System.Collections;
using System.IO;
using NUnit.Framework;
using PixelFlow.Controller;
using PixelFlow.Core;
using PixelFlow.Data;
using PixelFlow.View;
using UnityEngine;
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

        private GameController _controller;
        private GameHudView _hud;
        private PixelGridRenderer _grid;

        /// <summary>
        /// Restores the time scale changed by the auto-play test.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
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
