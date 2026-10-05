using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using PixelFlow.Core;
using PixelFlow.Data;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace PixelFlow.Tests
{
    /// <summary>
    /// Stress tests on a procedural 100x50 (5000 cells) level: per-step CPU time and steady-state allocations
    /// of the core shooting loop.
    /// </summary>
    public sealed class StressTests
    {
        private const int Width = 100;
        private const int Height = 50;
        private const int ColorCount = 8;
        private const int AmmoPerTank = 20;
        private const int WarmupSteps = 10;
        private const int MaxIterations = 100000;

        private LevelData _level;

        /// <summary>
        /// Builds the procedural level used by every test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _level = CreateStressLevel();
        }

        /// <summary>
        /// Destroys the procedural level asset.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (_level != null)
                Object.DestroyImmediate(_level);
        }

        /// <summary>
        /// Auto-plays the 5000-cell level to a win; the average time of one <see cref="ShootingLogic.Step"/>
        /// must be under 2 ms.
        /// </summary>
        [Test]
        public void Stress_5000Cells_FullAutoPlay_StepsUnder2msAverage()
        {
            LevelSession s = LevelSession.Create(_level);
            var shots = new List<ShotEvent>(s.Tray.Capacity);
            long stepTicks = 0;
            int stepCount = 0;

            GameState state = GameState.Playing;
            for (int i = 0; i < MaxIterations && state == GameState.Playing; i++)
            {
                if (s.Shooting.CanAnyTankFire())
                {
                    shots.Clear();
                    long t0 = Stopwatch.GetTimestamp();
                    s.Shooting.Step(shots);
                    stepTicks += Stopwatch.GetTimestamp() - t0;
                    stepCount++;
                }
                else
                {
                    ActivateSmallestFront(s);
                }
                state = GameRules.Evaluate(s.Grid, s.Tray, s.Supply, s.Shooting);
            }

            double avgMs = stepCount > 0 ? stepTicks * 1000.0 / Stopwatch.Frequency / stepCount : 0.0;
            Debug.Log($"[StressTests] {Width * Height} cells, {_level.tanks.Length} tanks: {stepCount} steps, " +
                      $"total {stepTicks * 1000.0 / Stopwatch.Frequency:F3} ms, avg {avgMs * 1000.0:F2} us/step.");

            Assert.AreEqual(GameState.Won, state);
            Assert.AreEqual(0, s.Grid.RemainingCount);
            Assert.Greater(stepCount, 0);
            Assert.Less(avgMs, 2.0, "Average ShootingLogic.Step time exceeds 2 ms.");
        }

        /// <summary>
        /// After the first 10 steps, the full auto-play loop (rules evaluation, tray activation, shooting step)
        /// allocates no GC memory until the level is won.
        /// </summary>
        /// <remarks>
        /// <see cref="AllocAssert.NoAlloc"/> runs the action once unmeasured before the measured call. That first
        /// run plays a separate warm-up session to the end and then switches the action to the measured session, so
        /// the measured call still plays a full level (assigning a reference does not allocate).
        /// </remarks>
        [Test]
        public void Stress_FullAutoPlay_NoAllocationInSteadyState()
        {
            LevelSession warm = LevelSession.Create(_level);
            LevelSession measured = LevelSession.Create(_level);
            var shots = new List<ShotEvent>(measured.Tray.Capacity);

            for (int i = 0; i < WarmupSteps; i++)
            {
                PlayIteration(warm, shots);
                PlayIteration(measured, shots);
            }

            LevelSession current = warm;
            GameState state = GameState.Playing;
            int measuredRuns = 0;
            AllocAssert.NoAlloc(() =>
            {
                for (int i = 0; i < MaxIterations; i++)
                {
                    state = PlayIteration(current, shots);
                    if (state != GameState.Playing)
                        break;
                }
                if (current == measured)
                    measuredRuns++;
                current = measured;
            });

            Assert.AreEqual(1, measuredRuns, "The measured session was not played exactly once.");
            Assert.AreEqual(GameState.Won, state);
            Assert.AreEqual(0, measured.Grid.RemainingCount);
        }

        /// <summary>
        /// One auto-play iteration: fire if any tray tank can, otherwise move the smallest-id lane front into the tray.
        /// </summary>
        private static GameState PlayIteration(LevelSession s, List<ShotEvent> shots)
        {
            if (s.Shooting.CanAnyTankFire())
            {
                shots.Clear();
                s.Shooting.Step(shots);
            }
            else
            {
                ActivateSmallestFront(s);
            }
            return GameRules.Evaluate(s.Grid, s.Tray, s.Supply, s.Shooting);
        }

        private static void ActivateSmallestFront(LevelSession s)
        {
            int bestLane = -1;
            int bestId = int.MaxValue;
            for (int lane = 0; lane < s.Supply.LaneCount; lane++)
            {
                ColorTankModel front = s.Supply.PeekFront(lane);
                if (front != null && front.Id < bestId)
                {
                    bestId = front.Id;
                    bestLane = lane;
                }
            }

            if (bestLane >= 0 && s.Supply.TryTakeFront(bestLane, out ColorTankModel tank))
                s.Tray.TryAdd(tank);
        }

        /// <summary>
        /// 100x50 level with 8 colours in diagonal bands broken up by a hash pattern (no empty cells), tanks from
        /// <see cref="TankGenerator.Generate"/>, 3 lanes and one slot per tank so it is always solvable.
        /// </summary>
        private static LevelData CreateStressLevel()
        {
            var cells = new byte[Width * Height];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
                    int band = (x + y) / 7;
                    cells[y * Width + x] = (byte)((band + (h % 3 == 0 ? 3 : 0)) % ColorCount);
                }
            }

            var palette = new Color32[ColorCount];
            for (int i = 0; i < ColorCount; i++)
                palette[i] = Color.HSVToRGB(i / (float)ColorCount, 0.8f, 0.9f);

            var level = ScriptableObject.CreateInstance<LevelData>();
            level.name = "StressLevel_100x50";
            level.width = Width;
            level.height = Height;
            level.palette = palette;
            level.cells = cells;
            level.tanks = TankGenerator.Generate(Width, Height, cells, AmmoPerTank);
            level.laneCount = 3;
            level.slotCount = level.tanks.Length;
            return level;
        }
    }
}
