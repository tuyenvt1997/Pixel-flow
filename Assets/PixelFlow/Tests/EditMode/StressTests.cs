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
    /// Stress tests on a procedural 100x50 (5000 cells) level: per-tick CPU time and steady-state allocations
    /// of the conveyor-belt shooting loop, played by <see cref="BeltAutoPlayer"/>.
    /// </summary>
    public sealed class StressTests
    {
        private const int Width = 100;
        private const int Height = 50;
        private const int ColorCount = 8;
        private const int AmmoPerTank = 20;
        private const int WarmupTicks = 10;
        private const int MaxTicks = 500000;

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
        /// Auto-plays the 5000-cell level to a win with <see cref="BeltAutoPlayer.Step"/>; the average time of one
        /// bot tick (launch plus <see cref="BeltShootingLogic.Tick"/>) must be under 2 ms.
        /// </summary>
        [Test]
        public void Stress_5000Cells_FullAutoPlay_StepsUnder2msAverage()
        {
            LevelSession s = LevelSession.Create(_level);
            var shots = new List<ShotEvent>(s.Belt.Capacity);
            long tickTicks = 0;
            int tickCount = 0;
            int shotCount = 0;

            GameState state = Evaluate(s);
            for (int i = 0; i < MaxTicks && state == GameState.Playing; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                shotCount += BeltAutoPlayer.Step(s, shots);
                tickTicks += Stopwatch.GetTimestamp() - t0;
                tickCount++;
                state = Evaluate(s);
            }

            double avgMs = tickCount > 0 ? tickTicks * 1000.0 / Stopwatch.Frequency / tickCount : 0.0;
            Debug.Log($"[StressTests] {Width * Height} cells, {_level.tanks.Length} tanks: {tickCount} ticks, " +
                      $"{shotCount} shots, total {tickTicks * 1000.0 / Stopwatch.Frequency:F3} ms, " +
                      $"avg {avgMs * 1000.0:F2} us/tick.");

            Assert.AreEqual(GameState.Won, state);
            Assert.AreEqual(0, s.Grid.RemainingCount);
            Assert.AreEqual(Width * Height, shotCount);
            Assert.Less(avgMs, 2.0, "Average belt tick time exceeds 2 ms.");
        }

        /// <summary>
        /// After the first 10 ticks, the full belt auto-play loop (bot launch, belt tick, rules evaluation)
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
            var shots = new List<ShotEvent>(measured.Belt.Capacity);

            for (int i = 0; i < WarmupTicks; i++)
            {
                PlayTick(warm, shots);
                PlayTick(measured, shots);
            }

            LevelSession current = warm;
            GameState state = GameState.Playing;
            int measuredRuns = 0;
            AllocAssert.NoAlloc(() =>
            {
                for (int i = 0; i < MaxTicks; i++)
                {
                    state = PlayTick(current, shots);
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
        /// One auto-play tick: <see cref="BeltAutoPlayer.Step"/>, then the belt rules.
        /// </summary>
        private static GameState PlayTick(LevelSession s, List<ShotEvent> shots)
        {
            BeltAutoPlayer.Step(s, shots);
            return Evaluate(s);
        }

        private static GameState Evaluate(LevelSession s)
        {
            return GameRules.Evaluate(s.Grid, s.Belt, s.Tray, s.Supply, s.BeltShooting);
        }

        /// <summary>
        /// 100x50 level with 8 colours in diagonal bands broken up by a hash pattern (no empty cells), tanks from
        /// <see cref="TankGenerator.Generate"/>, 3 lanes and one belt place and waiting slot per tank, so no lap can
        /// overflow the waiting slots and it is always solvable.
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
