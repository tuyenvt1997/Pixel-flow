using NUnit.Framework;
using PixelFlow.Core;
using PixelFlow.Data;
using System;
using System.Diagnostics;
using UnityEngine;

namespace PixelFlow.Tests
{
    /// <summary>
    /// Tests for PixelGridModel, the core board state for the Pixel Flow game.
    /// </summary>
    [TestFixture]
    public class PixelGridModelTests
    {
        [Test]
        public void Build_CountsPerColorAndTotal()
        {
            // Arrange: 2x2 grid with colors 0, 1, 2
            // rows[0] = top = "01"
            // rows[1] = bottom = "12"
            var level = TestLevels.Create(
                new[] { "01", "12" },
                new Color32[] { Color.red, Color.green, Color.blue },
                new ColorTankData[0]
            );

            // Act
            var model = new PixelGridModel(level.width, level.height, level.cells);

            // Assert
            Assert.AreEqual(2, model.Width);
            Assert.AreEqual(2, model.Height);
            Assert.AreEqual(4, model.RemainingCount);
            Assert.AreEqual(1, model.GetRemaining(0));
            Assert.AreEqual(2, model.GetRemaining(1));
            Assert.AreEqual(1, model.GetRemaining(2));
            Assert.AreEqual(0, model.GetRemaining(3));
        }

        [Test]
        public void GetCellsOfColor_ReturnsAllCoordinatesOfThatColor()
        {
            // Arrange: 2x2 grid
            // rows[0] = top = "01"     → y=1
            // rows[1] = bottom = "11"  → y=0
            var level = TestLevels.Create(
                new[] { "01", "11" },
                new Color32[] { Color.red, Color.green },
                new ColorTankData[0]
            );
            var model = new PixelGridModel(level.width, level.height, level.cells);

            // Act
            var color0Cells = model.GetCellsOfColor(0);
            var color1Cells = model.GetCellsOfColor(1);
            var color2Cells = model.GetCellsOfColor(2);

            // Assert
            Assert.AreEqual(1, color0Cells.Count);
            Assert.IsTrue(Contains(color0Cells, new Vector2Int(0, 1)));

            Assert.AreEqual(3, color1Cells.Count);
            Assert.IsTrue(Contains(color1Cells, new Vector2Int(1, 1)));
            Assert.IsTrue(Contains(color1Cells, new Vector2Int(0, 0)));
            Assert.IsTrue(Contains(color1Cells, new Vector2Int(1, 0)));

            Assert.AreEqual(0, color2Cells.Count);
        }

        [Test]
        public void TryGetExposedCell_OnlyBottomMostCellPerColumnIsExposed()
        {
            // Arrange: 1x2 grid
            // rows[0] = top = "0"     → y=1
            // rows[1] = bottom = "1"  → y=0 (exposed)
            var level = TestLevels.Create(
                new[] { "0", "1" },
                new Color32[] { Color.red, Color.green },
                new ColorTankData[0]
            );
            var model = new PixelGridModel(level.width, level.height, level.cells);

            // Act & Assert
            Assert.IsTrue(model.HasExposed(1));
            Assert.IsTrue(model.TryGetExposedCell(1, out var cell1));
            Assert.AreEqual(new Vector2Int(0, 0), cell1);

            Assert.IsFalse(model.HasExposed(0));
            Assert.IsFalse(model.TryGetExposedCell(0, out var cell0));
            Assert.AreEqual(Vector2Int.zero, cell0);
        }

        [Test]
        public void RemoveCell_ExposesNextCellInColumn()
        {
            // Arrange: 1x2 grid
            // rows[0] = top = "0"     → y=1
            // rows[1] = bottom = "1"  → y=0 (exposed)
            var level = TestLevels.Create(
                new[] { "0", "1" },
                new Color32[] { Color.red, Color.green },
                new ColorTankData[0]
            );
            var model = new PixelGridModel(level.width, level.height, level.cells);

            // Act: Remove the exposed color 1 cell
            model.RemoveCell(new Vector2Int(0, 0));

            // Assert: Color 0 at (0,1) should now be exposed
            Assert.IsFalse(model.HasExposed(1));
            Assert.IsTrue(model.HasExposed(0));
            Assert.IsTrue(model.TryGetExposedCell(0, out var cell0));
            Assert.AreEqual(new Vector2Int(0, 1), cell0);
            Assert.AreEqual(1, model.RemainingCount);
        }

        [Test]
        public void RemoveCell_SkipsGapsInColumn()
        {
            // Arrange: 1x3 grid with gap
            // rows[0] = top = "0"     → y=2
            // rows[1] = middle = "."  → y=1 (empty)
            // rows[2] = bottom = "1"  → y=0 (exposed)
            var level = TestLevels.Create(
                new[] { "0", ".", "1" },
                new Color32[] { Color.red, Color.green },
                new ColorTankData[0]
            );
            var model = new PixelGridModel(level.width, level.height, level.cells);

            // Act: Remove the exposed color 1 cell at (0,0)
            model.RemoveCell(new Vector2Int(0, 0));

            // Assert: Should skip the gap and expose color 0 at (0,2)
            Assert.IsTrue(model.HasExposed(0));
            Assert.IsTrue(model.TryGetExposedCell(0, out var cell0));
            Assert.AreEqual(new Vector2Int(0, 2), cell0);
            Assert.AreEqual(1, model.RemainingCount);
        }

        [Test]
        public void EmptyColumn_IsNeverExposed()
        {
            // Arrange: 2x2 grid with one empty column
            // rows[0] = top = ".1"     → y=1
            // rows[1] = bottom = ".1"  → y=0
            var level = TestLevels.Create(
                new[] { ".1", ".1" },
                new Color32[] { Color.red, Color.green },
                new ColorTankData[0]
            );
            var model = new PixelGridModel(level.width, level.height, level.cells);

            // Assert: Color 1 should be exposed at column 1
            Assert.IsTrue(model.HasExposed(1));
            Assert.IsTrue(model.TryGetExposedCell(1, out var cell1));
            Assert.AreEqual(new Vector2Int(1, 0), cell1);

            // Column 0 is all empty, so no color should be exposed there
            Assert.IsFalse(model.HasExposed(0));
        }

        [Test]
        public void RemoveCell_NotAFrontFromAnySide_Throws()
        {
            // Arrange: 3x3 grid with center cell surrounded on all sides
            // rows[0] = top = "000"    → y=2
            // rows[1] = middle = "010" → y=1 (center cell is not a front from any side)
            // rows[2] = bottom = "000" → y=0
            var level = TestLevels.Create(
                new[] { "000", "010", "000" },
                new Color32[] { Color.red, Color.green },
                new ColorTankData[0]
            );
            var model = new PixelGridModel(level.width, level.height, level.cells);

            // Act & Assert: Trying to remove a cell that's not a front from any side should throw
            Assert.Throws<InvalidOperationException>(() => model.RemoveCell(new Vector2Int(1, 1)));
        }

        [Test]
        public void RemoveCell_RaisesOnCellRemoved_AndOnClearedWhenEmpty()
        {
            // Arrange: 1x1 grid with one cell
            var level = TestLevels.Create(
                new[] { "0" },
                new Color32[] { Color.red },
                new ColorTankData[0]
            );
            var model = new PixelGridModel(level.width, level.height, level.cells);

            Vector2Int removedCell = default;
            int removedColorId = -1;
            bool clearedRaised = false;

            model.OnCellRemoved += (cell, colorId) =>
            {
                removedCell = cell;
                removedColorId = colorId;
            };

            model.OnCleared += () =>
            {
                clearedRaised = true;
            };

            // Act
            model.RemoveCell(new Vector2Int(0, 0));

            // Assert
            Assert.AreEqual(new Vector2Int(0, 0), removedCell);
            Assert.AreEqual(0, removedColorId);
            Assert.IsTrue(clearedRaised);
            Assert.AreEqual(0, model.RemainingCount);
        }

        [Test]
        public void TryGetExposedCell_DoesNotAllocate()
        {
            // Arrange: Create a 64×64 grid with random colors
            var random = new System.Random(42);
            var rows = new string[64];
            for (int i = 0; i < 64; i++)
            {
                var row = new char[64];
                for (int j = 0; j < 64; j++)
                {
                    row[j] = (char)('0' + random.Next(0, 8));
                }
                rows[i] = new string(row);
            }

            var level = TestLevels.Create(
                rows,
                new Color32[] { Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.magenta, Color.white, Color.black },
                new ColorTankData[0]
            );
            var model = new PixelGridModel(level.width, level.height, level.cells);

            // Warm-up: Call a few times to ensure any lazy initialization is done
            for (int i = 0; i < 10; i++)
            {
                model.TryGetExposedCell(i % 8, out _);
            }

            // Act & Assert: 10,000 calls should not allocate
            AllocAssert.NoAlloc(() =>
            {
                for (int i = 0; i < 10000; i++)
                {
                    model.TryGetExposedCell(i % 8, out _);
                }
            });
        }

        [Test]
        public void Build_5000Cells_Under20ms()
        {
            // Arrange: Create a 100×50 grid with random colors (5000 cells)
            var random = new System.Random(42);
            var rows = new string[50];
            for (int i = 0; i < 50; i++)
            {
                var row = new char[100];
                for (int j = 0; j < 100; j++)
                {
                    row[j] = (char)('0' + random.Next(0, 8));
                }
                rows[i] = new string(row);
            }

            var level = TestLevels.Create(
                rows,
                new Color32[] { Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.magenta, Color.white, Color.black },
                new ColorTankData[0]
            );

            // Act: Measure construction time
            var sw = Stopwatch.StartNew();
            var model = new PixelGridModel(level.width, level.height, level.cells);
            sw.Stop();

            // Assert
            Assert.AreEqual(100, model.Width);
            Assert.AreEqual(50, model.Height);
            Assert.AreEqual(5000, model.RemainingCount);
            Assert.Less(sw.ElapsedMilliseconds, 20, $"Build took {sw.ElapsedMilliseconds}ms, expected < 20ms");
        }

        [Test]
        public void Constructor_InvalidCellsLength_Throws()
        {
            // Arrange
            var cells = new byte[10]; // width * height = 2 * 2 = 4, but we provide 10

            // Act & Assert
            Assert.Throws<ArgumentException>(() => new PixelGridModel(2, 2, cells));
        }

        [Test]
        public void GetCell_ReturnsCorrectValue()
        {
            // Arrange: 2x2 grid
            // rows[0] = top = "01"     → y=1
            // rows[1] = bottom = "23"  → y=0
            var level = TestLevels.Create(
                new[] { "01", "23" },
                new Color32[] { Color.red, Color.green, Color.blue, Color.yellow },
                new ColorTankData[0]
            );
            var model = new PixelGridModel(level.width, level.height, level.cells);

            // Act & Assert
            Assert.AreEqual(2, model.GetCell(0, 0));
            Assert.AreEqual(3, model.GetCell(1, 0));
            Assert.AreEqual(0, model.GetCell(0, 1));
            Assert.AreEqual(1, model.GetCell(1, 1));
        }

        [Test]
        public void RemoveCell_UpdatesCellToEmpty()
        {
            // Arrange: 1x1 grid
            var level = TestLevels.Create(
                new[] { "0" },
                new Color32[] { Color.red },
                new ColorTankData[0]
            );
            var model = new PixelGridModel(level.width, level.height, level.cells);

            // Act
            model.RemoveCell(new Vector2Int(0, 0));

            // Assert
            Assert.AreEqual(LevelData.EmptyCell, model.GetCell(0, 0));
        }

        [Test]
        public void RemoveCell_WithMultipleExposedCells_ReturnsDeterministic()
        {
            // Arrange: 3x1 grid, all exposed
            // rows[0] = "012"  → y=0 (all exposed)
            var level = TestLevels.Create(
                new[] { "012" },
                new Color32[] { Color.red, Color.green, Color.blue },
                new ColorTankData[0]
            );
            var model = new PixelGridModel(level.width, level.height, level.cells);

            // Act: Get exposed cell for each color multiple times
            Assert.IsTrue(model.TryGetExposedCell(0, out var cell0a));
            Assert.IsTrue(model.TryGetExposedCell(0, out var cell0b));
            Assert.IsTrue(model.TryGetExposedCell(1, out var cell1a));
            Assert.IsTrue(model.TryGetExposedCell(1, out var cell1b));

            // Assert: Should return the same cell each time (deterministic)
            Assert.AreEqual(cell0a, cell0b);
            Assert.AreEqual(cell1a, cell1b);
        }

        private bool Contains(System.Collections.Generic.IReadOnlyList<Vector2Int> list, Vector2Int item)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == item)
                    return true;
            }
            return false;
        }
    }
}
