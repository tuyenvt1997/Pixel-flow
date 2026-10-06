using System.Collections.Generic;
using NUnit.Framework;
using PixelFlow.Core;
using PixelFlow.Data;
using UnityEngine;

namespace PixelFlow.Tests
{
    /// <summary>
    /// Tests for <see cref="GameRules.Evaluate(PixelGridModel, BeltModel, SlotQueueManager, SupplyModel, BeltShootingLogic)"/>
    /// and for <see cref="AutoPlayer"/>.
    /// </summary>
    public sealed class BeltRulesTests
    {
        private static readonly Color32[] TwoColors = { Color.red, Color.blue };

        private static LevelSession CreateSession(string[] rows, ColorTankData[] tanks, int lanes = 1, int slots = 5)
        {
            return LevelSession.Create(TestLevels.Create(rows, TwoColors, tanks, lanes, slots));
        }

        private static GameState Evaluate(LevelSession s)
        {
            return GameRules.Evaluate(s.Grid, s.Belt, s.Tray, s.Supply, s.BeltShooting);
        }

        /// <summary>
        /// The game is Won once no pixels remain.
        /// </summary>
        [Test]
        public void Evaluate_Won()
        {
            var s = CreateSession(new[] { "0" }, new[] { new ColorTankData { colorId = 0, ammo = 1 } });
            s.Supply.TryTakeFront(0, out ColorTankModel tank);
            s.Belt.TryLaunch(tank);
            var output = new List<ShotEvent>();

            Assert.AreEqual(GameState.Playing, Evaluate(s));
            s.BeltShooting.Tick(output); // admit
            s.BeltShooting.Tick(output); // fire

            Assert.AreEqual(0, s.Grid.RemainingCount);
            Assert.AreEqual(GameState.Won, Evaluate(s));
        }

        /// <summary>
        /// The game is Lost once a tank finishes its lap with ammo while every waiting slot is full.
        /// </summary>
        [Test]
        public void Evaluate_Overflow_IsLost()
        {
            var tanks = new[]
            {
                new ColorTankData { colorId = 0, ammo = 1 },
                new ColorTankData { colorId = 0, ammo = 1 },
                new ColorTankData { colorId = 1, ammo = 1 },
            };
            var s = CreateSession(new[] { "1" }, tanks, lanes: 1, slots: 1);
            s.Supply.TryTakeFront(0, out ColorTankModel parked);
            s.Tray.TryAdd(parked);
            s.Supply.TryTakeFront(0, out ColorTankModel rider);
            s.Belt.TryLaunch(rider);
            var output = new List<ShotEvent>();

            s.BeltShooting.Tick(output); // admit
            for (int i = 0; i < new BeltPath(1, 1).Length; i++)
            {
                Assert.AreEqual(GameState.Playing, Evaluate(s), $"Still playing before the lap ends (tick {i})");
                s.BeltShooting.Tick(output);
            }

            Assert.IsTrue(s.BeltShooting.Overflowed);
            Assert.IsFalse(s.Supply.IsEmpty, "Overflow is Lost even though supply still has a tank that could win");
            Assert.AreEqual(GameState.Lost, Evaluate(s));
        }

        /// <summary>
        /// Overflow is checked before Won: a tank that cleared the last pixel and then overflows the waiting slots
        /// leaves the game Lost even though no pixels remain.
        /// </summary>
        [Test]
        public void Evaluate_OverflowWithBoardCleared_IsLost()
        {
            var tanks = new[]
            {
                new ColorTankData { colorId = 0, ammo = 1 },
                new ColorTankData { colorId = 0, ammo = 2 },
            };
            var s = CreateSession(new[] { "0" }, tanks, lanes: 1, slots: 1);
            s.Supply.TryTakeFront(0, out ColorTankModel parked);
            s.Tray.TryAdd(parked);
            s.Supply.TryTakeFront(0, out ColorTankModel rider);
            s.Belt.TryLaunch(rider);
            var output = new List<ShotEvent>();

            s.BeltShooting.Tick(output); // admit
            for (int i = 0; i < new BeltPath(1, 1).Length; i++)
            {
                s.BeltShooting.Tick(output); // the first tick clears the only pixel; the last one ends the lap
            }

            Assert.AreEqual(0, s.Grid.RemainingCount, "The rider cleared the board");
            Assert.AreEqual(1, rider.Ammo, "The rider finished its lap with ammo left");
            Assert.IsTrue(s.BeltShooting.Overflowed);
            Assert.AreEqual(GameState.Lost, Evaluate(s));
        }

        /// <summary>
        /// The game is Lost when supply, belt and queue are empty and no slot tank's colour is on any front.
        /// </summary>
        [Test]
        public void Evaluate_Stuck_IsLost()
        {
            var s = CreateSession(new[] { "1" }, new[] { new ColorTankData { colorId = 0, ammo = 2 } });
            s.Supply.TryTakeFront(0, out ColorTankModel tank);
            s.Belt.TryLaunch(tank);
            var output = new List<ShotEvent>();

            s.BeltShooting.Tick(output); // admit
            for (int i = 0; i < new BeltPath(1, 1).Length; i++)
            {
                Assert.AreEqual(GameState.Playing, Evaluate(s), "Playing while the tank rides");
                s.BeltShooting.Tick(output);
            }

            Assert.AreEqual(0, s.Belt.Count);
            Assert.AreEqual(1, s.Tray.Count);
            Assert.IsFalse(s.BeltShooting.Overflowed);
            Assert.IsFalse(s.BeltShooting.CanAnyWaitingTankHit());
            Assert.AreEqual(GameState.Lost, Evaluate(s));
        }

        /// <summary>
        /// The game is still Playing when supply, belt and queue are empty but a slot tank can still hit a front.
        /// </summary>
        [Test]
        public void Evaluate_SlotTankCanStillHit_IsPlaying()
        {
            var s = CreateSession(new[] { "1" }, new[] { new ColorTankData { colorId = 1, ammo = 1 } });
            s.Supply.TryTakeFront(0, out ColorTankModel tank);
            s.Tray.TryAdd(tank);

            Assert.IsTrue(s.Supply.IsEmpty);
            Assert.AreEqual(0, s.Belt.Count + s.Belt.QueuedCount);
            Assert.IsTrue(s.BeltShooting.CanAnyWaitingTankHit());
            Assert.AreEqual(GameState.Playing, Evaluate(s));
        }

        /// <summary>
        /// The game is Lost when everything is empty but pixels remain.
        /// </summary>
        [Test]
        public void Evaluate_NothingLeftWithPixels_IsLost()
        {
            var s = CreateSession(new[] { "1" }, new ColorTankData[0]);

            Assert.AreEqual(GameState.Lost, Evaluate(s));
        }

        /// <summary>
        /// Evaluate and CanAnyWaitingTankHit do not allocate.
        /// </summary>
        [Test]
        public void Evaluate_DoesNotAllocate()
        {
            var tanks = new[]
            {
                new ColorTankData { colorId = 0, ammo = 1 },
                new ColorTankData { colorId = 0, ammo = 1 },
                new ColorTankData { colorId = 1, ammo = 1 },
            };
            var s = CreateSession(new[] { "01", "10" }, tanks);
            s.Supply.TryTakeFront(0, out ColorTankModel slotTank);
            s.Tray.TryAdd(slotTank);
            s.Supply.TryTakeFront(0, out ColorTankModel beltTank);
            s.Belt.TryLaunch(beltTank);

            AllocAssert.NoAlloc(() => s.BeltShooting.CanAnyWaitingTankHit());
            AllocAssert.NoAlloc(() => Evaluate(s));
        }

        /// <summary>
        /// The auto-player wins a small two-colour level.
        /// </summary>
        [Test]
        public void AutoPlayer_WinsSimpleLevel()
        {
            var tanks = new[]
            {
                new ColorTankData { colorId = 0, ammo = 2 },
                new ColorTankData { colorId = 1, ammo = 2 },
            };
            var s = CreateSession(new[] { "01", "10" }, tanks, lanes: 1, slots: 5);

            Assert.AreEqual(GameState.Won, AutoPlayer.Play(s));
            Assert.AreEqual(0, s.Grid.RemainingCount);
        }
    }
}
