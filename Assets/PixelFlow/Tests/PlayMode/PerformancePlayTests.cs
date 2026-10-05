using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using PixelFlow.Controller;
using PixelFlow.Core;
using PixelFlow.View;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.Profiling;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
#endif

namespace PixelFlow.Tests
{
    /// <summary>
    /// Frame-level performance checks of the Game scene while tanks shoot continuously: GC allocation per frame,
    /// main thread time and render batch counters.
    /// </summary>
    public sealed class PerformancePlayTests
    {
        private const string ScenePath = "Assets/PixelFlow/Scenes/Game.unity";
        private const int WarmupFrames = 60;
        private const int MaxWarmupFrames = 600;
        private const int DepletionSettleFrames = 30;
        private const int SampleFrames = 300;
        private const double FrameBudgetMs = 1000.0 / 60.0;

        private ProfilerRecorder _gcAlloc;
        private ProfilerRecorder _mainThread;
        private ProfilerRecorder _batches;
        private ProfilerRecorder _drawCalls;
        private int _savedTargetFrameRate;
        private int _savedVSync;

        /// <summary>
        /// Remembers the frame rate settings the test overrides.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _savedTargetFrameRate = Application.targetFrameRate;
            _savedVSync = QualitySettings.vSyncCount;
        }

        /// <summary>
        /// Releases the profiler recorders and restores the frame rate settings.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            _gcAlloc.Dispose();
            _mainThread.Dispose();
            _batches.Dispose();
            _drawCalls.Dispose();
            Application.targetFrameRate = _savedTargetFrameRate;
            QualitySettings.vSyncCount = _savedVSync;
            Time.captureDeltaTime = 0f;
#if UNITY_EDITOR
            ProfilerDriver.enabled = false;
            UnityEngine.Profiling.Profiler.enableAllocationCallstacks = false;
#endif
        }

        /// <summary>
        /// Plays Level_001 with the tray kept busy, warms up (at least 60 frames and one complete
        /// tank depletion), then samples 300 frames while shooting, with game time fixed at 1/60 s per frame:
        /// <list type="bullet">
        /// <item>GC allocation: the profiler hierarchy (main thread, "PlayerLoop" subtree, i.e. the Hierarchy view's
        /// GC Alloc column) must show 0 B in every sampled frame outside the test harness (test runner coroutine,
        /// editor async tasks). The editor-wide "GC Allocated In Frame" counter is reported as well.</item>
        /// <item>Main thread: with the 60 FPS cap lifted (so the counter measures work, not the wait), the average
        /// "Main Thread" frame time must be under 16.6 ms.</item>
        /// <item>Rendering: the board's instanced batch count must be at most 5; the "Batches Count" /
        /// "Draw Calls Count" counters are reported (they read 0 when batchmode renders nothing).</item>
        /// </list>
        /// </summary>
        [UnityTest]
        public IEnumerator SteadyStateShooting_NoGCAllocPerFrame_AndFrameBudget()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(ScenePath);
#endif
            yield return null;

            GameController controller = Object.FindFirstObjectByType<GameController>();
            PixelGridRenderer grid = Object.FindFirstObjectByType<PixelGridRenderer>();
            Assert.IsNotNull(controller, "GameController missing from Game scene.");
            Assert.IsNotNull(grid, "PixelGridRenderer missing from Game scene.");
            Assert.IsNotNull(controller.Session, "No level loaded on start.");

            // Measure the work per frame rather than the 60 FPS wait.
            // captureDeltaTime keeps the simulation at 60 FPS game time, so shooting runs as on device.
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            Time.captureDeltaTime = 1f / 60f;

            _gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            _mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");

#if UNITY_EDITOR
            ProfilerDriver.ClearAllFrames();
            ProfilerDriver.profileEditor = false;
            UnityEngine.Profiling.Profiler.enableAllocationCallstacks = true;
            s_callstacks.Clear();
            ProfilerDriver.enabled = true;
#endif

            // Warm-up: at least 60 frames and one complete tank depletion (removal from the tray and its shrink
            // animation), so every gameplay code path has run once. The first run of a path may allocate one-off
            // runtime data (e.g. Mono creates a MethodInfo on the first Delegate.Remove of a handler).
            int tanksAtStart = TanksLeft(controller.Session);
            int framesSinceDepletion = -1;
            for (int i = 0; i < MaxWarmupFrames && (i < WarmupFrames || framesSinceDepletion < DepletionSettleFrames); i++)
            {
                FeedTray(controller);
                if (framesSinceDepletion >= 0)
                    framesSinceDepletion++;
                else if (TanksLeft(controller.Session) < tanksAtStart)
                    framesSinceDepletion = 0;
                yield return null;
            }
            Assert.GreaterOrEqual(framesSinceDepletion, DepletionSettleFrames, "No tank depleted during warm-up.");

#if UNITY_EDITOR
            int firstProfiledFrame = ProfilerDriver.lastFrameIndex + 1;
#endif
            int samples = 0;
            int counterAllocFrames = 0;
            long counterMaxAlloc = 0;
            double totalMainMs = 0;
            double maxMainMs = 0;
            long maxBatches = 0;
            long maxDrawCalls = 0;
            int startPixels = controller.Session.Grid.RemainingCount;
            for (int frame = 0; frame < SampleFrames && controller.State == GameState.Playing; frame++)
            {
                FeedTray(controller);
                bool shooting = controller.Session.Shooting.CanAnyTankFire() || controller.ActiveProjectiles > 0;
                yield return null;

                // LastValue = the frame that just completed (the one in which the tray above was shooting).
                if (!shooting)
                    continue;

                samples++;
                long alloc = _gcAlloc.LastValue;
                if (alloc != 0)
                {
                    counterAllocFrames++;
                    if (alloc > counterMaxAlloc)
                        counterMaxAlloc = alloc;
                }

                double mainMs = _mainThread.LastValue * 1e-6;
                totalMainMs += mainMs;
                if (mainMs > maxMainMs)
                    maxMainMs = mainMs;
                if (_batches.LastValue > maxBatches)
                    maxBatches = _batches.LastValue;
                if (_drawCalls.LastValue > maxDrawCalls)
                    maxDrawCalls = _drawCalls.LastValue;
            }
            int shotPixels = startPixels - controller.Session.Grid.RemainingCount;
            double avgMainMs = samples > 0 ? totalMainMs / samples : 0;

            var report = new StringBuilder();
            report.Append($"[PerfTests] {samples} shooting frames sampled, {shotPixels} pixels shot. ")
                .Append($"Counter 'GC Allocated In Frame' (editor-wide): {counterAllocFrames} non-zero frames, max {counterMaxAlloc} B. ")
                .Append($"Main Thread: avg {avgMainMs:F3} ms, max {maxMainMs:F3} ms. ")
                .Append($"Render counters: batches max {maxBatches}, draw calls max {maxDrawCalls}; board instanced batches {grid.BatchCount}.");

            int gameAllocFrames = 0;
#if UNITY_EDITOR
            // Let the profiler flush the last sampled frames, then stop recording.
            int lastProfiledFrame = ProfilerDriver.lastFrameIndex;
            for (int i = 0; i < 5; i++)
                yield return null;
            ProfilerDriver.enabled = false;
            gameAllocFrames = AnalyzeHierarchy(firstProfiledFrame, lastProfiledFrame, report);
#endif
            Debug.Log(report.ToString());

            Assert.GreaterOrEqual(samples, SampleFrames * 9 / 10, "Shooting was not active for most sampled frames.");
            Assert.Greater(shotPixels, 0, "No pixel was shot during sampling.");
            Assert.AreEqual(0, gameAllocFrames, "Game code allocated GC memory in shooting frames; see the log.");
            Assert.IsTrue(_mainThread.Valid, "Main Thread counter unavailable.");
            Assert.Less(avgMainMs, FrameBudgetMs, "Average main thread time over the 60 FPS budget.");
            Assert.LessOrEqual(grid.BatchCount, 5, "Board needs more than 5 instanced batches.");
        }

#if UNITY_EDITOR
        /// <summary>
        /// Walks the main-thread "PlayerLoop" subtree of each profiled frame and attributes GC allocations to the
        /// innermost marker that allocated. Allocations made inside the test runner's coroutine (this test's own
        /// enumerator) are reported but not counted against the game.
        /// </summary>
        /// <returns>Number of frames in which game code (anything else under PlayerLoop) allocated.</returns>
        private static int AnalyzeHierarchy(int firstFrame, int lastFrame, StringBuilder report)
        {
            int frames = 0;
            int gameAllocFrames = 0;
            double totalPlayerLoopMs = 0;
            double maxPlayerLoopMs = 0;
            var allocByPath = new Dictionary<string, AllocSite>();
            var children = new List<int>();

            for (int f = Mathf.Max(firstFrame, ProfilerDriver.firstFrameIndex); f <= lastFrame; f++)
            {
                using (HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(f, 0,
                           HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                           HierarchyFrameDataView.columnGcMemory, false))
                {
                    if (view == null || !view.valid)
                        continue;

                    int playerLoop = FindChild(view, view.GetRootItemID(), "PlayerLoop", children);
                    if (playerLoop < 0)
                        continue;

                    frames++;
                    double loopMs = view.GetItemColumnDataAsFloat(playerLoop, HierarchyFrameDataView.columnTotalTime);
                    totalPlayerLoopMs += loopMs;
                    if (loopMs > maxPlayerLoopMs)
                        maxPlayerLoopMs = loopMs;

                    double gameBytes = CollectAllocs(view, playerLoop, "PlayerLoop", f - firstFrame, allocByPath);
                    if (gameBytes > 0)
                        gameAllocFrames++;
                }
            }

            report.Append($" Profiler hierarchy: {frames} frames (#{firstFrame}-#{lastFrame}); PlayerLoop avg ")
                .Append($"{(frames > 0 ? totalPlayerLoopMs / frames : 0):F3} ms, max {maxPlayerLoopMs:F3} ms; ")
                .Append($"game GC alloc in {gameAllocFrames} frames.");
            foreach (KeyValuePair<string, AllocSite> pair in allocByPath)
                report.Append($"\n  GC alloc {pair.Value.Bytes:F0} B in {pair.Value.Frames} frames ")
                    .Append($"(sample #{pair.Value.First}-#{pair.Value.Last}) at {pair.Key}");

            foreach (string stack in s_callstacks)
                report.Append("\n  Game allocation callstack:").Append(stack);

            Assert.Greater(frames, 0, "No profiler frames captured.");
            return gameAllocFrames;
        }

        /// <summary>
        /// Adds the GC bytes of every innermost allocating marker below <paramref name="id"/> to
        /// <paramref name="allocByPath"/>; returns the bytes not attributed to the test runner coroutine.
        /// </summary>
        private static double CollectAllocs(HierarchyFrameDataView view, int id, string path, int sample,
            Dictionary<string, AllocSite> allocByPath)
        {
            float bytes = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnGcMemory);
            if (bytes <= 0)
                return 0;

            var children = new List<int>();
            view.GetItemChildren(id, children);
            double childBytes = 0;
            double gameBytes = 0;
            foreach (int child in children)
            {
                string name = view.GetItemName(child);
                if (name == "GC.Alloc")
                {
                    if (!IsTestHarness(path))
                        CollectCallstacks(view, child);
                    continue;
                }
                float b = view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnGcMemory);
                if (b <= 0)
                    continue;
                childBytes += b;
                gameBytes += CollectAllocs(view, child, path + " > " + name, sample, allocByPath);
            }

            double self = bytes - childBytes;
            if (self > 0)
            {
                if (!allocByPath.TryGetValue(path, out AllocSite site))
                {
                    site = new AllocSite { First = sample };
                    allocByPath[path] = site;
                }
                site.Bytes += self;
                site.Frames++;
                site.Last = sample;
                if (!IsTestHarness(path))
                    gameBytes += self;
            }
            return gameBytes;
        }

        private static readonly HashSet<string> s_callstacks = new HashSet<string>();

        /// <summary>
        /// Resolves the managed callstacks of a GC.Alloc item (needs <c>Profiler.enableAllocationCallstacks</c>).
        /// </summary>
        private static void CollectCallstacks(HierarchyFrameDataView view, int gcAllocItem)
        {
            var frames = new List<ulong>();
            int count = view.GetItemMergedSamplesCount(gcAllocItem);
            for (int s = 0; s < count; s++)
            {
                view.GetItemMergedSampleCallstack(gcAllocItem, s, frames);
                var sb = new StringBuilder();
                for (int i = 0; i < frames.Count && i < 12; i++)
                {
                    FrameDataView.MethodInfo m = view.ResolveMethodInfo(frames[i]);
                    if (string.IsNullOrEmpty(m.methodName))
                        continue;
                    sb.Append("\n      ").Append(m.methodName);
                    if (!string.IsNullOrEmpty(m.sourceFileName))
                        sb.Append(" (").Append(m.sourceFileName).Append(':').Append(m.sourceFileLine).Append(')');
                }
                s_callstacks.Add(sb.ToString());
            }
        }

        private sealed class AllocSite
        {
            public double Bytes;
            public int Frames;
            public int First;
            public int Last;
        }

        /// <summary>
        /// True for PlayerLoop markers that run no game code: the test runner coroutine (this test's enumerator)
        /// and UnitySynchronizationContext tasks (async continuations of editor packages; the game has no async code).
        /// </summary>
        private static bool IsTestHarness(string path)
        {
            return path.Contains("PlaymodeTestsController") || path.Contains("PerformancePlayTests") ||
                   path.Contains("UnitySynchronizationContext");
        }

        private static int FindChild(HierarchyFrameDataView view, int parent, string name, List<int> buffer)
        {
            view.GetItemChildren(parent, buffer);
            foreach (int child in buffer)
            {
                if (view.GetItemName(child) == name)
                    return child;
            }
            return -1;
        }
#endif

        /// <summary>
        /// Keeps the tray busy: moves the lowest-id lane-front tank whose colour is exposed into the tray, or any
        /// lane-front tank when nothing in the tray can fire.
        /// </summary>
        private static int TanksLeft(LevelSession s)
        {
            return s.Supply.TotalRemaining + s.Tray.Count;
        }

        private static void FeedTray(GameController controller)
        {
            LevelSession s = controller.Session;
            if (s.Tray.IsFull)
                return;

            bool anyCanFire = s.Shooting.CanAnyTankFire();
            int best = -1;
            int bestId = int.MaxValue;
            for (int lane = 0; lane < s.Supply.LaneCount; lane++)
            {
                ColorTankModel front = s.Supply.PeekFront(lane);
                if (front == null || front.Id >= bestId)
                    continue;
                if (anyCanFire && !s.Grid.HasExposed(front.ColorId))
                    continue;
                bestId = front.Id;
                best = lane;
            }

            if (best >= 0)
                controller.TryActivateLane(best);
        }
    }
}
