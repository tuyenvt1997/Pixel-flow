using NUnit.Framework;
using PixelFlow.Core;
using PixelFlow.Data;
using System.Collections.Generic;
using UnityEngine;

namespace PixelFlow.Tests
{
    /// <summary>
    /// Tests for ShootingLogic class.
    /// </summary>
    public sealed class ShootingLogicTests
    {
        /// <summary>
        /// Test that a single tank fires at an exposed cell of its color.
        /// </summary>
        [Test]
        public void Step_TankFiresAtExposedCellOfItsColor()
        {
            // Arrange
            var palette = new Color32[] { Color.red, Color.blue };
            var tanks = new ColorTankData[] { new ColorTankData { colorId = 0, ammo = 5 } };
            var level = TestLevels.Create(new[] { ".", "0" }, palette, tanks, lanes: 1, slots: 5);
            var session = LevelSession.Create(level);

            // Add tank to tray
            session.Tray.TryAdd(session.Supply.PeekFront(0));
            session.Supply.TryTakeFront(0, out _);

            var tank = session.Tray[0];
            var initialAmmo = tank.Ammo;
            var output = new List<ShotEvent>();

            // Act
            int shotCount = session.Shooting.Step(output);

            // Assert
            Assert.AreEqual(1, shotCount, "Should fire 1 shot");
            Assert.AreEqual(1, output.Count, "Output should contain 1 shot event");
            Assert.AreEqual(tank, output[0].Tank, "Shot should be from the tank");
            Assert.AreEqual(0, output[0].SlotIndex, "Shot should be from slot 0");
            Assert.AreEqual(new Vector2Int(0, 0), output[0].Cell, "Shot should hit cell (0,0)");
            Assert.AreEqual(0, output[0].ColorId, "Shot should be color 0");
            Assert.AreEqual(initialAmmo - 1, tank.Ammo, "Tank ammo should decrease by 1");
            Assert.AreEqual(LevelData.EmptyCell, session.Grid.GetCell(0, 0), "Cell should be removed");
        }

        /// <summary>
        /// Test that a tank without an exposed target does not fire.
        /// </summary>
        [Test]
        public void Step_TankWithoutExposedTarget_DoesNotFire()
        {
            // Arrange - Grid has only color 1 exposed, tank is color 0
            var palette = new Color32[] { Color.red, Color.blue };
            var tanks = new ColorTankData[] { new ColorTankData { colorId = 0, ammo = 5 } };
            var level = TestLevels.Create(new[] { "1" }, palette, tanks, lanes: 1, slots: 5);
            var session = LevelSession.Create(level);

            // Add tank to tray
            session.Tray.TryAdd(session.Supply.PeekFront(0));
            session.Supply.TryTakeFront(0, out _);

            var tank = session.Tray[0];
            var initialAmmo = tank.Ammo;
            var output = new List<ShotEvent>();

            // Act
            int shotCount = session.Shooting.Step(output);

            // Assert
            Assert.AreEqual(0, shotCount, "Should fire 0 shots");
            Assert.AreEqual(0, output.Count, "Output should be empty");
            Assert.AreEqual(initialAmmo, tank.Ammo, "Tank ammo should remain unchanged");
        }

        /// <summary>
        /// Test that each tank in the tray fires at most once per step.
        /// </summary>
        [Test]
        public void Step_EachTrayTankFiresAtMostOncePerStep()
        {
            // Arrange
            var palette = new Color32[] { Color.red };
            var tanks = new ColorTankData[]
            {
                new ColorTankData { colorId = 0, ammo = 5 },
                new ColorTankData { colorId = 0, ammo = 5 }
            };
            var level = TestLevels.Create(new[] { "0.0" }, palette, tanks, lanes: 1, slots: 5);
            var session = LevelSession.Create(level);

            // Add both tanks to tray
            session.Tray.TryAdd(session.Supply.PeekFront(0));
            session.Supply.TryTakeFront(0, out _);
            session.Tray.TryAdd(session.Supply.PeekFront(0));
            session.Supply.TryTakeFront(0, out _);

            var output = new List<ShotEvent>();

            // Act
            int shotCount = session.Shooting.Step(output);

            // Assert
            Assert.AreEqual(2, shotCount, "Should fire 2 shots (one per tank)");
            Assert.AreEqual(2, output.Count, "Output should contain 2 shot events");
        }

        /// <summary>
        /// Test that when a tank depletes mid-step, other tanks still fire once each.
        /// </summary>
        [Test]
        public void Step_TankDepletesMidStep_OthersStillFireOnceEach()
        {
            // Arrange
            var palette = new Color32[] { Color.red, Color.blue };
            var tanks = new ColorTankData[]
            {
                new ColorTankData { colorId = 0, ammo = 1 },
                new ColorTankData { colorId = 1, ammo = 5 },
                new ColorTankData { colorId = 0, ammo = 5 }
            };
            var level = TestLevels.Create(new[] { "01", "00" }, palette, tanks, lanes: 1, slots: 5);
            var session = LevelSession.Create(level);

            // Add all three tanks to tray
            for (int i = 0; i < 3; i++)
            {
                session.Tray.TryAdd(session.Supply.PeekFront(0));
                session.Supply.TryTakeFront(0, out _);
            }

            var tank1 = session.Tray[1];
            var tank2 = session.Tray[2];
            var output = new List<ShotEvent>();

            // Act
            int shotCount = session.Shooting.Step(output);

            // Assert
            Assert.AreEqual(3, shotCount, "Should fire 3 shots");
            Assert.AreEqual(3, output.Count, "Output should contain 3 shot events");
            Assert.AreEqual(0, output[0].SlotIndex, "First shot from slot 0");
            Assert.AreEqual(1, output[1].SlotIndex, "Second shot from slot 1");
            Assert.AreEqual(2, output[2].SlotIndex, "Third shot from slot 2");
            Assert.AreEqual(2, session.Tray.Count, "Tray should have 2 tanks after depletion");
            Assert.AreEqual(tank1, session.Tray[0], "Tank 1 should be at slot 0 after shift");
            Assert.AreEqual(tank2, session.Tray[1], "Tank 2 should be at slot 1 after shift");
        }

        /// <summary>
        /// Test that repeated steps clear a column from bottom to top.
        /// </summary>
        [Test]
        public void Step_RepeatedUntilDone_ClearsColumnBottomUp()
        {
            // Arrange
            var palette = new Color32[] { Color.red };
            var tanks = new ColorTankData[] { new ColorTankData { colorId = 0, ammo = 3 } };
            var level = TestLevels.Create(new[] { "0", "0", "0" }, palette, tanks, lanes: 1, slots: 5);
            var session = LevelSession.Create(level);

            // Add tank to tray
            session.Tray.TryAdd(session.Supply.PeekFront(0));
            session.Supply.TryTakeFront(0, out _);

            var output = new List<ShotEvent>();

            // Act & Assert - Step 1
            output.Clear();
            session.Shooting.Step(output);
            Assert.AreEqual(new Vector2Int(0, 0), output[0].Cell, "First shot should hit y=0");

            // Act & Assert - Step 2
            output.Clear();
            session.Shooting.Step(output);
            Assert.AreEqual(new Vector2Int(0, 1), output[0].Cell, "Second shot should hit y=1");

            // Act & Assert - Step 3
            output.Clear();
            session.Shooting.Step(output);
            Assert.AreEqual(new Vector2Int(0, 2), output[0].Cell, "Third shot should hit y=2");

            // Tank should be depleted and removed from tray
            Assert.AreEqual(0, session.Tray.Count, "Tank should be removed after depletion");
        }

        /// <summary>
        /// Test that Step does not allocate memory after warm-up.
        /// </summary>
        [Test]
        public void Step_DoesNotAllocate()
        {
            // Arrange - Create large grid with random cells
            var palette = new Color32[10];
            for (int i = 0; i < 10; i++)
                palette[i] = new Color32((byte)(i * 25), (byte)(i * 25), (byte)(i * 25), 255);

            var tanks = new ColorTankData[5];
            for (int i = 0; i < 5; i++)
                tanks[i] = new ColorTankData { colorId = (byte)(i % 10), ammo = 1000 };

            var rows = new string[64];
            for (int y = 0; y < 64; y++)
            {
                var row = "";
                for (int x = 0; x < 64; x++)
                    row += (char)('0' + ((x + y) % 10));
                rows[y] = row;
            }

            var level = TestLevels.Create(rows, palette, tanks, lanes: 1, slots: 5);
            var session = LevelSession.Create(level);

            // Add all tanks to tray
            for (int i = 0; i < 5; i++)
            {
                session.Tray.TryAdd(session.Supply.PeekFront(0));
                session.Supply.TryTakeFront(0, out _);
            }

            var output = new List<ShotEvent>(16);

            // Warm-up
            for (int i = 0; i < 100; i++)
            {
                output.Clear();
                session.Shooting.Step(output);
            }

            // Act & Assert
            AllocAssert.NoAlloc(() =>
            {
                output.Clear();
                session.Shooting.Step(output);
            });
        }

        /// <summary>
        /// Test CanAnyTankFire returns true when at least one tank can fire.
        /// </summary>
        [Test]
        public void CanAnyTankFire_ReturnsTrueWhenTankCanFire()
        {
            // Arrange
            var palette = new Color32[] { Color.red };
            var tanks = new ColorTankData[] { new ColorTankData { colorId = 0, ammo = 5 } };
            var level = TestLevels.Create(new[] { "0" }, palette, tanks, lanes: 1, slots: 5);
            var session = LevelSession.Create(level);

            session.Tray.TryAdd(session.Supply.PeekFront(0));
            session.Supply.TryTakeFront(0, out _);

            // Act
            bool canFire = session.Shooting.CanAnyTankFire();

            // Assert
            Assert.IsTrue(canFire, "Should return true when tank can fire");
        }

        /// <summary>
        /// Test CanAnyTankFire returns false when no tank can fire.
        /// </summary>
        [Test]
        public void CanAnyTankFire_ReturnsFalseWhenNoTankCanFire()
        {
            // Arrange
            var palette = new Color32[] { Color.red, Color.blue };
            var tanks = new ColorTankData[] { new ColorTankData { colorId = 0, ammo = 5 } };
            var level = TestLevels.Create(new[] { "1" }, palette, tanks, lanes: 1, slots: 5);
            var session = LevelSession.Create(level);

            session.Tray.TryAdd(session.Supply.PeekFront(0));
            session.Supply.TryTakeFront(0, out _);

            // Act
            bool canFire = session.Shooting.CanAnyTankFire();

            // Assert
            Assert.IsFalse(canFire, "Should return false when no tank can fire");
        }
    }
}
