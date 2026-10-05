using NUnit.Framework;
using PixelFlow.Core;
using PixelFlow.Data;
using UnityEngine;

namespace PixelFlow.Tests
{
    /// <summary>
    /// Tests for GameRules class.
    /// </summary>
    public sealed class GameRulesTests
    {
        /// <summary>
        /// Test that the game is won when no pixels are left.
        /// </summary>
        [Test]
        public void Evaluate_NoPixelsLeft_IsWon()
        {
            // Arrange
            var palette = new Color32[] { Color.red };
            var tanks = new ColorTankData[] { new ColorTankData { colorId = 0, ammo = 1 } };
            var level = TestLevels.Create(new[] { "0" }, palette, tanks, lanes: 1, slots: 5);
            var session = LevelSession.Create(level);

            // Add tank to tray
            session.Tray.TryAdd(session.Supply.PeekFront(0));
            session.Supply.TryTakeFront(0, out _);

            // Fire to clear the grid
            session.Shooting.Step(new System.Collections.Generic.List<ShotEvent>());

            // Act
            var state = GameRules.Evaluate(session.Grid, session.Tray, session.Supply, session.Shooting);

            // Assert
            Assert.AreEqual(GameState.Won, state, "Should be Won when no pixels remain");
        }

        /// <summary>
        /// Test that the game is lost when tray is full and no tank can fire.
        /// </summary>
        [Test]
        public void Evaluate_TrayFullAndNoneCanFire_IsLost()
        {
            // Arrange
            var palette = new Color32[] { Color.red, Color.blue };
            var tanks = new ColorTankData[5];
            for (int i = 0; i < 5; i++)
                tanks[i] = new ColorTankData { colorId = 0, ammo = 5 };

            var level = TestLevels.Create(new[] { "1" }, palette, tanks, lanes: 1, slots: 5);
            var session = LevelSession.Create(level);

            // Fill tray with tanks that can't fire
            for (int i = 0; i < 5; i++)
            {
                session.Tray.TryAdd(session.Supply.PeekFront(0));
                session.Supply.TryTakeFront(0, out _);
            }

            // Act
            var state = GameRules.Evaluate(session.Grid, session.Tray, session.Supply, session.Shooting);

            // Assert
            Assert.AreEqual(GameState.Lost, state, "Should be Lost when tray is full and no tank can fire");
        }

        /// <summary>
        /// Test that the game is lost when supply is empty and no tank can fire.
        /// </summary>
        [Test]
        public void Evaluate_SupplyEmptyAndNoneCanFire_IsLost()
        {
            // Arrange
            var palette = new Color32[] { Color.red, Color.blue };
            var tanks = new ColorTankData[] { new ColorTankData { colorId = 0, ammo = 5 } };
            var level = TestLevels.Create(new[] { "1" }, palette, tanks, lanes: 1, slots: 5);
            var session = LevelSession.Create(level);

            // Add tank to tray (supply becomes empty)
            session.Tray.TryAdd(session.Supply.PeekFront(0));
            session.Supply.TryTakeFront(0, out _);

            // Act
            var state = GameRules.Evaluate(session.Grid, session.Tray, session.Supply, session.Shooting);

            // Assert
            Assert.AreEqual(GameState.Lost, state, "Should be Lost when supply is empty and no tank can fire");
        }

        /// <summary>
        /// Test that the game is playing when tray is not full and supply has tanks.
        /// </summary>
        [Test]
        public void Evaluate_TrayNotFullAndSupplyHasTanks_IsPlaying()
        {
            // Arrange
            var palette = new Color32[] { Color.red, Color.blue };
            var tanks = new ColorTankData[] { new ColorTankData { colorId = 0, ammo = 5 }, new ColorTankData { colorId = 0, ammo = 5 } };
            var level = TestLevels.Create(new[] { "1" }, palette, tanks, lanes: 1, slots: 5);
            var session = LevelSession.Create(level);

            // Add one tank to tray (tray not full, supply not empty)
            session.Tray.TryAdd(session.Supply.PeekFront(0));
            session.Supply.TryTakeFront(0, out _);

            // Act
            var state = GameRules.Evaluate(session.Grid, session.Tray, session.Supply, session.Shooting);

            // Assert
            Assert.AreEqual(GameState.Playing, state, "Should be Playing when tray is not full and supply has tanks");
        }

        /// <summary>
        /// Test that a tank with no pixels of its color eventually leads to a loss.
        /// </summary>
        [Test]
        public void Evaluate_TankWithNoPixelsOfItsColor_EventuallyLost()
        {
            // Arrange - Grid has only color 0 (1 cell), tanks are color 2 and color 0
            var palette = new Color32[] { Color.red, Color.green, Color.blue };
            var tanks = new ColorTankData[]
            {
                new ColorTankData { colorId = 2, ammo = 5 },
                new ColorTankData { colorId = 0, ammo = 1 }
            };
            var level = TestLevels.Create(new[] { "0" }, palette, tanks, lanes: 1, slots: 1);
            var session = LevelSession.Create(level);

            // Add first tank (color 2) to tray - it can't fire at color 0 pixel
            session.Tray.TryAdd(session.Supply.PeekFront(0));
            session.Supply.TryTakeFront(0, out _);

            // Act
            var state = GameRules.Evaluate(session.Grid, session.Tray, session.Supply, session.Shooting);

            // Assert
            Assert.AreEqual(GameState.Lost, state, "Should be Lost when tray is full and no tank can fire");
        }

        /// <summary>
        /// Test that LevelSession.Create builds models correctly from LevelData.
        /// </summary>
        [Test]
        public void Create_BuildsModelsFromLevelData()
        {
            // Arrange
            var palette = new Color32[] { Color.red, Color.blue };
            var tanks = new ColorTankData[]
            {
                new ColorTankData { colorId = 0, ammo = 10 },
                new ColorTankData { colorId = 1, ammo = 15 },
                new ColorTankData { colorId = 0, ammo = 20 }
            };
            var level = TestLevels.Create(new[] { "01", "00" }, palette, tanks, lanes: 2, slots: 3);

            // Act
            var session = LevelSession.Create(level);

            // Assert
            Assert.IsNotNull(session.Grid, "Grid should be created");
            Assert.IsNotNull(session.Tray, "Tray should be created");
            Assert.IsNotNull(session.Supply, "Supply should be created");
            Assert.IsNotNull(session.Shooting, "Shooting should be created");
            Assert.AreEqual(palette, session.Palette, "Palette should match");
            Assert.AreEqual(2, session.Width, "Width should match");
            Assert.AreEqual(2, session.Height, "Height should match");

            // Check tray capacity
            Assert.AreEqual(3, session.Tray.Capacity, "Tray should have correct capacity");

            // Check supply lanes
            Assert.AreEqual(2, session.Supply.LaneCount, "Supply should have correct lane count");

            // Check tank IDs (should be 0, 1, 2)
            Assert.AreEqual(0, session.Supply.PeekFront(0).Id, "Tank 0 should have ID 0");
            Assert.AreEqual(1, session.Supply.PeekFront(1).Id, "Tank 1 should have ID 1");
            Assert.AreEqual(2, session.Supply.GetLane(0)[1].Id, "Tank 2 should have ID 2");

            // Check grid cells
            Assert.AreEqual(0, session.Grid.GetCell(0, 0), "Cell (0,0) should be color 0");
            Assert.AreEqual(0, session.Grid.GetCell(1, 0), "Cell (1,0) should be color 0");
            Assert.AreEqual(0, session.Grid.GetCell(0, 1), "Cell (0,1) should be color 0");
            Assert.AreEqual(1, session.Grid.GetCell(1, 1), "Cell (1,1) should be color 1");
        }

        /// <summary>
        /// Test that LevelSession.Create throws when tank color is outside palette range.
        /// </summary>
        [Test]
        public void Create_TankColorOutsidePalette_Throws()
        {
            // Arrange
            var palette = new Color32[] { Color.red, Color.blue };
            var tanks = new ColorTankData[]
            {
                new ColorTankData { colorId = 0, ammo = 10 },
                new ColorTankData { colorId = 5, ammo = 15 } // Invalid color ID
            };
            var level = TestLevels.Create(new[] { "0" }, palette, tanks, lanes: 1, slots: 5);

            // Act & Assert
            Assert.Throws<System.ArgumentException>(() => LevelSession.Create(level), "Should throw when tank color is outside palette");
        }
    }
}
