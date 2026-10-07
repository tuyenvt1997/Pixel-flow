using System.Collections.Generic;
using NUnit.Framework;
using PixelFlow.Core;
using PixelFlow.Data;
using UnityEngine;

namespace PixelFlow.Tests
{
    /// <summary>
    /// Tests for <see cref="BeltShootingLogic"/> and <see cref="SlotQueueManager.TryRemove"/>.
    /// </summary>
    public sealed class BeltShootingTests
    {
        private static readonly Color32[] TwoColors = { Color.red, Color.blue };

        private static LevelSession CreateSession(string[] rows, ColorTankData[] tanks, int lanes = 1, int slots = 5)
        {
            return LevelSession.Create(TestLevels.Create(rows, TwoColors, tanks, lanes, slots));
        }

        /// <summary>
        /// Takes the front tank of lane 0 and puts it straight onto the belt at position 0.
        /// </summary>
        private static ColorTankModel LaunchAndAdmit(LevelSession s)
        {
            s.Supply.TryTakeFront(0, out ColorTankModel tank);
            Assert.IsTrue(s.Belt.TryLaunch(tank), "Launch should be accepted");
            Assert.IsTrue(s.Belt.TryAdmitFromQueue(), "Tank should enter at position 0");
            return tank;
        }

        /// <summary>
        /// Each tick fires at the front of the line given by BeltPath.Resolve for the tank's position.
        /// On a full 3x3 board the corner lines (positions 3, 6, 9, 11) were already emptied by earlier shots.
        /// </summary>
        [Test]
        public void Tick_FiresAtFrontOnEachSide()
        {
            var s = CreateSession(new[] { "000", "000", "000" },
                new[] { new ColorTankData { colorId = 0, ammo = 100 } });
            ColorTankModel tank = LaunchAndAdmit(s);
            var path = new BeltPath(3, 3);

            // Expected hit per position 0..11; null = the line is empty, so no shot.
            var expected = new Vector2Int?[]
            {
                new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), // bottom, columns 0..2
                null, new Vector2Int(2, 1), new Vector2Int(2, 2),                 // right, rows 0..2
                null, new Vector2Int(1, 2), new Vector2Int(0, 2),                 // top, columns 2..0
                null, new Vector2Int(0, 1), null,                                 // left, rows 2..0
            };

            var output = new List<ShotEvent>();
            for (int position = 0; position < 12; position++)
            {
                path.Resolve(position, out BoardSide side, out int line);
                bool hasFront = s.Grid.TryGetFront(side, line, out Vector2Int front);
                Assert.AreEqual(expected[position].HasValue, hasFront, $"Front existence at position {position}");
                if (hasFront)
                {
                    Assert.AreEqual(expected[position].Value, front, $"Resolved front at position {position}");
                }

                output.Clear();
                int shots = s.BeltShooting.Tick(output);

                if (expected[position].HasValue)
                {
                    Assert.AreEqual(1, shots, $"One shot at position {position}");
                    Assert.AreEqual(1, output.Count);
                    Assert.AreEqual(expected[position].Value, output[0].Cell, $"Hit cell at position {position}");
                    Assert.AreEqual(position, output[0].BeltPosition, $"BeltPosition at position {position}");
                    Assert.AreSame(tank, output[0].Tank);
                    Assert.AreEqual(0, output[0].ColorId);
                    Assert.AreEqual(LevelData.EmptyCell, s.Grid.GetCell(output[0].Cell.x, output[0].Cell.y));
                }
                else
                {
                    Assert.AreEqual(0, shots, $"No shot at position {position}");
                    Assert.AreEqual(0, output.Count);
                }
            }

            Assert.AreEqual(100 - 8, tank.Ammo, "8 shots fired over the lap");
            Assert.AreEqual(1, s.Grid.RemainingCount, "Only the centre cell is never a front-on-arrival");
        }

        /// <summary>
        /// A tank whose colour does not match the front cell neither fires nor consumes ammo.
        /// </summary>
        [Test]
        public void Tick_MismatchedColour_DoesNotFireOrConsume()
        {
            var s = CreateSession(new[] { "1" }, new[] { new ColorTankData { colorId = 0, ammo = 3 } });
            ColorTankModel tank = LaunchAndAdmit(s);
            var output = new List<ShotEvent>();

            int shots = s.BeltShooting.Tick(output);

            Assert.AreEqual(0, shots);
            Assert.AreEqual(0, output.Count);
            Assert.AreEqual(3, tank.Ammo);
            Assert.AreEqual(1, s.Grid.RemainingCount);
            Assert.AreEqual(1, s.Belt.Count, "Tank keeps riding");
            Assert.AreEqual(1, s.Belt.PositionAt(0));
            Assert.AreEqual(1, s.Belt.VisitedAt(0));
        }

        /// <summary>
        /// A tank that fires its last ammo leaves the belt and does not go to a waiting slot.
        /// </summary>
        [Test]
        public void Tick_DepletedTank_LeavesBelt_NotToSlot()
        {
            var s = CreateSession(new[] { "00" }, new[] { new ColorTankData { colorId = 0, ammo = 1 } });
            ColorTankModel tank = LaunchAndAdmit(s);
            ColorTankModel left = null;
            bool? lap = null;
            s.Belt.OnTankLeft += (t, l) => { left = t; lap = l; };

            int shots = s.BeltShooting.Tick(new List<ShotEvent>());

            Assert.AreEqual(1, shots);
            Assert.IsTrue(tank.IsDepleted);
            Assert.AreEqual(0, s.Belt.Count, "Depleted tank leaves the belt");
            Assert.AreSame(tank, left);
            Assert.AreEqual(false, lap);
            Assert.AreEqual(0, s.Tray.Count, "Depleted tank does not go to a slot");
            Assert.IsFalse(s.BeltShooting.Overflowed);
        }

        /// <summary>
        /// A tank that depletes on the last visit of its lap leaves the belt instead of going to a slot.
        /// The tank is moved forward before its first tick so its last visit lands on the top of column 1,
        /// the only position from which the colour-0 cell is a front.
        /// </summary>
        [Test]
        public void Tick_DepletesOnLastPosition_DoesNotGoToSlot()
        {
            var s = CreateSession(new[] { "101", "111" }, new[] { new ColorTankData { colorId = 0, ammo = 1 } });
            ColorTankModel tank = LaunchAndAdmit(s);
            int lapLength = new BeltPath(3, 2).Length; // 10; top of column 1 is position 6
            for (int i = 0; i < 7; i++)
            {
                s.Belt.Advance(0); // start at position 7, so the 10th visit is at position 6
            }

            bool? lap = null;
            s.Belt.OnTankLeft += (t, l) => lap = l;
            var output = new List<ShotEvent>();

            for (int tick = 1; tick < lapLength; tick++)
            {
                s.BeltShooting.Tick(output);
            }

            Assert.AreEqual(0, output.Count, "No shot before the last visit");
            Assert.AreEqual(1, s.Belt.Count);
            Assert.AreEqual(6, s.Belt.PositionAt(0));
            Assert.AreEqual(lapLength - 1, s.Belt.VisitedAt(0));

            s.BeltShooting.Tick(output);

            Assert.AreEqual(1, output.Count);
            Assert.AreEqual(new Vector2Int(1, 1), output[0].Cell);
            Assert.IsTrue(tank.IsDepleted);
            Assert.AreEqual(0, s.Belt.Count);
            Assert.AreEqual(false, lap);
            Assert.AreEqual(0, s.Tray.Count, "Depleted tank does not go to a slot");
            Assert.IsFalse(s.BeltShooting.Overflowed);
        }

        /// <summary>
        /// A tank that finishes its lap with ammo left goes to the first free waiting slot after exactly L ticks.
        /// </summary>
        [Test]
        public void Tick_LapWithAmmo_GoesToFirstFreeSlot()
        {
            var s = CreateSession(new[] { "11", "11" }, new[] { new ColorTankData { colorId = 0, ammo = 4 } });
            ColorTankModel tank = LaunchAndAdmit(s);
            int lapLength = new BeltPath(2, 2).Length;
            ColorTankModel left = null;
            bool? lap = null;
            s.Belt.OnTankLeft += (t, l) => { left = t; lap = l; };
            var output = new List<ShotEvent>();

            for (int tick = 1; tick < lapLength; tick++)
            {
                s.BeltShooting.Tick(output);
            }

            Assert.AreEqual(1, s.Belt.Count, "Still on the belt after L-1 ticks");
            Assert.AreEqual(0, s.Tray.Count);

            s.BeltShooting.Tick(output);

            Assert.AreEqual(0, s.Belt.Count);
            Assert.AreEqual(1, s.Tray.Count);
            Assert.AreSame(tank, s.Tray[0]);
            Assert.AreSame(tank, left);
            Assert.AreEqual(true, lap);
            Assert.AreEqual(4, tank.Ammo);
            Assert.IsFalse(s.BeltShooting.Overflowed);
        }

        /// <summary>
        /// A tank that finishes its lap while every waiting slot is full latches Overflowed.
        /// </summary>
        [Test]
        public void Tick_LapWithSlotsFull_SetsOverflowed()
        {
            var tanks = new ColorTankData[2];
            for (int i = 0; i < tanks.Length; i++)
            {
                tanks[i] = new ColorTankData { colorId = 0, ammo = 2 };
            }

            var s = CreateSession(new[] { "1" }, tanks, lanes: 1, slots: 1);
            s.Supply.TryTakeFront(0, out ColorTankModel parked);
            Assert.IsTrue(s.Tray.TryAdd(parked));
            ColorTankModel rider = LaunchAndAdmit(s);
            int lapLength = new BeltPath(1, 1).Length;
            var overflowed = new List<ColorTankModel>();
            bool overflowedWhenRaised = false;
            s.BeltShooting.OnOverflow += t =>
            {
                overflowed.Add(t);
                overflowedWhenRaised = s.BeltShooting.Overflowed;
            };

            for (int tick = 0; tick < lapLength; tick++)
            {
                Assert.IsFalse(s.BeltShooting.Overflowed);
                Assert.AreEqual(0, overflowed.Count, "OnOverflow raised before the lap ended");
                s.BeltShooting.Tick(new List<ShotEvent>());
            }

            Assert.IsTrue(s.BeltShooting.Overflowed);
            Assert.AreEqual(0, s.Belt.Count, "Tank left the belt");
            Assert.AreEqual(1, s.Tray.Count);
            Assert.AreSame(parked, s.Tray[0]);
            Assert.AreEqual(1, overflowed.Count, "OnOverflow raised once");
            Assert.AreSame(rider, overflowed[0], "OnOverflow carries the overflowed tank");
            Assert.IsTrue(overflowedWhenRaised, "Overflowed is already true when OnOverflow is raised");
        }

        /// <summary>
        /// A tank that finishes its lap into a free waiting slot does not raise OnOverflow.
        /// </summary>
        [Test]
        public void Tick_LapIntoFreeSlot_DoesNotRaiseOnOverflow()
        {
            var s = CreateSession(new[] { "1" }, new[] { new ColorTankData { colorId = 0, ammo = 2 } });
            LaunchAndAdmit(s);
            int raised = 0;
            s.BeltShooting.OnOverflow += _ => raised++;

            for (int tick = 0; tick < new BeltPath(1, 1).Length; tick++)
            {
                s.BeltShooting.Tick(new List<ShotEvent>());
            }

            Assert.AreEqual(1, s.Tray.Count, "Tank went to the waiting slot");
            Assert.AreEqual(0, raised);
        }

        /// <summary>
        /// The shooting logic uses the belt's path and rejects a belt whose path does not match the grid's size.
        /// </summary>
        [Test]
        public void Ctor_BeltPathSizeMismatch_Throws()
        {
            var grid = new PixelGridModel(3, 2, new byte[6]);
            var slots = new SlotQueueManager(1);

            Assert.Throws<System.ArgumentException>(
                () => new BeltShootingLogic(grid, new BeltModel(1, new BeltPath(2, 3)), slots), "Width/height swapped");
            Assert.Throws<System.ArgumentException>(
                () => new BeltShootingLogic(grid, new BeltModel(1, new BeltPath(3, 3)), slots), "Height differs");
            Assert.DoesNotThrow(() => new BeltShootingLogic(grid, new BeltModel(1, new BeltPath(3, 2)), slots));
        }

        /// <summary>
        /// Queued tanks enter after belt tanks are processed, once position 0 is free.
        /// </summary>
        [Test]
        public void Tick_AdmitsQueuedTankAfterProcessing()
        {
            var tanks = new[]
            {
                new ColorTankData { colorId = 0, ammo = 5 },
                new ColorTankData { colorId = 0, ammo = 5 },
            };
            var s = CreateSession(new[] { "11", "11" }, tanks);
            ColorTankModel first = LaunchAndAdmit(s);
            s.Supply.TryTakeFront(0, out ColorTankModel second);
            Assert.IsTrue(s.Belt.TryLaunch(second));
            Assert.AreEqual(1, s.Belt.QueuedCount);

            s.BeltShooting.Tick(new List<ShotEvent>());

            Assert.AreEqual(2, s.Belt.Count, "Second tank entered once the first moved off position 0");
            Assert.AreEqual(0, s.Belt.QueuedCount);
            Assert.AreSame(first, s.Belt.TankAt(0));
            Assert.AreEqual(1, s.Belt.PositionAt(0));
            Assert.AreSame(second, s.Belt.TankAt(1));
            Assert.AreEqual(0, s.Belt.PositionAt(1));
            Assert.AreEqual(0, s.Belt.VisitedAt(1), "Entering tank was not processed this tick");
        }

        /// <summary>
        /// Tick does not allocate on a 64x64 board with five tanks on the belt.
        /// </summary>
        [Test]
        public void Tick_DoesNotAllocate()
        {
            var rows = new string[64];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = new string('0', 64);
            }

            var tanks = new ColorTankData[5];
            for (int i = 0; i < tanks.Length; i++)
            {
                tanks[i] = new ColorTankData { colorId = 0, ammo = 100000 };
            }

            var s = CreateSession(rows, tanks, lanes: 1, slots: 5);
            for (int i = 0; i < tanks.Length; i++)
            {
                s.Supply.TryTakeFront(0, out ColorTankModel tank);
                Assert.IsTrue(s.Belt.TryLaunch(tank));
            }

            var output = new List<ShotEvent>(16);
            for (int i = 0; i < 50; i++)
            {
                output.Clear();
                s.BeltShooting.Tick(output);
            }

            Assert.AreEqual(5, s.Belt.Count, "All tanks are on the belt after warm-up");

            int shots = 0;
            AllocAssert.NoAlloc(() =>
            {
                output.Clear();
                shots = s.BeltShooting.Tick(output);
            });
            Assert.Greater(shots, 0, "The measured tick fired");
        }

        /// <summary>
        /// TryRemove shifts the remaining slot tanks left and raises OnTankRemoved with the old index.
        /// </summary>
        [Test]
        public void TryRemove_ShiftsLeft_AndRaisesEvent()
        {
            var slots = new SlotQueueManager(5);
            var a = new ColorTankModel(0, 0, 1);
            var b = new ColorTankModel(1, 0, 1);
            var c = new ColorTankModel(2, 0, 1);
            slots.TryAdd(a);
            slots.TryAdd(b);
            slots.TryAdd(c);
            ColorTankModel removed = null;
            int removedIndex = -1;
            slots.OnTankRemoved += (t, i) => { removed = t; removedIndex = i; };

            Assert.IsTrue(slots.TryRemove(b));

            Assert.AreSame(b, removed);
            Assert.AreEqual(1, removedIndex);
            Assert.AreEqual(2, slots.Count);
            Assert.AreSame(a, slots[0]);
            Assert.AreSame(c, slots[1]);
            Assert.AreEqual(-1, slots.IndexOf(b));
        }

        /// <summary>
        /// TryRemove returns false and changes nothing for a tank that is not in the slots.
        /// </summary>
        [Test]
        public void TryRemove_Absent_ReturnsFalse()
        {
            var slots = new SlotQueueManager(5);
            var a = new ColorTankModel(0, 0, 1);
            slots.TryAdd(a);
            bool raised = false;
            slots.OnTankRemoved += (t, i) => raised = true;

            Assert.IsFalse(slots.TryRemove(new ColorTankModel(1, 0, 1)));

            Assert.IsFalse(raised);
            Assert.AreEqual(1, slots.Count);
            Assert.AreSame(a, slots[0]);
        }
    }
}
