using NUnit.Framework;
using PixelFlow.Core;
using PixelFlow.Data;
using UnityEngine;

namespace PixelFlow.Tests
{
    /// <summary>
    /// Tests for <see cref="LevelSession.Create"/>. The belt rules of <see cref="GameRules"/> are tested in <see cref="BeltRulesTests"/>.
    /// </summary>
    public sealed class GameRulesTests
    {
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
            Assert.IsNotNull(session.Belt, "Belt should be created");
            Assert.IsNotNull(session.BeltShooting, "Belt shooting should be created");
            Assert.AreEqual(3, session.Belt.Capacity, "Belt capacity should equal slotCount");
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

        /// <summary>
        /// Test that LevelSession.Create rejects null data and null palette/tanks/cells arrays.
        /// </summary>
        [Test]
        public void Create_NullArrays_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => LevelSession.Create(null), "null data");

            var palette = new Color32[] { Color.red };
            var tanks = new ColorTankData[] { new ColorTankData { colorId = 0, ammo = 1 } };

            var noPalette = TestLevels.Create(new[] { "0" }, palette, tanks);
            noPalette.palette = null;
            Assert.Throws<System.ArgumentException>(() => LevelSession.Create(noPalette), "null palette");

            var noTanks = TestLevels.Create(new[] { "0" }, palette, tanks);
            noTanks.tanks = null;
            Assert.Throws<System.ArgumentException>(() => LevelSession.Create(noTanks), "null tanks");

            var noCells = TestLevels.Create(new[] { "0" }, palette, tanks);
            noCells.cells = null;
            Assert.Throws<System.ArgumentException>(() => LevelSession.Create(noCells), "null cells");

            Object.DestroyImmediate(noPalette);
            Object.DestroyImmediate(noTanks);
            Object.DestroyImmediate(noCells);
        }

        /// <summary>
        /// Test that LevelSession.Create throws when a non-empty cell references a colour outside the palette,
        /// while EmptyCell (255) stays valid.
        /// </summary>
        [Test]
        public void Create_CellColorOutsidePalette_Throws()
        {
            var palette = new Color32[] { Color.red, Color.blue };
            var tanks = new ColorTankData[] { new ColorTankData { colorId = 0, ammo = 1 } };
            var level = TestLevels.Create(new[] { "0.", "12" }, palette, tanks);

            var ex = Assert.Throws<System.ArgumentException>(() => LevelSession.Create(level));
            StringAssert.Contains("colorId 2", ex.Message);

            Object.DestroyImmediate(level);
        }
    }
}
