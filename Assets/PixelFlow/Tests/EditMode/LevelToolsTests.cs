using System;
using System.Diagnostics;
using NUnit.Framework;
using PixelFlow.Core;
using PixelFlow.Data;
using PixelFlow.EditorTools;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace PixelFlow.Tests
{
    /// <summary>
    /// Tests for the level tools: TankGenerator, TextureQuantizer and SampleLevelFactory.
    /// </summary>
    public sealed class LevelToolsTests
    {
        private static byte[] RandomCells(int width, int height, int colors, int seed)
        {
            var rng = new Random(seed);
            var cells = new byte[width * height];
            for (int i = 0; i < cells.Length; i++)
            {
                // Roughly 10% empty cells, the rest spread over the colors.
                cells[i] = rng.Next(10) == 0 ? LevelData.EmptyCell : (byte)rng.Next(colors);
            }
            return cells;
        }

        /// <summary>
        /// Total ammo generated for each color equals the number of cells of that color.
        /// </summary>
        [Test]
        public void Generate_AmmoPerColorEqualsPixelCount()
        {
            const int width = 100, height = 50, colors = 6;
            byte[] cells = RandomCells(width, height, colors, 12345);

            ColorTankData[] tanks = TankGenerator.Generate(width, height, cells, 20);

            var expected = new int[colors];
            foreach (byte c in cells)
            {
                if (c != LevelData.EmptyCell) expected[c]++;
            }

            var actual = new int[colors];
            foreach (ColorTankData t in tanks)
            {
                actual[t.colorId] += t.ammo;
            }

            CollectionAssert.AreEqual(expected, actual);
        }

        /// <summary>
        /// Every tank holds between 1 and ammoPerTank shots.
        /// </summary>
        [Test]
        public void Generate_NoTankExceedsAmmoPerTank_AndNoneIsEmpty()
        {
            const int width = 100, height = 50;
            byte[] cells = RandomCells(width, height, 7, 777);

            ColorTankData[] tanks = TankGenerator.Generate(width, height, cells, 20);

            Assert.That(tanks.Length, Is.GreaterThan(0));
            foreach (ColorTankData t in tanks)
            {
                Assert.That(t.ammo, Is.InRange(1, 20), $"Tank of color {t.colorId} has ammo {t.ammo}");
            }
        }

        /// <summary>
        /// Tanks are ordered by the peel time of their first cell: the bottom color comes first.
        /// </summary>
        [Test]
        public void Generate_OrdersTanksByPeelTime()
        {
            var level = TestLevels.Create(new[] { "1", "0" }, new Color32[] { Color.red, Color.blue }, new ColorTankData[0]);
            try
            {
                ColorTankData[] tanks = TankGenerator.Generate(level.width, level.height, level.cells, 20);

                Assert.AreEqual(2, tanks.Length);
                Assert.AreEqual(0, tanks[0].colorId);
                Assert.AreEqual(1, tanks[1].colorId);
                Assert.AreEqual(1, tanks[0].ammo);
                Assert.AreEqual(1, tanks[1].ammo);
            }
            finally
            {
                Object.DestroyImmediate(level);
            }
        }

        /// <summary>
        /// The peel order follows the belt sweep: on a 2x2 board with colour 1 at the top-left, one round visits
        /// Bottom col 0 (0,0), Bottom col 1 (1,0), Right row 0 (empty), Right row 1 (1,1), Top col 1 (empty) and
        /// Top col 0 (0,1), so the colour-1 cell is peeled last.
        /// </summary>
        [Test]
        public void Generate_PeelOrder_FollowsBeltSweep()
        {
            var level = TestLevels.Create(new[] { "10", "00" }, new Color32[] { Color.red, Color.blue }, new ColorTankData[0]);
            try
            {
                ColorTankData[] tanks = TankGenerator.Generate(level.width, level.height, level.cells, 1);

                var colours = new int[tanks.Length];
                for (int i = 0; i < tanks.Length; i++)
                {
                    colours[i] = tanks[i].colorId;
                    Assert.AreEqual(1, tanks[i].ammo, $"ammo of tank {i}");
                }
                CollectionAssert.AreEqual(new[] { 0, 0, 0, 1 }, colours);
            }
            finally
            {
                Object.DestroyImmediate(level);
            }
        }

        /// <summary>
        /// Distinct RGB colors map to palette entries in order of appearance; transparent pixels become empty.
        /// </summary>
        [Test]
        public void Quantize_MapsColorsAndTransparency()
        {
            Color32 red = new Color32(255, 0, 0, 255);
            Color32 blue = new Color32(0, 0, 255, 255);
            Color32 clear = new Color32(0, 0, 0, 0);
            var pixels = new[] { red, red, clear, blue };

            TextureQuantizer.Quantize(pixels, 2, 2, out Color32[] palette, out byte[] cells);

            CollectionAssert.AreEqual(new[] { red, blue }, palette);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 255, 1 }, cells);
        }

        /// <summary>
        /// Images with more than 254 distinct opaque colors are rejected.
        /// </summary>
        [Test]
        public void Quantize_MoreThan254Colors_Throws()
        {
            const int count = 255;
            var pixels = new Color32[count];
            for (int i = 0; i < count; i++)
            {
                pixels[i] = new Color32((byte)i, 0, 0, 255);
            }

            Assert.Throws<ArgumentException>(() =>
                TextureQuantizer.Quantize(pixels, count, 1, out _, out _));
        }

        /// <summary>
        /// The 64x64 sample level has 4096 filled cells and the AutoPlayer can win it under the conveyor-belt rules.
        /// </summary>
        [Test]
        public void SampleLevel_Has4096Cells_AndIsSolvableByAutoPlayer()
        {
            LevelData level = SampleLevelFactory.Create64();
            try
            {
                Assert.AreEqual(64, level.width);
                Assert.AreEqual(64, level.height);
                Assert.AreEqual(4096, level.cells.Length);
                foreach (byte c in level.cells)
                {
                    Assert.AreNotEqual(LevelData.EmptyCell, c);
                }
                Assert.AreEqual(5, level.palette.Length);
                Assert.AreEqual(3, level.laneCount);
                Assert.AreEqual(5, level.slotCount);

                Assert.AreEqual(GameState.Won, AutoPlayer.Play(LevelSession.Create(level)));
            }
            finally
            {
                Object.DestroyImmediate(level);
            }
        }

        /// <summary>
        /// Deserializing the sample level and building its session takes less than 50 ms.
        /// </summary>
        [Test]
        public void SampleLevel_SerializedLoadUnder50ms()
        {
            LevelData source = SampleLevelFactory.Create64();
            LevelData target = ScriptableObject.CreateInstance<LevelData>();
            try
            {
                byte[] bytes = LevelSerializer.ToBytes(source);

                var sw = Stopwatch.StartNew();
                LevelSerializer.FromBytes(bytes, target);
                LevelSession session = LevelSession.Create(target);
                sw.Stop();

                Assert.AreEqual(4096, session.Grid.RemainingCount);
                Assert.That(sw.Elapsed.TotalMilliseconds, Is.LessThan(50.0), $"Load took {sw.Elapsed.TotalMilliseconds:F2} ms");
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(target);
            }
        }
    }
}
