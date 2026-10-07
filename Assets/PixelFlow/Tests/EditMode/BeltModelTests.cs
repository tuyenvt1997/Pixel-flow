using NUnit.Framework;
using PixelFlow.Core;
using System;

namespace PixelFlow.Tests
{
    /// <summary>
    /// Tests for BeltPath and BeltModel classes.
    /// </summary>
    public sealed class BeltModelTests
    {
        /// <summary>
        /// Test that BeltPath.Resolve maps every position correctly on a 3x2 board.
        /// </summary>
        [Test]
        public void Resolve_3x2_MapsEveryPosition()
        {
            // Arrange
            var path = new BeltPath(width: 3, height: 2);

            // Assert
            Assert.AreEqual(10, path.Length, "Length should be 2*3 + 2*2 = 10");

            // Bottom edge: positions 0, 1, 2 -> columns 0, 1, 2
            path.Resolve(0, out var side0, out var line0);
            Assert.AreEqual(BoardSide.Bottom, side0, "Position 0 should be Bottom");
            Assert.AreEqual(0, line0, "Position 0 should be column 0");

            path.Resolve(1, out var side1, out var line1);
            Assert.AreEqual(BoardSide.Bottom, side1, "Position 1 should be Bottom");
            Assert.AreEqual(1, line1, "Position 1 should be column 1");

            path.Resolve(2, out var side2, out var line2);
            Assert.AreEqual(BoardSide.Bottom, side2, "Position 2 should be Bottom");
            Assert.AreEqual(2, line2, "Position 2 should be column 2");

            // Right edge: positions 3, 4 -> rows 0, 1
            path.Resolve(3, out var side3, out var line3);
            Assert.AreEqual(BoardSide.Right, side3, "Position 3 should be Right");
            Assert.AreEqual(0, line3, "Position 3 should be row 0");

            path.Resolve(4, out var side4, out var line4);
            Assert.AreEqual(BoardSide.Right, side4, "Position 4 should be Right");
            Assert.AreEqual(1, line4, "Position 4 should be row 1");

            // Top edge: positions 5, 6, 7 -> columns 2, 1, 0 (reversed)
            path.Resolve(5, out var side5, out var line5);
            Assert.AreEqual(BoardSide.Top, side5, "Position 5 should be Top");
            Assert.AreEqual(2, line5, "Position 5 should be column 2");

            path.Resolve(6, out var side6, out var line6);
            Assert.AreEqual(BoardSide.Top, side6, "Position 6 should be Top");
            Assert.AreEqual(1, line6, "Position 6 should be column 1");

            path.Resolve(7, out var side7, out var line7);
            Assert.AreEqual(BoardSide.Top, side7, "Position 7 should be Top");
            Assert.AreEqual(0, line7, "Position 7 should be column 0");

            // Left edge: positions 8, 9 -> rows 1, 0 (reversed)
            path.Resolve(8, out var side8, out var line8);
            Assert.AreEqual(BoardSide.Left, side8, "Position 8 should be Left");
            Assert.AreEqual(1, line8, "Position 8 should be row 1");

            path.Resolve(9, out var side9, out var line9);
            Assert.AreEqual(BoardSide.Left, side9, "Position 9 should be Left");
            Assert.AreEqual(0, line9, "Position 9 should be row 0");
        }

        /// <summary>
        /// Test that BeltPath.Resolve throws ArgumentOutOfRangeException for out-of-range positions.
        /// </summary>
        [Test]
        public void Resolve_OutOfRange_Throws()
        {
            // Arrange
            var path = new BeltPath(width: 3, height: 2);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => path.Resolve(-1, out _, out _), "Should throw for position -1");
            Assert.Throws<ArgumentOutOfRangeException>(() => path.Resolve(10, out _, out _), "Should throw for position 10 (== Length)");
        }

        /// <summary>
        /// BeltModel exposes the path it was built with.
        /// </summary>
        [Test]
        public void Path_IsTheConstructorPath()
        {
            var belt = new BeltModel(2, new BeltPath(width: 3, height: 2));

            Assert.AreEqual(3, belt.Path.Width);
            Assert.AreEqual(2, belt.Path.Height);
            Assert.AreEqual(10, belt.Path.Length);
        }

        /// <summary>
        /// Test that BeltModel respects capacity including queued tanks.
        /// </summary>
        [Test]
        public void TryLaunch_RespectsCapacityIncludingQueue()
        {
            // Arrange
            var path = new BeltPath(width: 3, height: 2);
            var belt = new BeltModel(capacity: 2, path);
            var tankA = new ColorTankModel(id: 0, colorId: 0, ammo: 5);
            var tankB = new ColorTankModel(id: 1, colorId: 1, ammo: 5);
            var tankC = new ColorTankModel(id: 2, colorId: 2, ammo: 5);

            // Act
            var resultA = belt.TryLaunch(tankA);
            var resultB = belt.TryLaunch(tankB);
            var resultC = belt.TryLaunch(tankC);

            // Assert
            Assert.IsTrue(resultA, "Tank A should be accepted");
            Assert.IsTrue(resultB, "Tank B should be accepted");
            Assert.IsFalse(resultC, "Tank C should be rejected (capacity full)");
            Assert.AreEqual(2, belt.QueuedCount, "QueuedCount should be 2");
            Assert.AreEqual(0, belt.Count, "Count should be 0 (tanks still in queue)");
        }

        /// <summary>
        /// Test that TryAdmitFromQueue enters a tank at position zero and raises events.
        /// </summary>
        [Test]
        public void TryAdmitFromQueue_EntersAtPositionZero_RaisesEvents()
        {
            // Arrange
            var path = new BeltPath(width: 3, height: 2);
            var belt = new BeltModel(capacity: 5, path);
            var tank = new ColorTankModel(id: 0, colorId: 0, ammo: 5);

            ColorTankModel enteredTank = null;
            ColorTankModel movedTank = null;
            int movedPosition = -1;

            belt.OnTankEntered += t => enteredTank = t;
            belt.OnTankMoved += (t, pos) => { movedTank = t; movedPosition = pos; };

            belt.TryLaunch(tank);

            // Act
            var result = belt.TryAdmitFromQueue();

            // Assert
            Assert.IsTrue(result, "TryAdmitFromQueue should succeed");
            Assert.AreEqual(1, belt.Count, "Count should be 1");
            Assert.AreEqual(0, belt.QueuedCount, "QueuedCount should be 0");
            Assert.AreSame(tank, belt.TankAt(0), "Tank should be at index 0");
            Assert.AreEqual(0, belt.PositionAt(0), "Tank should be at position 0");
            Assert.AreEqual(0, belt.VisitedAt(0), "Visited count should be 0");
            Assert.AreSame(tank, enteredTank, "OnTankEntered should be raised");
            Assert.AreSame(tank, movedTank, "OnTankMoved should be raised");
            Assert.AreEqual(0, movedPosition, "Tank should be moved to position 0");
        }

        /// <summary>
        /// Test that the queue waits while position zero is occupied.
        /// </summary>
        [Test]
        public void Queue_WaitsWhilePositionZeroOccupied()
        {
            // Arrange
            var path = new BeltPath(width: 3, height: 2);
            var belt = new BeltModel(capacity: 5, path);
            var tankA = new ColorTankModel(id: 0, colorId: 0, ammo: 5);
            var tankB = new ColorTankModel(id: 1, colorId: 1, ammo: 5);

            belt.TryLaunch(tankA);
            belt.TryAdmitFromQueue(); // A enters at position 0
            belt.TryLaunch(tankB);

            // Act - try to admit B while A is still at position 0
            var result1 = belt.TryAdmitFromQueue();

            // Assert
            Assert.IsFalse(result1, "TryAdmitFromQueue should fail while position 0 is occupied");
            Assert.AreEqual(1, belt.Count, "Count should still be 1");
            Assert.AreEqual(1, belt.QueuedCount, "QueuedCount should still be 1");

            // Act - advance A, then try again
            belt.Advance(0);
            var result2 = belt.TryAdmitFromQueue();

            // Assert
            Assert.IsTrue(result2, "TryAdmitFromQueue should succeed after position 0 is clear");
            Assert.AreEqual(2, belt.Count, "Count should be 2");
            Assert.AreEqual(0, belt.QueuedCount, "QueuedCount should be 0");
        }

        /// <summary>
        /// Test that TryLaunch throws for null, depleted, or duplicate tanks.
        /// </summary>
        [Test]
        public void TryLaunch_DepletedOrDuplicate_Throws()
        {
            // Arrange
            var path = new BeltPath(width: 3, height: 2);
            var belt = new BeltModel(capacity: 5, path);
            var tank = new ColorTankModel(id: 0, colorId: 0, ammo: 1);

            // Deplete the tank
            tank.TryConsume();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => belt.TryLaunch(null), "Should throw for null tank");
            Assert.Throws<ArgumentException>(() => belt.TryLaunch(tank), "Should throw for depleted tank");

            // Test duplicate
            var activeTank = new ColorTankModel(id: 1, colorId: 1, ammo: 5);
            belt.TryLaunch(activeTank);
            belt.TryAdmitFromQueue();
            Assert.Throws<ArgumentException>(() => belt.TryLaunch(activeTank), "Should throw for tank already on belt");

            // Test queued duplicate
            var queuedTank = new ColorTankModel(id: 2, colorId: 2, ammo: 5);
            belt.TryLaunch(queuedTank);
            Assert.Throws<ArgumentException>(() => belt.TryLaunch(queuedTank), "Should throw for tank already in queue");
        }

        /// <summary>
        /// Test that Advance and RemoveAt raise events and keep entry order.
        /// </summary>
        [Test]
        public void Advance_And_RemoveAt_RaiseEvents_AndKeepEntryOrder()
        {
            // Arrange
            var path = new BeltPath(width: 3, height: 2);
            var belt = new BeltModel(capacity: 5, path);
            var tankA = new ColorTankModel(id: 0, colorId: 0, ammo: 5);
            var tankB = new ColorTankModel(id: 1, colorId: 1, ammo: 5);
            var tankC = new ColorTankModel(id: 2, colorId: 2, ammo: 5);

            ColorTankModel lastMovedTank = null;
            int lastMovedPosition = -1;
            ColorTankModel lastLeftTank = null;
            bool lastLeftLapCompleted = false;

            belt.OnTankMoved += (t, pos) => { lastMovedTank = t; lastMovedPosition = pos; };
            belt.OnTankLeft += (t, lap) => { lastLeftTank = t; lastLeftLapCompleted = lap; };

            belt.TryLaunch(tankA);
            belt.TryLaunch(tankB);
            belt.TryLaunch(tankC);
            belt.TryAdmitFromQueue(); // A enters at position 0
            belt.Advance(0); // Move A to position 1 to free position 0
            belt.TryAdmitFromQueue(); // B enters at position 0
            belt.Advance(1); // Move B to position 1 (A is now at 1, B will be at 1 too after this)
            belt.TryAdmitFromQueue(); // C enters at position 0

            // Act - advance A again
            belt.Advance(0); // Advance A (currently at position 1) to position 2

            // Assert
            Assert.AreSame(tankA, lastMovedTank, "OnTankMoved should be raised for A");
            Assert.AreEqual(2, lastMovedPosition, "A should move to position 2");
            Assert.AreEqual(2, belt.PositionAt(0), "A should be at position 2");

            // Act - mark visited for all tanks
            belt.MarkVisited(0); // A
            belt.MarkVisited(1); // B
            belt.MarkVisited(2); // C

            // Assert
            Assert.AreEqual(1, belt.VisitedAt(0), "A visited count should be 1");
            Assert.AreEqual(1, belt.VisitedAt(1), "B visited count should be 1");
            Assert.AreEqual(1, belt.VisitedAt(2), "C visited count should be 1");

            // Act - remove B (middle)
            belt.RemoveAt(1, lapCompleted: true);

            // Assert
            Assert.AreEqual(2, belt.Count, "Count should be 2");
            Assert.AreSame(tankA, belt.TankAt(0), "A should still be at index 0");
            Assert.AreSame(tankC, belt.TankAt(1), "C should now be at index 1");
            Assert.AreSame(tankB, lastLeftTank, "OnTankLeft should be raised for B");
            Assert.IsTrue(lastLeftLapCompleted, "Lap should be marked as completed");
        }

        /// <summary>
        /// Test that Clear empties both belt and queue.
        /// </summary>
        [Test]
        public void Clear_EmptiesBeltAndQueue()
        {
            // Arrange
            var path = new BeltPath(width: 3, height: 2);
            var belt = new BeltModel(capacity: 5, path);
            var tankA = new ColorTankModel(id: 0, colorId: 0, ammo: 5);
            var tankB = new ColorTankModel(id: 1, colorId: 1, ammo: 5);

            belt.TryLaunch(tankA);
            belt.TryLaunch(tankB);
            belt.TryAdmitFromQueue(); // A enters

            // Act
            belt.Clear();

            // Assert
            Assert.AreEqual(0, belt.Count, "Count should be 0");
            Assert.AreEqual(0, belt.QueuedCount, "QueuedCount should be 0");
            Assert.IsFalse(belt.IsFull, "IsFull should be false");
        }
    }
}
