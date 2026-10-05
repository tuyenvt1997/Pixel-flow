using System;
using System.Collections.Generic;
using PixelFlow.Core;
using PixelFlow.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace PixelFlow.View
{
    /// <summary>
    /// Draws the whole pixel board with GPU instancing (Graphics.RenderMeshInstanced): one cube instance per
    /// non-empty cell, packed into batches of at most <see cref="MaxInstancesPerBatch"/>, each batch with its own
    /// <see cref="MaterialPropertyBlock"/> carrying the per-instance <c>_BaseColor</c> array.
    /// No GameObject is created per cell. The owner calls <see cref="Build"/> when a level loads,
    /// <see cref="HideCell"/> when a cell is destroyed and <see cref="Clear"/> when the board is torn down.
    /// </summary>
    public sealed class PixelGridRenderer : MonoBehaviour
    {
        /// <summary>
        /// Maximum number of instances submitted in one instanced draw call.
        /// </summary>
        public const int MaxInstancesPerBatch = 1023;

        /// <summary>
        /// Cube edge length relative to <see cref="BoardLayout.CellSize"/>; the remainder is the gap between cells.
        /// </summary>
        public const float CubeScaleFactor = 0.92f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("Mesh drawn for every cell (a unit cube).")]
        [SerializeField] private Mesh cubeMesh;

        [Tooltip("Material using PixelFlow/InstancedColor with GPU instancing enabled.")]
        [SerializeField] private Material material;

        /// <summary>
        /// Fixed-size storage for one instanced draw call. Arrays are always full size so the
        /// MaterialPropertyBlock array length never changes and batches can be reused across levels.
        /// </summary>
        private sealed class Batch
        {
            public readonly Matrix4x4[] Matrices = new Matrix4x4[MaxInstancesPerBatch];
            public readonly Vector4[] Colors = new Vector4[MaxInstancesPerBatch];
            public readonly MaterialPropertyBlock Props = new MaterialPropertyBlock();
            public int Count;
            public bool Dirty;
        }

        private readonly List<Batch> _batches = new List<Batch>();
        private int[] _cellToInstance;
        private int _gridWidth;
        private int _gridHeight;
        private int _instanceCount;
        private int _batchCount;
        private RenderParams _renderParams;

        /// <summary>
        /// Number of instanced draw calls issued per frame for the current board.
        /// </summary>
        public int BatchCount => _batchCount;

        /// <summary>
        /// Number of cube instances in the current board (non-empty cells at build time; hidden cells still count).
        /// </summary>
        public int InstanceCount => _instanceCount;

        /// <summary>
        /// Builds the instance data for <paramref name="grid"/>: one cube per non-empty cell, centred at
        /// <see cref="BoardLayout.CellToWorld"/>, scaled to <c>CellSize * 0.92</c>, coloured with the linear
        /// version of its palette entry. Previously allocated arrays are reused when large enough, so
        /// rebuilding a board of the same or a smaller size does not allocate.
        /// </summary>
        /// <param name="grid">Board model to draw.</param>
        /// <param name="palette">Level palette (sRGB), indexed by cell colour id.</param>
        /// <param name="layout">Cell-to-world mapping.</param>
        /// <exception cref="ArgumentNullException">Thrown if grid or palette is null.</exception>
        public void Build(PixelGridModel grid, Color32[] palette, BoardLayout layout)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));
            if (palette == null)
                throw new ArgumentNullException(nameof(palette));

            int width = grid.Width;
            int height = grid.Height;
            int cellCount = width * height;
            if (_cellToInstance == null || _cellToInstance.Length < cellCount)
                _cellToInstance = new int[cellCount];

            _gridWidth = width;
            _gridHeight = height;
            _instanceCount = 0;

            float size = layout.CellSize * CubeScaleFactor;
            var scale = new Vector3(size, size, size);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int cellIndex = y * width + x;
                    byte colorId = grid.GetCell(x, y);
                    if (colorId == LevelData.EmptyCell)
                    {
                        _cellToInstance[cellIndex] = -1;
                        continue;
                    }

                    int instance = _instanceCount++;
                    int batchIndex = instance / MaxInstancesPerBatch;
                    int slot = instance - batchIndex * MaxInstancesPerBatch;
                    if (batchIndex == _batches.Count)
                        _batches.Add(new Batch());

                    Batch batch = _batches[batchIndex];
                    batch.Matrices[slot] = Matrix4x4.TRS(layout.CellToWorld(new Vector2Int(x, y)), Quaternion.identity, scale);
                    batch.Colors[slot] = ((Color)palette[colorId]).linear;
                    _cellToInstance[cellIndex] = instance;
                }
            }

            _batchCount = (_instanceCount + MaxInstancesPerBatch - 1) / MaxInstancesPerBatch;
            for (int b = 0; b < _batchCount; b++)
            {
                Batch batch = _batches[b];
                batch.Count = Mathf.Min(MaxInstancesPerBatch, _instanceCount - b * MaxInstancesPerBatch);
                batch.Dirty = true;
            }

            float boardWidth = width * layout.CellSize;
            float boardHeight = height * layout.CellSize;
            var center = new Vector3(layout.Origin.x + boardWidth * 0.5f, layout.Origin.y + boardHeight * 0.5f, 0f);
            _renderParams = new RenderParams(material)
            {
                layer = gameObject.layer,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                worldBounds = new Bounds(center, new Vector3(boardWidth, boardHeight, layout.CellSize)),
            };
        }

        /// <summary>
        /// Hides the cube of <paramref name="cell"/> by setting its instance matrix scale to zero and marks
        /// its batch dirty (the batch's colour array is re-uploaded in the next <see cref="Draw"/>).
        /// Out-of-range cells, empty cells and calls after <see cref="Clear"/> are ignored.
        /// </summary>
        /// <param name="cell">Grid coordinates of the cell to hide.</param>
        public void HideCell(Vector2Int cell)
        {
            int instance = InstanceOf(cell);
            if (instance < 0)
                return;

            int batchIndex = instance / MaxInstancesPerBatch;
            int slot = instance - batchIndex * MaxInstancesPerBatch;
            Batch batch = _batches[batchIndex];
            Vector3 position = batch.Matrices[slot].GetPosition();
            batch.Matrices[slot] = Matrix4x4.TRS(position, Quaternion.identity, Vector3.zero);
            batch.Dirty = true;
        }

        /// <summary>
        /// Removes every instance; nothing is drawn until the next <see cref="Build"/>. Arrays are kept for reuse.
        /// </summary>
        public void Clear()
        {
            _instanceCount = 0;
            _batchCount = 0;
            _gridWidth = 0;
            _gridHeight = 0;
        }

        /// <summary>
        /// Returns the current instance matrix of <paramref name="cell"/>, or <see cref="Matrix4x4.zero"/>
        /// if the cell has no instance (empty, out of range, or the board was cleared).
        /// </summary>
        /// <param name="cell">Grid coordinates.</param>
        /// <returns>The instance's world matrix (zero scale once hidden).</returns>
        public Matrix4x4 GetInstanceMatrix(Vector2Int cell)
        {
            int instance = InstanceOf(cell);
            if (instance < 0)
                return Matrix4x4.zero;

            int batchIndex = instance / MaxInstancesPerBatch;
            return _batches[batchIndex].Matrices[instance - batchIndex * MaxInstancesPerBatch];
        }

        /// <summary>
        /// Submits one instanced draw call per batch for the current frame. Called from <c>LateUpdate</c>;
        /// may also be called manually (e.g. by an editor capture tool) right before rendering a camera.
        /// Does not allocate.
        /// </summary>
        public void Draw()
        {
            if (_batchCount == 0 || cubeMesh == null || material == null)
                return;

            for (int b = 0; b < _batchCount; b++)
            {
                Batch batch = _batches[b];
                if (batch.Dirty)
                {
                    batch.Props.SetVectorArray(BaseColorId, batch.Colors);
                    batch.Dirty = false;
                }

                _renderParams.matProps = batch.Props;
                Graphics.RenderMeshInstanced(in _renderParams, cubeMesh, 0, batch.Matrices, batch.Count);
            }
        }

        private void LateUpdate()
        {
            Draw();
        }

        private int InstanceOf(Vector2Int cell)
        {
            if (_instanceCount == 0 || cell.x < 0 || cell.y < 0 || cell.x >= _gridWidth || cell.y >= _gridHeight)
                return -1;
            return _cellToInstance[cell.y * _gridWidth + cell.x];
        }
    }
}
