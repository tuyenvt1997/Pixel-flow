using NUnit.Framework;
using PixelFlow.Core;
using System;
using System.Collections.Generic;

namespace PixelFlow.Tests
{
    [TestFixture]
    public class TankModelsTests
    {
        #region ColorTankModel Tests

        [Test]
        public void Tank_TryConsume_DecrementsAndRaisesAmmoChanged()
        {
            var tank = new ColorTankModel(1, 3, 5);
            int eventFiredCount = 0;
            ColorTankModel eventTank = null;
            int eventAmmo = -1;

            tank.OnAmmoChanged += (t, a) =>
            {
                eventFiredCount++;
                eventTank = t;
                eventAmmo = a;
            };

            Assert.That(tank.TryConsume(), Is.True);
            Assert.That(tank.Ammo, Is.EqualTo(4));
            Assert.That(eventFiredCount, Is.EqualTo(1));
            Assert.That(eventTank, Is.SameAs(tank));
            Assert.That(eventAmmo, Is.EqualTo(4));

            Assert.That(tank.TryConsume(), Is.True);
            Assert.That(tank.Ammo, Is.EqualTo(3));
            Assert.That(eventFiredCount, Is.EqualTo(2));
            Assert.That(eventAmmo, Is.EqualTo(3));
        }

        [Test]
        public void Tank_LastConsume_RaisesDepletedOnce_ThenTryConsumeReturnsFalse()
        {
            var tank = new ColorTankModel(1, 3, 2);
            int ammoChangedCount = 0;
            int depletedCount = 0;
            ColorTankModel depletedTank = null;

            tank.OnAmmoChanged += (t, a) => ammoChangedCount++;
            tank.OnDepleted += t =>
            {
                depletedCount++;
                depletedTank = t;
            };

            // First consume: 2 -> 1
            Assert.That(tank.TryConsume(), Is.True);
            Assert.That(tank.Ammo, Is.EqualTo(1));
            Assert.That(tank.IsDepleted, Is.False);
            Assert.That(ammoChangedCount, Is.EqualTo(1));
            Assert.That(depletedCount, Is.EqualTo(0));

            // Second consume: 1 -> 0 (depleted)
            Assert.That(tank.TryConsume(), Is.True);
            Assert.That(tank.Ammo, Is.EqualTo(0));
            Assert.That(tank.IsDepleted, Is.True);
            Assert.That(ammoChangedCount, Is.EqualTo(2));
            Assert.That(depletedCount, Is.EqualTo(1));
            Assert.That(depletedTank, Is.SameAs(tank));

            // Third attempt: already depleted
            Assert.That(tank.TryConsume(), Is.False);
            Assert.That(tank.Ammo, Is.EqualTo(0));
            Assert.That(ammoChangedCount, Is.EqualTo(2)); // No change
            Assert.That(depletedCount, Is.EqualTo(1)); // Still just once
        }

        [Test]
        public void Tank_NonPositiveAmmo_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ColorTankModel(1, 3, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ColorTankModel(1, 3, -1));
            Assert.DoesNotThrow(() => new ColorTankModel(1, 3, 1));
        }

        #endregion

        #region SlotQueueManager Tests

        [Test]
        public void TryAdd_FillsSlotsInOrder_RaisesOnTankAdded()
        {
            var manager = new SlotQueueManager(3);
            var events = new List<(ColorTankModel tank, int slot)>();
            manager.OnTankAdded += (t, s) => events.Add((t, s));

            var tankA = new ColorTankModel(1, 10, 20);
            var tankB = new ColorTankModel(2, 11, 20);
            var tankC = new ColorTankModel(3, 12, 20);

            Assert.That(manager.TryAdd(tankA), Is.True);
            Assert.That(manager.Count, Is.EqualTo(1));
            Assert.That(manager[0], Is.SameAs(tankA));
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0].tank, Is.SameAs(tankA));
            Assert.That(events[0].slot, Is.EqualTo(0));

            Assert.That(manager.TryAdd(tankB), Is.True);
            Assert.That(manager.Count, Is.EqualTo(2));
            Assert.That(manager[1], Is.SameAs(tankB));
            Assert.That(events[1].slot, Is.EqualTo(1));

            Assert.That(manager.TryAdd(tankC), Is.True);
            Assert.That(manager.Count, Is.EqualTo(3));
            Assert.That(manager[2], Is.SameAs(tankC));
            Assert.That(events[2].slot, Is.EqualTo(2));

            Assert.That(manager.IsFull, Is.True);
        }

        [Test]
        public void TryAdd_WhenFull_ReturnsFalse_AndLeavesSlotsUnchanged()
        {
            var manager = new SlotQueueManager(2);
            var events = new List<(ColorTankModel tank, int slot)>();
            manager.OnTankAdded += (t, s) => events.Add((t, s));

            var tankA = new ColorTankModel(1, 10, 20);
            var tankB = new ColorTankModel(2, 11, 20);
            var tankC = new ColorTankModel(3, 12, 20);

            Assert.That(manager.TryAdd(tankA), Is.True);
            Assert.That(manager.TryAdd(tankB), Is.True);
            Assert.That(manager.Count, Is.EqualTo(2));
            Assert.That(manager.IsFull, Is.True);

            // Try to add third tank
            Assert.That(manager.TryAdd(tankC), Is.False);
            Assert.That(manager.Count, Is.EqualTo(2));
            Assert.That(manager[0], Is.SameAs(tankA));
            Assert.That(manager[1], Is.SameAs(tankB));
            Assert.That(events.Count, Is.EqualTo(2)); // No new event
        }

        [Test]
        public void FirstTankDepleted_ShiftsOthersLeft()
        {
            var manager = new SlotQueueManager(5);
            var removedEvents = new List<(ColorTankModel tank, int oldIndex)>();
            manager.OnTankRemoved += (t, i) => removedEvents.Add((t, i));

            var tankA = new ColorTankModel(1, 10, 1);
            var tankB = new ColorTankModel(2, 11, 20);
            var tankC = new ColorTankModel(3, 12, 20);

            manager.TryAdd(tankA);
            manager.TryAdd(tankB);
            manager.TryAdd(tankC);

            Assert.That(manager.Count, Is.EqualTo(3));
            Assert.That(manager[0], Is.SameAs(tankA));

            // Deplete tankA
            tankA.TryConsume();
            Assert.That(tankA.IsDepleted, Is.True);

            // Manager should have removed it and shifted others
            Assert.That(manager.Count, Is.EqualTo(2));
            Assert.That(manager[0], Is.SameAs(tankB));
            Assert.That(manager[1], Is.SameAs(tankC));
            Assert.That(removedEvents.Count, Is.EqualTo(1));
            Assert.That(removedEvents[0].tank, Is.SameAs(tankA));
            Assert.That(removedEvents[0].oldIndex, Is.EqualTo(0));
        }

        [Test]
        public void MiddleTankDepleted_ShiftsOnlyTanksBehind()
        {
            var manager = new SlotQueueManager(5);
            var removedEvents = new List<(ColorTankModel tank, int oldIndex)>();
            manager.OnTankRemoved += (t, i) => removedEvents.Add((t, i));

            var tankA = new ColorTankModel(1, 10, 20);
            var tankB = new ColorTankModel(2, 11, 1);
            var tankC = new ColorTankModel(3, 12, 20);

            manager.TryAdd(tankA);
            manager.TryAdd(tankB);
            manager.TryAdd(tankC);

            Assert.That(manager.Count, Is.EqualTo(3));

            // Deplete tankB (middle)
            tankB.TryConsume();
            Assert.That(tankB.IsDepleted, Is.True);

            // Manager should have removed it and shifted C left
            Assert.That(manager.Count, Is.EqualTo(2));
            Assert.That(manager[0], Is.SameAs(tankA));
            Assert.That(manager[1], Is.SameAs(tankC));
            Assert.That(removedEvents.Count, Is.EqualTo(1));
            Assert.That(removedEvents[0].tank, Is.SameAs(tankB));
            Assert.That(removedEvents[0].oldIndex, Is.EqualTo(1));
        }

        #endregion

        #region SupplyModel Tests

        [Test]
        public void Supply_DistributesRoundRobin()
        {
            var tanks = new List<ColorTankModel>();
            for (int i = 0; i < 7; i++)
            {
                tanks.Add(new ColorTankModel(i, (byte)(10 + i), 20));
            }

            var supply = new SupplyModel(tanks, 3);

            Assert.That(supply.LaneCount, Is.EqualTo(3));
            Assert.That(supply.TotalRemaining, Is.EqualTo(7));
            Assert.That(supply.IsEmpty, Is.False);

            // Lane 0: tanks 0, 3, 6
            var lane0 = supply.GetLane(0);
            Assert.That(lane0.Count, Is.EqualTo(3));
            Assert.That(lane0[0].Id, Is.EqualTo(0));
            Assert.That(lane0[1].Id, Is.EqualTo(3));
            Assert.That(lane0[2].Id, Is.EqualTo(6));

            // Lane 1: tanks 1, 4
            var lane1 = supply.GetLane(1);
            Assert.That(lane1.Count, Is.EqualTo(2));
            Assert.That(lane1[0].Id, Is.EqualTo(1));
            Assert.That(lane1[1].Id, Is.EqualTo(4));

            // Lane 2: tanks 2, 5
            var lane2 = supply.GetLane(2);
            Assert.That(lane2.Count, Is.EqualTo(2));
            Assert.That(lane2[0].Id, Is.EqualTo(2));
            Assert.That(lane2[1].Id, Is.EqualTo(5));
        }

        [Test]
        public void Supply_TryTakeFront_RemovesFrontAndRaisesLaneChanged()
        {
            var tanks = new List<ColorTankModel>
            {
                new ColorTankModel(0, 10, 20),
                new ColorTankModel(1, 11, 20),
                new ColorTankModel(2, 12, 20),
                new ColorTankModel(3, 13, 20)
            };

            var supply = new SupplyModel(tanks, 2);
            var changedLanes = new List<int>();
            supply.OnLaneChanged += lane => changedLanes.Add(lane);

            // Lane 0: tanks 0, 2
            // Lane 1: tanks 1, 3
            Assert.That(supply.PeekFront(0).Id, Is.EqualTo(0));
            Assert.That(supply.TryTakeFront(0, out var taken), Is.True);
            Assert.That(taken.Id, Is.EqualTo(0));
            Assert.That(supply.TotalRemaining, Is.EqualTo(3));
            Assert.That(changedLanes.Count, Is.EqualTo(1));
            Assert.That(changedLanes[0], Is.EqualTo(0));

            // Now lane 0 front should be tank 2
            Assert.That(supply.PeekFront(0).Id, Is.EqualTo(2));

            // Take from lane 1
            Assert.That(supply.TryTakeFront(1, out taken), Is.True);
            Assert.That(taken.Id, Is.EqualTo(1));
            Assert.That(supply.TotalRemaining, Is.EqualTo(2));
            Assert.That(changedLanes[1], Is.EqualTo(1));

            // Now lane 1 front should be tank 3
            Assert.That(supply.PeekFront(1).Id, Is.EqualTo(3));
        }

        [Test]
        public void Supply_TryTakeFront_EmptyLane_ReturnsFalse()
        {
            var tanks = new List<ColorTankModel>
            {
                new ColorTankModel(0, 10, 20)
            };

            var supply = new SupplyModel(tanks, 2);

            // Lane 0: tank 0
            // Lane 1: empty
            Assert.That(supply.PeekFront(1), Is.Null);
            Assert.That(supply.TryTakeFront(1, out var taken), Is.False);
            Assert.That(taken, Is.Null);

            // Take the only tank
            Assert.That(supply.TryTakeFront(0, out taken), Is.True);
            Assert.That(taken.Id, Is.EqualTo(0));

            // Now lane 0 is also empty
            Assert.That(supply.PeekFront(0), Is.Null);
            Assert.That(supply.TryTakeFront(0, out taken), Is.False);
            Assert.That(supply.TotalRemaining, Is.EqualTo(0));
            Assert.That(supply.IsEmpty, Is.True);
        }

        [Test]
        public void Supply_FindLaneWithFront_ReturnsMinusOneForNonFront()
        {
            var tanks = new List<ColorTankModel>
            {
                new ColorTankModel(0, 10, 20),
                new ColorTankModel(1, 11, 20),
                new ColorTankModel(2, 12, 20)
            };

            var supply = new SupplyModel(tanks, 2);

            // Lane 0: tanks 0, 2
            // Lane 1: tank 1
            Assert.That(supply.FindLaneWithFront(tanks[0]), Is.EqualTo(0));
            Assert.That(supply.FindLaneWithFront(tanks[1]), Is.EqualTo(1));
            Assert.That(supply.FindLaneWithFront(tanks[2]), Is.EqualTo(-1)); // Not at front

            // Take front of lane 0
            supply.TryTakeFront(0, out _);

            // Now tank 2 is at front of lane 0
            Assert.That(supply.FindLaneWithFront(tanks[2]), Is.EqualTo(0));
            Assert.That(supply.FindLaneWithFront(tanks[0]), Is.EqualTo(-1)); // Removed
        }

        #endregion
    }
}
