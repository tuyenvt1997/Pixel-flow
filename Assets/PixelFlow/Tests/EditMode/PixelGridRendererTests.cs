using NUnit.Framework;
using PixelFlow.Core;
using PixelFlow.Data;
using PixelFlow.View;
using UnityEditor;
using UnityEngine;

namespace PixelFlow.Tests
{
    /// <summary>
    /// Tests for <see cref="PixelGridRenderer"/> instance/batch bookkeeping (no actual GPU draw).
    /// </summary>
    public class PixelGridRendererTests
    {
        private const string SunLevelPath = PixelFlow.EditorTools.SampleLevelFactory.AssetPath;

        private GameObject _go;
        private GameObject _cubePrimitive;
        private PixelGridRenderer _renderer;

        [SetUp]
        public void SetUp()
        {
            _cubePrimitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _go = new GameObject("PixelGridRendererTest");
            _renderer = _go.AddComponent<PixelGridRenderer>();

            var so = new SerializedObject(_renderer);
            so.FindProperty("cubeMesh").objectReferenceValue = _cubePrimitive.GetComponent<MeshFilter>().sharedMesh;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            Object.DestroyImmediate(_cubePrimitive);
        }

        private static readonly Color32[] TwoColors =
        {
            new Color32(255, 0, 0, 255),
            new Color32(0, 0, 255, 255),
        };

        [Test]
        public void Build_SunLevel_Uses5Batches()
        {
            var level = AssetDatabase.LoadAssetAtPath<LevelData>(SunLevelPath);
            Assert.IsNotNull(level, "Sun level asset (Level_06_Sun) missing");
            var session = LevelSession.Create(level);

            _renderer.Build(session.Grid, session.Palette, BoardLayout.Fit(level.width, level.height, new Rect(-5f, -5f, 10f, 10f)));

            Assert.AreEqual(64 * 64, _renderer.InstanceCount);
            Assert.AreEqual(5, _renderer.BatchCount);
        }

        [Test]
        public void Build_SkipsEmptyCells()
        {
            var level = TestLevels.Create(new[] { "0.1", ".0." }, TwoColors, new ColorTankData[0]);
            var grid = new PixelGridModel(level.width, level.height, level.cells);

            _renderer.Build(grid, level.palette, BoardLayout.Fit(3, 2, new Rect(0f, 0f, 3f, 2f)));

            Assert.AreEqual(3, _renderer.InstanceCount);
            Assert.AreEqual(1, _renderer.BatchCount);
            Assert.AreEqual(Matrix4x4.zero, _renderer.GetInstanceMatrix(new Vector2Int(1, 1)), "empty cell has no instance");
            Object.DestroyImmediate(level);
        }

        [Test]
        public void HideCell_ZeroesThatInstanceScale()
        {
            var level = TestLevels.Create(new[] { "01", "10" }, TwoColors, new ColorTankData[0]);
            var grid = new PixelGridModel(level.width, level.height, level.cells);
            var layout = BoardLayout.Fit(2, 2, new Rect(0f, 0f, 2f, 2f));
            _renderer.Build(grid, level.palette, layout);

            Matrix4x4 before = _renderer.GetInstanceMatrix(new Vector2Int(1, 0));
            Assert.AreEqual(0.92f, before.lossyScale.x, 1e-4f, "cube scale = CellSize * 0.92");
            Assert.AreEqual(layout.CellToWorld(new Vector2Int(1, 0)), before.GetPosition());

            _renderer.HideCell(new Vector2Int(1, 0));

            Matrix4x4 after = _renderer.GetInstanceMatrix(new Vector2Int(1, 0));
            Assert.AreEqual(Vector3.zero, after.lossyScale);
            Assert.AreEqual(0.92f, _renderer.GetInstanceMatrix(new Vector2Int(0, 0)).lossyScale.x, 1e-4f, "other cells untouched");
            Assert.AreEqual(4, _renderer.InstanceCount, "hiding keeps the instance slot");
            Object.DestroyImmediate(level);
        }

        [Test]
        public void Build_Twice_ReusesArrays_WhenLargeEnough()
        {
            var level = AssetDatabase.LoadAssetAtPath<LevelData>(SunLevelPath);
            var session = LevelSession.Create(level);
            var layout = BoardLayout.Fit(level.width, level.height, new Rect(-5f, -5f, 10f, 10f));
            _renderer.Build(session.Grid, session.Palette, layout);

            AllocAssert.NoAlloc(() => _renderer.Build(session.Grid, session.Palette, layout));
            Assert.AreEqual(5, _renderer.BatchCount);
        }

        [Test]
        public void Clear_RemovesAllInstances()
        {
            var level = TestLevels.Create(new[] { "01" }, TwoColors, new ColorTankData[0]);
            var grid = new PixelGridModel(level.width, level.height, level.cells);
            _renderer.Build(grid, level.palette, BoardLayout.Fit(2, 1, new Rect(0f, 0f, 2f, 1f)));

            _renderer.Clear();

            Assert.AreEqual(0, _renderer.InstanceCount);
            Assert.AreEqual(0, _renderer.BatchCount);
            Assert.DoesNotThrow(() => _renderer.HideCell(new Vector2Int(0, 0)));
            Object.DestroyImmediate(level);
        }
    }
}
