using System;
using PixelFlow.Core;
using PixelFlow.Data;
using UnityEditor;
using UnityEngine;

namespace PixelFlow.EditorTools
{
    /// <summary>
    /// Builds the starter levels: five small pictures with few, big pixels (heart, mushroom, smiley, flower,
    /// house with tree) played before the 64x64 sun level. Levels 1 to 3 are drawn from editable string-row art,
    /// levels 4 and 5 procedurally from circles and rectangles. Background cells are empty
    /// (<see cref="LevelData.EmptyCell"/>) so every picture reads as an icon.
    /// </summary>
    public static class StarterLevelFactory
    {
        /// <summary>
        /// Number of starter levels <see cref="Create"/> can build (levels 1 to 5).
        /// </summary>
        public const int StarterLevelCount = 5;

        /// <summary>
        /// Project paths of all levels in play order: the five starter levels followed by the sun level
        /// (<see cref="SampleLevelFactory.AssetPath"/>).
        /// </summary>
        public static readonly string[] AssetPaths =
        {
            "Assets/PixelFlow/Levels/Level_01_Heart.asset",
            "Assets/PixelFlow/Levels/Level_02_Mushroom.asset",
            "Assets/PixelFlow/Levels/Level_03_Smiley.asset",
            "Assets/PixelFlow/Levels/Level_04_Flower.asset",
            "Assets/PixelFlow/Levels/Level_05_House.asset",
            SampleLevelFactory.AssetPath,
        };

        private const byte E = LevelData.EmptyCell;

        // rows[0] is the TOP row, '.' = empty, digits = palette index (same convention as the test helper).
        private static readonly string[] HeartRows =
        {
            ".00..00.",
            "01100000",
            "01000000",
            "00000000",
            ".000000.",
            "..0000..",
            "...00...",
            "........",
        };

        private static readonly string[] MushroomRows =
        {
            "....0000....",
            "..00011000..",
            ".0001111000.",
            ".0100110010.",
            "011100001110",
            "001000000100",
            "000000000000",
            "....2222....",
            "....2222....",
            "...222222...",
            "...222222...",
            "............",
        };

        private static readonly string[] SmileyRows =
        {
            ".....111111.....",
            "...1100000011...",
            "..100000000001..",
            ".10000000000001.",
            ".10013000013001.",
            "1000110000110001",
            "1000110000110001",
            "1000000000000001",
            "1022000000002201",
            "1000100000010001",
            ".10001000010001.",
            ".10000111100001.",
            "..100000000001..",
            "...1100000011...",
            ".....111111.....",
            "................",
        };

        /// <summary>
        /// Creates starter level <paramref name="levelNumber"/> in memory with its tanks from
        /// <see cref="TankGenerator.Generate"/>:
        /// 1 = 8x8 heart (2 colours, 5 ammo/tank, 2 lanes), 2 = 12x12 mushroom (3 colours, 8 ammo, 2 lanes),
        /// 3 = 16x16 smiley (4 colours, 10 ammo, 3 lanes), 4 = 24x24 flower (4 colours, 12 ammo, 3 lanes),
        /// 5 = 32x32 house and tree (5 colours, 15 ammo, 3 lanes). All levels have 5 tray slots.
        /// </summary>
        /// <param name="levelNumber">Starter level number, 1 to <see cref="StarterLevelCount"/>.</param>
        /// <returns>A new (unsaved) LevelData instance; the caller owns it.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if levelNumber is not in 1..5.</exception>
        public static LevelData Create(int levelNumber)
        {
            switch (levelNumber)
            {
                case 1:
                    return Build("Level_01_Heart", 8, FromRows(HeartRows), new[]
                    {
                        new Color32(230, 57, 70, 255),   // red
                        new Color32(255, 182, 193, 255), // pink highlight
                    }, 5, 2);
                case 2:
                    return Build("Level_02_Mushroom", 12, FromRows(MushroomRows), new[]
                    {
                        new Color32(211, 47, 47, 255),   // red cap
                        new Color32(250, 250, 245, 255), // white spots
                        new Color32(239, 207, 160, 255), // beige stem
                    }, 8, 2);
                case 3:
                    return Build("Level_03_Smiley", 16, FromRows(SmileyRows), new[]
                    {
                        new Color32(255, 210, 20, 255),  // yellow face
                        new Color32(74, 44, 24, 255),    // dark brown outline, eyes, mouth
                        new Color32(255, 128, 150, 255), // pink cheeks
                        new Color32(255, 255, 255, 255), // white eye highlights
                    }, 10, 3);
                case 4:
                    return Build("Level_04_Flower", 24, CreateFlowerCells(), new[]
                    {
                        new Color32(236, 96, 160, 255),  // pink petals
                        new Color32(255, 196, 0, 255),   // yellow centre
                        new Color32(102, 187, 106, 255), // green stem
                        new Color32(46, 125, 50, 255),   // dark green leaves
                    }, 12, 3);
                case 5:
                    return Build("Level_05_House", 32, CreateHouseCells(), new[]
                    {
                        new Color32(245, 222, 179, 255), // cream wall
                        new Color32(183, 50, 40, 255),   // red-brown roof and chimney
                        new Color32(129, 212, 250, 255), // light blue windows
                        new Color32(121, 85, 72, 255),   // brown door and trunk
                        new Color32(67, 160, 71, 255),   // green foliage
                    }, 15, 3);
                default:
                    throw new ArgumentOutOfRangeException(nameof(levelNumber), levelNumber,
                        $"Starter levels are numbered 1 to {StarterLevelCount}.");
            }
        }

        /// <summary>
        /// Menu command / <c>-executeMethod</c> entry point: generates starter levels 1 to 5 and the sun level
        /// and saves them at <see cref="AssetPaths"/> (existing assets are replaced in place, keeping their GUIDs).
        /// A legacy <c>Level_001.asset</c> is first moved to the sun path.
        /// </summary>
        [MenuItem("PixelFlow/Create Levels")]
        public static void CreateAllAssets()
        {
            SampleLevelFactory.EnsureLevelFolder();
            for (int n = 1; n <= StarterLevelCount; n++)
            {
                LevelData saved = SampleLevelFactory.SaveOrReplace(Create(n), AssetPaths[n - 1]);
                Debug.Log($"[PixelFlow] Level {n} saved to {AssetPaths[n - 1]}: {saved.width}x{saved.height}, " +
                          $"{saved.palette.Length} colors, {saved.tanks.Length} tanks.");
            }
            SampleLevelFactory.CreateAsset();
        }

        private static LevelData Build(string name, int size, byte[] cells, Color32[] palette, int ammoPerTank, int lanes)
        {
            if (cells.Length != size * size)
                throw new InvalidOperationException($"{name}: expected {size * size} cells, got {cells.Length}.");

            var level = ScriptableObject.CreateInstance<LevelData>();
            level.name = name;
            level.width = size;
            level.height = size;
            level.palette = palette;
            level.cells = cells;
            level.tanks = TankGenerator.Generate(size, size, cells, ammoPerTank);
            level.laneCount = lanes;
            level.slotCount = 5;
            return level;
        }

        /// <summary>
        /// Converts square string-row art (rows[0] = top row) into row-major cells with y = 0 at the bottom.
        /// </summary>
        private static byte[] FromRows(string[] rows)
        {
            int size = rows.Length;
            var cells = new byte[size * size];
            for (int r = 0; r < size; r++)
            {
                string row = rows[r];
                if (row.Length != size)
                    throw new InvalidOperationException($"Art row {r} has length {row.Length}, expected {size}.");

                int y = size - 1 - r;
                for (int x = 0; x < size; x++)
                {
                    char c = row[x];
                    if (c == '.')
                        cells[y * size + x] = E;
                    else if (c >= '0' && c <= '9')
                        cells[y * size + x] = (byte)(c - '0');
                    else
                        throw new InvalidOperationException($"Invalid art character '{c}' in row {r}.");
                }
            }
            return cells;
        }

        /// <summary>
        /// 24x24 flower: yellow centre, five round pink petals, a two-cell green stem and two dark green leaves.
        /// </summary>
        private static byte[] CreateFlowerCells()
        {
            const int size = 24;
            const float cx = 11.5f, cy = 14.5f;
            var cells = new byte[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    byte c = E;
                    if (Dist(x, y, cx, cy) < 3.1f)
                    {
                        c = 1;
                    }
                    else if (IsPetal(x, y, cx, cy))
                    {
                        c = 0;
                    }
                    else if ((x == 11 || x == 12) && y >= 1 && y <= 9)
                    {
                        c = 2;
                    }
                    else if (InEllipse(x, y, 7.5f, 2.5f, 3.8f, 1.7f) || InEllipse(x, y, 15.5f, 4f, 3.8f, 1.7f))
                    {
                        c = 3;
                    }
                    cells[y * size + x] = c;
                }
            }
            return cells;
        }

        private static bool IsPetal(int x, int y, float cx, float cy)
        {
            for (int k = 0; k < 5; k++)
            {
                float a = k * Mathf.PI * 2f / 5f + Mathf.PI / 2f;
                if (Dist(x, y, cx + Mathf.Cos(a) * 5.9f, cy + Mathf.Sin(a) * 5.9f) < 3.2f)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 32x32 house and tree: cream wall with a brown door and two blue windows, a red-brown triangular roof
        /// with a chimney, and a tree (brown trunk, round green crown) to the right.
        /// </summary>
        private static byte[] CreateHouseCells()
        {
            const int size = 32;
            const int lift = 4; // centres the picture vertically
            var cells = new byte[size * size];
            for (int row = 0; row < size; row++)
            {
                int y = row - lift;
                for (int x = 0; x < size; x++)
                {
                    byte c = E;
                    bool wall = x >= 3 && x <= 18 && y >= 1 && y <= 13;
                    if (wall)
                    {
                        bool door = x >= 9 && x <= 12 && y <= 7;
                        bool window = (x >= 5 && x <= 7 || x >= 14 && x <= 16) && y >= 8 && y <= 11;
                        c = door ? (byte)3 : window ? (byte)2 : (byte)0;
                    }
                    else if (y >= 14 && y <= 22 && Mathf.Abs(x - 10.5f) <= 9f - (y - 14) * 1.1f)
                    {
                        c = 1; // roof
                    }
                    else if (x >= 15 && x <= 16 && y >= 18 && y <= 23)
                    {
                        c = 1; // chimney
                    }
                    else if (Dist(x, y, 25.5f, 15f) < 6.2f)
                    {
                        c = 4; // crown
                    }
                    else if (x >= 24 && x <= 26 && y >= 1 && y <= 9)
                    {
                        c = 3; // trunk
                    }
                    cells[row * size + x] = c;
                }
            }
            return cells;
        }

        private static float Dist(int x, int y, float cx, float cy)
        {
            float dx = x - cx, dy = y - cy;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static bool InEllipse(int x, int y, float cx, float cy, float rx, float ry)
        {
            float dx = (x - cx) / rx, dy = (y - cy) / ry;
            return dx * dx + dy * dy < 1f;
        }
    }
}
