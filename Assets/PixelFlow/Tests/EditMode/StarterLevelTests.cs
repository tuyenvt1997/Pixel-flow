using System;
using NUnit.Framework;
using PixelFlow.Core;
using PixelFlow.Data;
using PixelFlow.EditorTools;
using Object = UnityEngine.Object;

namespace PixelFlow.Tests
{
    /// <summary>
    /// Tests for <see cref="StarterLevelFactory"/>: the five small starter levels (fewer, bigger pixels) that are
    /// played before the 64x64 sun level.
    /// </summary>
    public sealed class StarterLevelTests
    {
        /// <summary>
        /// Each starter level has the size, colour count and tank/lane/slot configuration from the level table.
        /// </summary>
        [TestCase(1, 8, 2, 5, 2, 5)]
        [TestCase(2, 12, 3, 8, 2, 5)]
        [TestCase(3, 16, 4, 10, 3, 5)]
        [TestCase(4, 24, 4, 12, 3, 5)]
        [TestCase(5, 32, 5, 15, 3, 5)]
        public void Create_Level_HasExpectedSizeColoursAndConfig(int number, int size, int colours, int ammoPerTank,
            int lanes, int slots)
        {
            LevelData level = StarterLevelFactory.Create(number);
            try
            {
                Assert.AreEqual(size, level.width, "width");
                Assert.AreEqual(size, level.height, "height");
                Assert.AreEqual(size * size, level.cells.Length, "cells length");
                Assert.AreEqual(colours, level.palette.Length, "palette length");
                Assert.AreEqual(lanes, level.laneCount, "laneCount");
                Assert.AreEqual(slots, level.slotCount, "slotCount");
                Assert.Greater(level.tanks.Length, 0, "no tanks");
                foreach (ColorTankData tank in level.tanks)
                {
                    Assert.That(tank.ammo, Is.InRange(1, ammoPerTank), "tank ammo");
                }
                foreach (UnityEngine.Color32 c in level.palette)
                {
                    Assert.AreEqual(255, c.a, "palette alpha");
                }
            }
            finally
            {
                Object.DestroyImmediate(level);
            }
        }

        /// <summary>
        /// Level 1 is a tiny icon: between 30 and 64 non-empty cells.
        /// </summary>
        [Test]
        public void Create_Level1_HasFewPixels()
        {
            LevelData level = StarterLevelFactory.Create(1);
            try
            {
                Assert.That(CountPixels(level), Is.InRange(30, 64));
            }
            finally
            {
                Object.DestroyImmediate(level);
            }
        }

        /// <summary>
        /// The non-empty pixel count strictly increases from level 1 to level 5, and level 5 is still smaller
        /// than the 4096-pixel sun level.
        /// </summary>
        [Test]
        public void Create_Levels_AreSmallerThanNext()
        {
            int previous = 0;
            for (int n = 1; n <= 5; n++)
            {
                LevelData level = StarterLevelFactory.Create(n);
                try
                {
                    int pixels = CountPixels(level);
                    Assert.Greater(pixels, previous, $"Level {n} ({pixels} pixels) is not bigger than level {n - 1} ({previous}).");
                    previous = pixels;
                }
                finally
                {
                    Object.DestroyImmediate(level);
                }
            }
            Assert.Less(previous, 64 * 64, "Level 5 is not smaller than the sun level.");
        }

        /// <summary>
        /// Every non-empty cell indexes the palette, every palette colour is used, and per colour the total tank
        /// ammo equals the number of cells of that colour.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void Create_Level_AllCellsInPalette_AndAmmoMatchesPixels(int number)
        {
            LevelData level = StarterLevelFactory.Create(number);
            try
            {
                var pixels = new int[level.palette.Length];
                foreach (byte c in level.cells)
                {
                    if (c == LevelData.EmptyCell)
                        continue;
                    Assert.Less(c, level.palette.Length, "cell colour outside palette");
                    pixels[c]++;
                }

                var ammo = new int[level.palette.Length];
                foreach (ColorTankData tank in level.tanks)
                {
                    Assert.Less(tank.colorId, level.palette.Length, "tank colour outside palette");
                    ammo[tank.colorId] += tank.ammo;
                }

                for (int c = 0; c < pixels.Length; c++)
                {
                    Assert.Greater(pixels[c], 0, $"Colour {c} is unused.");
                    Assert.AreEqual(pixels[c], ammo[c], $"Ammo of colour {c} does not match its pixel count.");
                }
            }
            finally
            {
                Object.DestroyImmediate(level);
            }
        }

        /// <summary>
        /// The greedy AutoPlayer wins every starter level with the level's own lanes and slots.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void Create_Level_IsSolvableByAutoPlayer(int number)
        {
            LevelData level = StarterLevelFactory.Create(number);
            try
            {
                Assert.AreEqual(GameState.Won, AutoPlayer.Play(LevelSession.Create(level)));
            }
            finally
            {
                Object.DestroyImmediate(level);
            }
        }

        /// <summary>
        /// Level numbers outside 1..5 are rejected.
        /// </summary>
        [TestCase(0)]
        [TestCase(6)]
        public void Create_OutOfRange_Throws(int number)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => StarterLevelFactory.Create(number));
        }

        /// <summary>
        /// The asset path list holds the six levels in play order and ends with the sun level.
        /// </summary>
        [Test]
        public void AssetPaths_HasSixLevelsInOrder_EndingWithSun()
        {
            CollectionAssert.AreEqual(new[]
            {
                "Assets/PixelFlow/Levels/Level_01_Heart.asset",
                "Assets/PixelFlow/Levels/Level_02_Mushroom.asset",
                "Assets/PixelFlow/Levels/Level_03_Smiley.asset",
                "Assets/PixelFlow/Levels/Level_04_Flower.asset",
                "Assets/PixelFlow/Levels/Level_05_House.asset",
                "Assets/PixelFlow/Levels/Level_06_Sun.asset",
            }, StarterLevelFactory.AssetPaths);
            Assert.AreEqual(SampleLevelFactory.AssetPath, StarterLevelFactory.AssetPaths[5]);
        }

        private static int CountPixels(LevelData level)
        {
            int n = 0;
            foreach (byte c in level.cells)
            {
                if (c != LevelData.EmptyCell)
                    n++;
            }
            return n;
        }
    }
}
