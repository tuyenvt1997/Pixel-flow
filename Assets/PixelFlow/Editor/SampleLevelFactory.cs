using PixelFlow.Core;
using PixelFlow.Data;
using UnityEditor;
using UnityEngine;

namespace PixelFlow.EditorTools
{
    /// <summary>
    /// Builds the procedural 64x64 sample level (a sun over green ground) and saves it as an asset.
    /// </summary>
    public static class SampleLevelFactory
    {
        /// <summary>
        /// Project path of the generated sample level asset.
        /// </summary>
        public const string AssetPath = "Assets/PixelFlow/Levels/Level_001.asset";

        private const int Size = 64;
        private const int GroundRows = 12;
        private const int AmmoPerTank = 20;

        private const byte Sky = 0;
        private const byte Yellow = 1;
        private const byte Orange = 2;
        private const byte Grass = 3;
        private const byte DarkGrass = 4;

        /// <summary>
        /// Creates the 64x64 sample level in memory: sky background, concentric yellow/orange sun rings,
        /// and 12 rows of green ground at the bottom. No empty cells; 5 colors; tanks from
        /// <see cref="TankGenerator.Generate"/> with 20 ammo each; 3 lanes, 5 slots.
        /// </summary>
        /// <returns>A new (unsaved) LevelData instance; the caller owns it.</returns>
        public static LevelData Create64()
        {
            var cells = new byte[Size * Size];
            const float cx = 31.5f, cy = 42f;

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    byte c;
                    if (y < GroundRows)
                    {
                        // Lighter grass on top, darker soil-like grass below.
                        c = y >= GroundRows - 3 ? Grass : DarkGrass;
                    }
                    else
                    {
                        float dx = x - cx, dy = y - cy;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        if (r < 5f) c = Yellow;
                        else if (r < 9f) c = Orange;
                        else if (r < 13f) c = Yellow;
                        else if (r < 16f) c = Orange;
                        else c = Sky;
                    }
                    cells[y * Size + x] = c;
                }
            }

            var level = ScriptableObject.CreateInstance<LevelData>();
            level.name = "Level_001";
            level.width = Size;
            level.height = Size;
            level.palette = new Color32[]
            {
                new Color32(135, 206, 235, 255), // Sky
                new Color32(255, 221, 51, 255),  // Yellow
                new Color32(255, 140, 26, 255),  // Orange
                new Color32(92, 184, 72, 255),   // Grass
                new Color32(46, 125, 50, 255),   // DarkGrass
            };
            level.cells = cells;
            level.tanks = TankGenerator.Generate(Size, Size, cells, AmmoPerTank);
            level.laneCount = 3;
            level.slotCount = 5;
            return level;
        }

        /// <summary>
        /// Menu command: generates the sample level and writes it to <see cref="AssetPath"/>,
        /// creating the folder if needed and overwriting an existing asset in place (its GUID is kept).
        /// Public so it can be run via <c>-executeMethod</c>.
        /// </summary>
        [MenuItem("PixelFlow/Create Sample Level")]
        public static void CreateAsset()
        {
            if (!AssetDatabase.IsValidFolder("Assets/PixelFlow/Levels"))
            {
                AssetDatabase.CreateFolder("Assets/PixelFlow", "Levels");
            }

            LevelData level = Create64();
            SaveOrReplace(level, AssetPath);
            Debug.Log($"[PixelFlow] Sample level saved to {AssetPath}: {level.width}x{level.height}, {level.palette.Length} colors, {level.tanks.Length} tanks.");
        }

        /// <summary>
        /// Saves <paramref name="level"/> at <paramref name="path"/>. If a LevelData already exists there,
        /// its contents are replaced in place (keeping references to it valid) and <paramref name="level"/> is destroyed.
        /// </summary>
        /// <param name="level">The in-memory level to save.</param>
        /// <param name="path">Project-relative asset path ending in <c>.asset</c>.</param>
        /// <returns>The persisted asset.</returns>
        internal static LevelData SaveOrReplace(LevelData level, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<LevelData>(path);
            LevelData saved;
            if (existing != null)
            {
                string name = existing.name;
                EditorUtility.CopySerialized(level, existing);
                existing.name = name;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(level);
                saved = existing;
            }
            else
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                {
                    AssetDatabase.DeleteAsset(path); // a non-LevelData asset occupies the path
                }
                AssetDatabase.CreateAsset(level, path);
                saved = level;
            }

            AssetDatabase.SaveAssets();
            return saved;
        }
    }
}
