using System.IO;
using PixelFlow.Core;
using PixelFlow.Data;
using UnityEditor;
using UnityEngine;

namespace PixelFlow.EditorTools
{
    /// <summary>
    /// Editor window that bakes a readable Texture2D into a LevelData asset saved next to the texture.
    /// Pipeline: <see cref="TextureQuantizer.Quantize"/> then <see cref="TankGenerator.Generate"/>.
    /// </summary>
    public sealed class LevelBakerWindow : EditorWindow
    {
        [SerializeField] private Texture2D _texture;
        [SerializeField] private int _ammoPerTank = 20;
        [SerializeField] private int _laneCount = 3;
        [SerializeField] private int _slotCount = 5;

        /// <summary>
        /// Opens the Level Baker window.
        /// </summary>
        [MenuItem("PixelFlow/Level Baker")]
        public static void Open()
        {
            GetWindow<LevelBakerWindow>("Level Baker");
        }

        private void OnGUI()
        {
            _texture = (Texture2D)EditorGUILayout.ObjectField("Texture (Read/Write)", _texture, typeof(Texture2D), false);
            _ammoPerTank = Mathf.Max(1, EditorGUILayout.IntField("Ammo Per Tank", _ammoPerTank));
            _laneCount = Mathf.Max(1, EditorGUILayout.IntField("Lane Count", _laneCount));
            _slotCount = Mathf.Max(1, EditorGUILayout.IntField("Slot Count", _slotCount));

            if (_texture != null && !_texture.isReadable)
            {
                EditorGUILayout.HelpBox("Enable Read/Write in the texture import settings.", MessageType.Error);
            }

            using (new EditorGUI.DisabledScope(_texture == null || !_texture.isReadable))
            {
                if (GUILayout.Button("Bake"))
                {
                    Bake();
                }
            }
        }

        private void Bake()
        {
            int width = _texture.width;
            int height = _texture.height;

            TextureQuantizer.Quantize(_texture.GetPixels32(), width, height, out Color32[] palette, out byte[] cells);

            var level = CreateInstance<LevelData>();
            level.width = width;
            level.height = height;
            level.palette = palette;
            level.cells = cells;
            level.tanks = TankGenerator.Generate(width, height, cells, _ammoPerTank);
            level.laneCount = _laneCount;
            level.slotCount = _slotCount;

            int cellCount = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] != LevelData.EmptyCell) cellCount++;
            }
            int tankCount = level.tanks.Length;

            string texturePath = AssetDatabase.GetAssetPath(_texture);
            string assetPath = Path.Combine(Path.GetDirectoryName(texturePath), Path.GetFileNameWithoutExtension(texturePath) + "_Level.asset").Replace('\\', '/');

            LevelData saved = SampleLevelFactory.SaveOrReplace(level, assetPath);
            Debug.Log($"[PixelFlow] Baked {assetPath}: {width}x{height}, {cellCount} cells, {palette.Length} colors, {tankCount} tanks.", saved);
        }
    }
}
