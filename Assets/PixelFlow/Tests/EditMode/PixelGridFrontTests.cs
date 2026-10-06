using NUnit.Framework;
using PixelFlow.Core;
using PixelFlow.Data;
using System;
using UnityEngine;

namespace PixelFlow.Tests
{
    /// <summary>
    /// Tests for four-sided front lookup in PixelGridModel.
    /// </summary>
    [TestFixture]
    public class PixelGridFrontTests
    {
        [Test]
        public void TryGetFront_AllSides_FindFirstNonEmptyCell()
        {
            // Arrange: 3x3 grid with cells at specific positions
            // rows[0] = top = ".1."     → y=2
            // rows[1] = middle = "2.3"  → y=1
            // rows[2] = bottom = ".0."  → y=0
            var level = TestLevels.Create(
                new[] { ".1.", "2.3", ".0." },
                new Color32[] { Color.red, Color.green, Color.blue, Color.yellow },
                new ColorTankData[0]
            );
            var model = new PixelGridModel(level.width, level.height, level.cells);

            // Act & Assert - Bottom (fires upward)
            Assert.IsTrue(model.TryGetFront(BoardSide.Bottom, 0, out var bottomCol0));
            Assert.AreEqual(new Vector2Int(0, 1), bottomCol0); // cell '2' at (0,1)

            Assert.IsTrue(model.TryGetFront(BoardSide.Bottom, 1, out var bottomCol1));
            Assert.AreEqual(new Vector2Int(1, 0), bottomCol1); // cell '0' at (1,0)

            // Act & Assert - Top (fires downward)
            Assert.IsTrue(model.TryGetFront(BoardSide.Top, 1, out var topCol1));
            Assert.AreEqual(new Vector2Int(1, 2), topCol1); // cell '1' at (1,2)

            Assert.IsTrue(model.TryGetFront(BoardSide.Top, 2, out var topCol2));
            Assert.AreEqual(new Vector2Int(2, 1), topCol2); // cell '3' at (2,1)

            // Act & Assert - Left (fires rightward)
            Assert.IsTrue(model.TryGetFront(BoardSide.Left, 1, out var leftRow1));
            Assert.AreEqual(new Vector2Int(0, 1), leftRow1); // cell '2' at (0,1)

            Assert.IsTrue(model.TryGetFront(BoardSide.Right, 1, out var rightRow1));
            Assert.AreEqual(new Vector2Int(2, 1), rightRow1); // cell '3' at (2,1)

            Assert.IsTrue(model.TryGetFront(BoardSide.Left, 0, out var leftRow0));
            Assert.AreEqual(new Vector2Int(1, 0), leftRow0); // cell '0' at (1,0)
        }

        [Test]
        public void TryGetFront_EmptyLine_ReturnsFalse()
        {
            // Arrange: 3x3 grid with empty column 0
            // rows[0] = top = ".12"
            // rows[1] = middle = ".34"
            // rows[2] = bottom = ".56"
            var level = TestLevels.Create(
                new[] { ".12", ".34", ".56" },
                new Color32[] { Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.magenta, Color.white },
                new ColorTankData[0]
            );
            var model = new PixelGridModel(level.width, level.height, level.cells);

            // Act & Assert
            Assert.IsFalse(model.TryGetFront(BoardSide.Bottom, 0, out var bottom));
            Assert.AreEqual(Vector2Int.zero, bottom);

            Assert.IsFalse(model.TryGetFront(BoardSide.Top, 0, out var top));
            Assert.AreEqual(Vector2Int.zero, top);
        }

        [Test]
        public void RemoveCell_CornerFront_UpdatesBothSides()
        {
            // Arrange: 2x2 grid with all cells same color
            // rows[0] = top = "00"    → y=1
            // rows[1] = bottom = "00" → y=0
            var level = TestLevels.Create(
                new[] { "00", "00" },
                new Color32[] { Color.red },
                new ColorTankData[0]
            );
            var model = new PixelGridModel(level.width, level.height, level.cells);

            // Act: Remove bottom-left corner (0,0)
            model.RemoveCell(new Vector2Int(0, 0));

            // Assert: Bottom col 0 should now point to (0,1)
            Assert.IsTrue(model.TryGetFront(BoardSide.Bottom, 0, out var bottomCol0));
            Assert.AreEqual(new Vector2Int(0, 1), bottomCol0);

            // Assert: Left row 0 should now point to (1,0)
            Assert.IsTrue(model.TryGetFront(BoardSide.Left, 0, out var leftRow0));
            Assert.AreEqual(new Vector2Int(1, 0), leftRow0);
        }

        [Test]
        public void RemoveCell_SkipsGaps_FromRight()
        {
            // Arrange: 1x3 grid with gap in middle
            // rows[0] = "0.1" → y=0
            var level = TestLevels.Create(
                new[] { "0.1" },
                new Color32[] { Color.red, Color.green },
                new ColorTankData[0]
            );
            var model = new PixelGridModel(level.width, level.height, level.cells);

            // Act: Remove rightmost cell (2,0)
            model.RemoveCell(new Vector2Int(2, 0));

            // Assert: Right row 0 should now point to (0,0), skipping the gap
            Assert.IsTrue(model.TryGetFront(BoardSide.Right, 0, out var rightRow0));
            Assert.AreEqual(new Vector2Int(0, 0), rightRow0);
        }

        [Test]
        public void HasAnyFront_TracksColours()
        {
            // Arrange: 3x3 grid with color 1 buried in center, surrounded by color 0
            // rows[0] = top = "000"    → y=2
            // rows[1] = middle = "010" → y=1
            // rows[2] = bottom = "000" → y=0
            var level = TestLevels.Create(
                new[] { "000", "010", "000" },
                new Color32[] { Color.red, Color.green },
                new ColorTankData[0]
            );
            var model = new PixelGridModel(level.width, level.height, level.cells);

            // Assert: Color 1 is buried, not a front
            Assert.IsFalse(model.HasAnyFront(1));
            Assert.IsTrue(model.HasAnyFront(0));

            // Act: Remove the cells above color 1
            model.RemoveCell(new Vector2Int(1, 2)); // Top of column 1

            // Assert: Color 1 is now exposed from top
            Assert.IsTrue(model.HasAnyFront(1));
        }

        [Test]
        public void RemoveCell_NotAFront_Throws()
        {
            // Arrange: 3x3 grid fully filled
            // rows[0] = top = "000"
            // rows[1] = middle = "010"
            // rows[2] = bottom = "000"
            var level = TestLevels.Create(
                new[] { "000", "010", "000" },
                new Color32[] { Color.red, Color.green },
                new ColorTankData[0]
            );
            var model = new PixelGridModel(level.width, level.height, level.cells);

            // Act & Assert: Removing center cell (1,1) should throw
            var ex = Assert.Throws<InvalidOperationException>(() => model.RemoveCell(new Vector2Int(1, 1)));
            Assert.That(ex.Message, Does.Contain("not").And.Contains("front"));
        }

        [Test]
        public void TryGetFront_And_HasAnyFront_DoNotAllocate()
        {
            // Arrange: 64x64 random grid
            var random = new System.Random(42);
            var rows = new string[64];
            for (int i = 0; i < 64; i++)
            {
                var row = new char[64];
                for (int j = 0; j < 64; j++)
                {
                    row[j] = (char)('0' + random.Next(10)); // colors 0-9
                }
                rows[i] = new string(row);
            }

            var palette = new Color32[10];
            for (int i = 0; i < 10; i++)
            {
                palette[i] = new Color32((byte)(i * 25), (byte)(i * 25), (byte)(i * 25), 255);
            }

            var level = TestLevels.Create(rows, palette, new ColorTankData[0]);
            var model = new PixelGridModel(level.width, level.height, level.cells);

            // Act & Assert: No allocations in hot paths
            AllocAssert.NoAlloc(() =>
            {
                model.TryGetFront(BoardSide.Bottom, 0, out _);
                model.TryGetFront(BoardSide.Top, 0, out _);
                model.TryGetFront(BoardSide.Left, 0, out _);
                model.TryGetFront(BoardSide.Right, 0, out _);
                model.HasAnyFront(0);
                model.HasAnyFront(5);
            });
        }
    }
}
