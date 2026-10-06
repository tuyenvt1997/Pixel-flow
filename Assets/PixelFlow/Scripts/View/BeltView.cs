using System.Collections.Generic;
using System.Globalization;
using PixelFlow.Core;
using TMPro;
using UnityEngine;

namespace PixelFlow.View
{
    /// <summary>
    /// Presents the conveyor belt: a rounded-rectangle track one track-width outside the board, chevron arrows
    /// showing the counter-clockwise direction of travel, and the "used/capacity" counter at the entrance
    /// (bottom-left). Maps fractional belt positions to world points for the tank views (<see cref="PositionToWorld"/>).
    /// <para>Belt position <c>p</c> sits on the track centreline next to its line (spec §1): bottom positions
    /// under their column, right positions beside their row, and so on. Between the last position of an edge and the
    /// first of the next, the path passes through the track's corner point instead of cutting diagonally.</para>
    /// The track, chevrons, track rims and the board well (the darker panel inside the track) are one mesh with four
    /// sub-meshes, rebuilt by <see cref="Build"/> (level load only).
    /// Nothing allocates per frame.
    /// </summary>
    public sealed class BeltView : MonoBehaviour
    {
        private const int CornerSegments = 8;
        private const float TrackDepth = 0.6f;
        private const float ChevronDepth = 0.58f;
        private const float RimDepth = 0.59f;
        private const float WellDepth = 0.62f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("Renders the track (material 0), the chevrons (material 1), the track rims (material 2) and the " +
                 "board well (material 3). Sub-meshes without a material are not drawn.")]
        [SerializeField] private MeshRenderer trackRenderer;

        [Tooltip("Mesh filter of the track renderer; its mesh is generated at runtime.")]
        [SerializeField] private MeshFilter trackFilter;

        [Tooltip("World-space \"used/capacity\" counter shown below the belt entrance.")]
        [SerializeField] private TextMeshPro counterText;

        [Tooltip("Width of the track in world units. The track centreline is one track-width outside the board.")]
        [Min(0.1f)]
        [SerializeField] private float trackWidth = 0.9f;

        [Tooltip("Distance in world units between two chevrons along an edge.")]
        [Min(0.2f)]
        [SerializeField] private float chevronSpacing = 1.3f;

        [Tooltip("Track colour.")]
        [SerializeField] private Color trackColor = new Color(0.36f, 0.4f, 0.62f, 1f);

        [Tooltip("Chevron colour.")]
        [SerializeField] private Color chevronColor = new Color(0.55f, 0.6f, 0.85f, 1f);

        [Tooltip("Colour of the thin rims along the inner and outer edges of the track.")]
        [SerializeField] private Color rimColor = new Color(0.66f, 0.68f, 0.92f, 1f);

        [Tooltip("Colour of the board well: the panel filling the inside of the track behind the board.")]
        [SerializeField] private Color wellColor = new Color(0.18f, 0.18f, 0.31f, 1f);

        [Tooltip("Width of each track rim as a fraction of the track width.")]
        [Range(0f, 0.4f)]
        [SerializeField] private float rimFraction = 0.1f;

        private readonly List<Vector3> _vertices = new List<Vector3>(256);
        private readonly List<int> _trackTriangles = new List<int>(512);
        private readonly List<int> _chevronTriangles = new List<int>(512);
        private readonly List<int> _rimTriangles = new List<int>(512);
        private readonly List<int> _wellTriangles = new List<int>(256);

        private Mesh _mesh;
        private MaterialPropertyBlock _props;
        private string[] _counterLabels = new string[0];
        private int _labelCapacity = -1;
        private int _shownUsed = -1;
        private int _shownCapacity = -1;

        private int _width;
        private int _height;
        private int _length;
        private Vector3 _origin;
        private float _cellSize;
        private float _left;
        private float _right;
        private float _bottom;
        private float _top;

        /// <summary>
        /// Track width in world units (tanks on the belt are scaled to fit it).
        /// </summary>
        public float TrackWidth => trackWidth;

        /// <summary>
        /// Number of belt positions of the last <see cref="Build"/> (0 when cleared).
        /// </summary>
        public int Length => _length;

        /// <summary>
        /// Lays the track out around the board described by <paramref name="layout"/>: the centreline is one
        /// track-width outside the board edges. Rebuilds the track and chevron mesh, places the counter below the
        /// entrance (bottom-left corner) and shows the track.
        /// </summary>
        /// <param name="path">Belt path of the level (board width and height).</param>
        /// <param name="layout">Board layout the cells are drawn with.</param>
        public void Build(BeltPath path, BoardLayout layout)
        {
            _width = path.Width;
            _height = path.Height;
            _length = path.Length;
            _origin = layout.Origin;
            _cellSize = layout.CellSize;

            float d = trackWidth;
            _left = _origin.x - d;
            _right = _origin.x + _width * _cellSize + d;
            _bottom = _origin.y - d;
            _top = _origin.y + _height * _cellSize + d;

            BuildMesh();

            if (trackRenderer != null)
            {
                if (_props == null)
                    _props = new MaterialPropertyBlock();
                _props.SetColor(BaseColorId, trackColor);
                trackRenderer.SetPropertyBlock(_props, 0);
                _props.SetColor(BaseColorId, chevronColor);
                trackRenderer.SetPropertyBlock(_props, 1);
                int materialCount = trackRenderer.sharedMaterials.Length; // level load only
                if (materialCount > 2)
                {
                    _props.SetColor(BaseColorId, rimColor);
                    trackRenderer.SetPropertyBlock(_props, 2);
                }
                if (materialCount > 3)
                {
                    _props.SetColor(BaseColorId, wellColor);
                    trackRenderer.SetPropertyBlock(_props, 3);
                }
                trackRenderer.enabled = true;
            }

            if (counterText != null)
            {
                counterText.transform.position = new Vector3(_left, _bottom - trackWidth * 0.5f - 0.55f, 0f);
                counterText.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// Hides the track and the counter. Safe to call when nothing is built.
        /// </summary>
        public void Clear()
        {
            _length = 0;
            if (_mesh != null)
                _mesh.Clear();
            if (trackRenderer != null)
                trackRenderer.enabled = false;
            if (counterText != null)
                counterText.gameObject.SetActive(false);
            _shownUsed = -1;
            _shownCapacity = -1;
        }

        /// <summary>
        /// World point of a (fractional) belt position on the track centreline, with z = 0. Integer positions sit
        /// next to their line; between two positions the point is interpolated linearly, through the corner point
        /// when the two positions are on different edges. The position wraps around the loop, so
        /// <c>Length - 0.5</c> (and <c>-0.5</c>) is the bottom-left corner at the entrance.
        /// </summary>
        /// <param name="position">Belt position; any value, taken modulo <see cref="Length"/>.</param>
        /// <returns>The world point, or this transform's position if nothing is built.</returns>
        public Vector3 PositionToWorld(float position)
        {
            if (_length <= 0)
                return transform.position;

            float p = position % _length;
            if (p < 0f)
                p += _length;

            int k = (int)p;
            if (k >= _length)
                k = _length - 1;
            float t = p - k;
            int next = k + 1 == _length ? 0 : k + 1;

            Vector3 a = PointAt(k);
            Vector3 b = PointAt(next);
            if (TryGetCornerAfter(k, out Vector3 corner))
            {
                return t < 0.5f
                    ? Vector3.LerpUnclamped(a, corner, t * 2f)
                    : Vector3.LerpUnclamped(corner, b, t * 2f - 1f);
            }

            return Vector3.LerpUnclamped(a, b, t);
        }

        /// <summary>
        /// World point where the tank at index <paramref name="queueIndex"/> of the entrance queue waits: the
        /// bottom-left corner of the track, later queued tanks stacked slightly down and to the left.
        /// </summary>
        /// <param name="queueIndex">Index in the entrance queue (0 = next to enter).</param>
        /// <returns>The world point.</returns>
        public Vector3 EntrancePosition(int queueIndex)
        {
            float offset = 0.12f * queueIndex;
            return new Vector3(_left - offset, _bottom - offset, 0f);
        }

        /// <summary>
        /// Shows "<paramref name="used"/>/<paramref name="capacity"/>" in the counter (format <c>"{0}/{1}"</c>).
        /// The labels are cached per capacity, so updating the counter during play does not allocate.
        /// </summary>
        /// <param name="used">Tanks on the belt plus tanks in the entrance queue.</param>
        /// <param name="capacity">Belt capacity.</param>
        public void SetCounter(int used, int capacity)
        {
            if (counterText == null || (used == _shownUsed && capacity == _shownCapacity))
                return;

            if (capacity != _labelCapacity && capacity >= 0)
            {
                _counterLabels = new string[capacity + 1];
                for (int i = 0; i <= capacity; i++)
                    _counterLabels[i] = string.Format(CultureInfo.InvariantCulture, "{0}/{1}", i, capacity);
                _labelCapacity = capacity;
            }

            if (used >= 0 && used < _counterLabels.Length && capacity == _labelCapacity)
                counterText.text = _counterLabels[used];
            else
                counterText.SetText("{0}/{1}", used, capacity);

            _shownUsed = used;
            _shownCapacity = capacity;
        }

        private void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
        }

        private Vector3 PointAt(int p)
        {
            float half = 0.5f * _cellSize;
            if (p < _width)
                return new Vector3(_origin.x + p * _cellSize + half, _bottom, 0f);

            int w2h = _width + _height;
            if (p < w2h)
                return new Vector3(_right, _origin.y + (p - _width) * _cellSize + half, 0f);

            if (p < 2 * _width + _height)
            {
                int column = _width - 1 - (p - w2h);
                return new Vector3(_origin.x + column * _cellSize + half, _top, 0f);
            }

            int row = _height - 1 - (p - 2 * _width - _height);
            return new Vector3(_left, _origin.y + row * _cellSize + half, 0f);
        }

        private bool TryGetCornerAfter(int p, out Vector3 corner)
        {
            if (p == _width - 1)
                corner = new Vector3(_right, _bottom, 0f);
            else if (p == _width + _height - 1)
                corner = new Vector3(_right, _top, 0f);
            else if (p == 2 * _width + _height - 1)
                corner = new Vector3(_left, _top, 0f);
            else if (p == _length - 1)
                corner = new Vector3(_left, _bottom, 0f);
            else
            {
                corner = default;
                return false;
            }

            return true;
        }

        private void BuildMesh()
        {
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "BeltTrack" };
                if (trackFilter != null)
                    trackFilter.sharedMesh = _mesh;
            }

            _vertices.Clear();
            _trackTriangles.Clear();
            _chevronTriangles.Clear();
            _rimTriangles.Clear();
            _wellTriangles.Clear();

            AddTrackBand();
            AddWell();
            AddChevrons(new Vector3(_left, _bottom), new Vector3(_right, _bottom), Vector3.right);
            AddChevrons(new Vector3(_right, _bottom), new Vector3(_right, _top), Vector3.up);
            AddChevrons(new Vector3(_right, _top), new Vector3(_left, _top), Vector3.left);
            AddChevrons(new Vector3(_left, _top), new Vector3(_left, _bottom), Vector3.down);

            _mesh.Clear();
            _mesh.subMeshCount = 4;
            _mesh.SetVertices(_vertices);
            _mesh.SetTriangles(_trackTriangles, 0);
            _mesh.SetTriangles(_chevronTriangles, 1);
            _mesh.SetTriangles(_rimTriangles, 2);
            _mesh.SetTriangles(_wellTriangles, 3);
            _mesh.RecalculateBounds();
        }

        /// <summary>
        /// Adds the rounded-rectangle band (an inner and an outer outline whose corner arcs share a centre, joined by
        /// quads) to sub-mesh 0, and the thin inner and outer rims along its edges to sub-mesh 2.
        /// Triangles are emitted for both windings so the band is visible regardless of culling.
        /// </summary>
        private void AddTrackBand()
        {
            float innerRadius = trackWidth * 0.2f;
            float outerRadius = innerRadius + trackWidth;
            float rim = trackWidth * rimFraction;
            AddRing(innerRadius, outerRadius, TrackDepth, _trackTriangles);
            if (rim > 0f)
            {
                AddRing(innerRadius, innerRadius + rim, RimDepth, _rimTriangles);
                AddRing(outerRadius - rim, outerRadius, RimDepth, _rimTriangles);
            }
        }

        /// <summary>
        /// Arc centre of corner <paramref name="c"/> (counter-clockwise, starting bottom-right), shared by the track
        /// outlines, the rims and the well.
        /// </summary>
        private Vector2 CornerCentre(int c)
        {
            float inset = trackWidth * 0.5f + trackWidth * 0.2f;
            switch (c)
            {
                case 0: return new Vector2(_right - inset, _bottom + inset);
                case 1: return new Vector2(_right - inset, _top - inset);
                case 2: return new Vector2(_left + inset, _top - inset);
                default: return new Vector2(_left + inset, _bottom + inset);
            }
        }

        private static Vector2 CornerDirection(int c, int s)
        {
            float angle = (-90f + 90f * c + 90f * s / CornerSegments) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        /// <summary>
        /// Adds a closed rounded-rectangle ring between the radii <paramref name="innerRadius"/> and
        /// <paramref name="outerRadius"/> around the corner centres to <paramref name="triangles"/>.
        /// </summary>
        private void AddRing(float innerRadius, float outerRadius, float depth, List<int> triangles)
        {
            int start = _vertices.Count;
            int ringPoints = 4 * (CornerSegments + 1);
            for (int c = 0; c < 4; c++)
            {
                Vector2 centre = CornerCentre(c);
                for (int s = 0; s <= CornerSegments; s++)
                {
                    Vector2 dir = CornerDirection(c, s);
                    Vector2 inner = centre + dir * innerRadius;
                    Vector2 outer = centre + dir * outerRadius;
                    _vertices.Add(new Vector3(inner.x, inner.y, depth));
                    _vertices.Add(new Vector3(outer.x, outer.y, depth));
                }
            }

            for (int i = 0; i < ringPoints; i++)
            {
                int j = (i + 1) % ringPoints;
                AddQuad(triangles, start + 2 * i, start + 2 * i + 1, start + 2 * j + 1, start + 2 * j);
            }
        }

        /// <summary>
        /// Adds the board well to sub-mesh 3: a rounded rectangle filling the inside of the track (its outline is the
        /// track's inner edge), built as a fan around the board centre, behind the board.
        /// </summary>
        private void AddWell()
        {
            float innerRadius = trackWidth * 0.2f;
            int centre = _vertices.Count;
            _vertices.Add(new Vector3((_left + _right) * 0.5f, (_bottom + _top) * 0.5f, WellDepth));
            int start = _vertices.Count;
            int ringPoints = 4 * (CornerSegments + 1);
            for (int c = 0; c < 4; c++)
            {
                Vector2 corner = CornerCentre(c);
                for (int s = 0; s <= CornerSegments; s++)
                {
                    Vector2 p = corner + CornerDirection(c, s) * innerRadius;
                    _vertices.Add(new Vector3(p.x, p.y, WellDepth));
                }
            }

            for (int i = 0; i < ringPoints; i++)
            {
                int j = (i + 1) % ringPoints;
                _wellTriangles.Add(centre); _wellTriangles.Add(start + i); _wellTriangles.Add(start + j);
                _wellTriangles.Add(centre); _wellTriangles.Add(start + j); _wellTriangles.Add(start + i);
            }
        }

        /// <summary>
        /// Adds evenly spaced chevrons pointing along <paramref name="direction"/> on the straight part of the edge
        /// from <paramref name="from"/> to <paramref name="to"/> (centreline corner points).
        /// </summary>
        private void AddChevrons(Vector3 from, Vector3 to, Vector3 direction)
        {
            float margin = trackWidth * 1.2f;
            float length = Vector3.Distance(from, to) - 2f * margin;
            if (length <= 0f)
                return;

            int count = Mathf.Max(1, Mathf.FloorToInt(length / chevronSpacing) + 1);
            float step = count > 1 ? length / (count - 1) : 0f;
            Vector3 first = from + direction * (margin + (count > 1 ? 0f : length * 0.5f));
            for (int i = 0; i < count; i++)
                AddChevron(first + direction * (step * i), direction);
        }

        private void AddChevron(Vector3 centre, Vector3 direction)
        {
            float size = trackWidth * 0.32f;
            float thickness = trackWidth * 0.09f;
            Vector3 perp = new Vector3(-direction.y, direction.x, 0f);
            Vector3 tip = centre + direction * (size * 0.5f);
            Vector3 armA = centre - direction * (size * 0.5f) + perp * size;
            Vector3 armB = centre - direction * (size * 0.5f) - perp * size;
            AddBar(tip, armA, thickness);
            AddBar(tip, armB, thickness);
        }

        private void AddBar(Vector3 a, Vector3 b, float thickness)
        {
            Vector3 along = (b - a).normalized;
            Vector3 side = new Vector3(-along.y, along.x, 0f) * (thickness * 0.5f);
            Vector3 extend = along * (thickness * 0.5f);
            int start = _vertices.Count;
            Vector3 depth = new Vector3(0f, 0f, ChevronDepth);
            _vertices.Add(a - extend - side + depth);
            _vertices.Add(a - extend + side + depth);
            _vertices.Add(b + extend + side + depth);
            _vertices.Add(b + extend - side + depth);
            AddQuad(_chevronTriangles, start, start + 1, start + 2, start + 3);
        }

        private static void AddQuad(List<int> triangles, int a, int b, int c, int d)
        {
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
            triangles.Add(a); triangles.Add(c); triangles.Add(d);
            triangles.Add(a); triangles.Add(c); triangles.Add(b);
            triangles.Add(a); triangles.Add(d); triangles.Add(c);
        }
    }
}
