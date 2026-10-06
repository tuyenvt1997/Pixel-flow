using System.Collections.Generic;
using System.Globalization;
using PixelFlow.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace PixelFlow.View
{
    /// <summary>
    /// Presents the conveyor belt: a rounded-rectangle track one track-width outside the board, chevron arrows
    /// scrolling counter-clockwise in the direction of travel, and the "used/capacity" counter at the entrance
    /// (bottom-left). Maps fractional belt positions to world points for the tank views (<see cref="PositionToWorld"/>).
    /// <para>Belt position <c>p</c> sits on the track centreline next to its line (spec §1): bottom positions
    /// under their column, right positions beside their row, and so on. Between two positions the point moves at a
    /// constant speed along the centreline; between the last position of an edge and the first of the next it follows
    /// the rounded corner arc of the track.</para>
    /// <para>The belt clock (<see cref="SetBeltClock"/>, pushed by the controller every frame while the belt runs)
    /// gives the tick phase the tank views interpolate with, and scrolls the chevrons by the distance a tank covers on
    /// a straight edge, so tanks and arrows move together; while the clock is not advanced everything holds still.</para>
    /// The track, chevrons, track rims and the board well (the darker panel inside the track) are one mesh with four
    /// sub-meshes, rebuilt by <see cref="Build"/> (level load only); the chevron vertices are rewritten in
    /// <c>LateUpdate</c> into a preallocated array. Nothing allocates per frame.
    /// </summary>
    public sealed class BeltView : MonoBehaviour
    {
        private const int CornerSegments = 8;
        private const float TrackDepth = 0.6f;
        private const float ChevronDepth = 0.58f;
        private const float RimDepth = 0.59f;
        private const float WellDepth = 0.62f;
        private const float CornerRadiusFraction = 0.7f; // centreline arc radius / track width
        private const int ChevronVertexCount = 8;

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

        [Tooltip("Approximate distance in world units between two chevrons; they are spread evenly around the " +
                 "whole track.")]
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

        private readonly Vector2[] _chevronShape = new Vector2[ChevronVertexCount];
        private Vector3[] _meshVertices = new Vector3[0];
        private int _chevronStart;
        private int _chevronCount;
        private float _chevronStep;
        private float _chevronScroll;
        private bool _chevronsDirty;
        private float _clock;

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
        private float _radius;
        private float _straightX;
        private float _straightY;
        private float _arc;
        private float _perimeter;

        /// <summary>
        /// Track width in world units (tanks on the belt are scaled to fit it).
        /// </summary>
        public float TrackWidth => trackWidth;

        /// <summary>
        /// Number of belt positions of the last <see cref="Build"/> (0 when cleared).
        /// </summary>
        public int Length => _length;

        /// <summary>
        /// Belt clock last set with <see cref="SetBeltClock"/>, in belt positions within <c>[0, Length)</c>
        /// (0 after <see cref="Build"/>).
        /// </summary>
        public float Clock => _clock;

        /// <summary>
        /// Fraction of the current tick interval that has elapsed, in <c>[0, 1)</c>: the fractional part of
        /// <see cref="Clock"/>. Belt tanks are drawn this far from their previous position towards their model position.
        /// </summary>
        public float Phase => _clock - Mathf.Floor(_clock);

        /// <summary>
        /// Distance in world units the chevrons have scrolled along the track since <see cref="Build"/>, wrapped to
        /// the spacing between two chevrons.
        /// </summary>
        public float ChevronScroll => _chevronScroll;

        /// <summary>
        /// Number of chevrons spread around the track (0 when cleared).
        /// </summary>
        public int ChevronCount => _chevronCount;

        /// <summary>
        /// Distance in world units along the track between two consecutive chevrons (the perimeter divided by
        /// <see cref="ChevronCount"/>).
        /// </summary>
        public float ChevronStep => _chevronStep;

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
            _radius = trackWidth * CornerRadiusFraction;
            _straightX = _right - _left - 2f * _radius;
            _straightY = _top - _bottom - 2f * _radius;
            _arc = 0.5f * Mathf.PI * _radius;
            _perimeter = 2f * (_straightX + _straightY) + 4f * _arc;
            _clock = 0f;
            _chevronScroll = 0f;

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
            _chevronCount = 0;
            _chevronsDirty = false;
            _clock = 0f;
            _chevronScroll = 0f;
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
        /// next to their line; between two positions the point moves at a constant speed along the centreline,
        /// around the rounded corner arc when the two positions are on different edges. The position wraps around the
        /// loop, so <c>Length - 0.5</c> (and <c>-0.5</c>) is the middle of the bottom-left arc at the entrance.
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
            float from = DistanceAt(k);
            float to = k + 1 == _length ? _perimeter + DistanceAt(0) : DistanceAt(k + 1);
            return TrackPoint(from + (to - from) * t, out _);
        }

        /// <summary>
        /// Moves the belt clock to <paramref name="clock"/> (completed ticks modulo <see cref="Length"/> plus the
        /// elapsed fraction of the current tick). <see cref="Phase"/> follows it, and the chevrons scroll forward by
        /// the distance the clock moved (wrapping at <see cref="Length"/>) times the cell size, i.e. exactly as far as
        /// a tank on a straight edge. Not calling it freezes the belt. Allocation-free.
        /// </summary>
        /// <param name="clock">New belt clock, in <c>[0, Length)</c>.</param>
        public void SetBeltClock(float clock)
        {
            if (_length <= 0)
                return;

            float delta = clock - _clock;
            if (delta < 0f)
                delta += _length;
            _clock = clock;
            if (delta <= 0f || _chevronCount == 0)
                return;

            _chevronScroll = Mathf.Repeat(_chevronScroll + delta * _cellSize, _chevronStep);
            _chevronsDirty = true;
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

        private void LateUpdate()
        {
            if (!_chevronsDirty || _mesh == null)
                return;

            _chevronsDirty = false;
            WriteChevrons();
            _mesh.SetVertices(_meshVertices, 0, _meshVertices.Length,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
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

        /// <summary>
        /// Distance along the centreline loop (measured counter-clockwise from the end of the bottom-left arc) of
        /// integer belt position <paramref name="p"/>.
        /// </summary>
        private float DistanceAt(int p)
        {
            Vector3 point = PointAt(p);
            if (p < _width)
                return point.x - (_left + _radius);
            if (p < _width + _height)
                return _straightX + _arc + point.y - (_bottom + _radius);
            if (p < 2 * _width + _height)
                return _straightX + _straightY + 2f * _arc + (_right - _radius) - point.x;
            return 2f * _straightX + _straightY + 3f * _arc + (_top - _radius) - point.y;
        }

        /// <summary>
        /// Point (z = 0) and unit tangent (direction of travel) at <paramref name="distance"/> along the centreline
        /// loop: bottom edge, bottom-right arc, right edge, top-right arc, top edge, top-left arc, left edge,
        /// bottom-left arc. The distance wraps around the loop.
        /// </summary>
        private Vector3 TrackPoint(float distance, out Vector2 tangent)
        {
            float s = Mathf.Repeat(distance, _perimeter);
            for (int c = 0; c < 4; c++)
            {
                float straight = (c & 1) == 0 ? _straightX : _straightY;
                if (s < straight)
                    return StraightPoint(c, s, out tangent);

                s -= straight;
                if (s < _arc || c == 3) // the last arc also absorbs any rounding remainder
                {
                    float angle = (-90f + 90f * c + 90f * Mathf.Min(s / _arc, 1f)) * Mathf.Deg2Rad;
                    float cos = Mathf.Cos(angle);
                    float sin = Mathf.Sin(angle);
                    tangent = new Vector2(-sin, cos);
                    Vector2 centre = CornerCentre(c);
                    return new Vector3(centre.x + cos * _radius, centre.y + sin * _radius, 0f);
                }

                s -= _arc;
            }

            tangent = Vector2.right; // not reached
            return new Vector3(_left + _radius, _bottom, 0f);
        }

        private Vector3 StraightPoint(int edge, float s, out Vector2 tangent)
        {
            switch (edge)
            {
                case 0:
                    tangent = Vector2.right;
                    return new Vector3(_left + _radius + s, _bottom, 0f);
                case 1:
                    tangent = Vector2.up;
                    return new Vector3(_right, _bottom + _radius + s, 0f);
                case 2:
                    tangent = Vector2.left;
                    return new Vector3(_right - _radius - s, _top, 0f);
                default:
                    tangent = Vector2.down;
                    return new Vector3(_left, _top - _radius - s, 0f);
            }
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
            AddChevrons();

            // Level load only: the cached array the chevron vertices are rewritten into whenever they move.
            if (_meshVertices.Length != _vertices.Count)
                _meshVertices = new Vector3[_vertices.Count];
            _vertices.CopyTo(_meshVertices);
            WriteChevrons();
            _chevronsDirty = false;

            _mesh.Clear();
            _mesh.MarkDynamic();
            _mesh.subMeshCount = 4;
            _mesh.SetVertices(_meshVertices);
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
        /// Adds the chevrons to sub-mesh 1: <c>round(perimeter / chevronSpacing)</c> chevrons spread evenly around the
        /// whole centreline loop, two bars (eight vertices) each. Only the triangles and placeholder vertices are added
        /// here; <see cref="WriteChevrons"/> places the vertices. Also fills the chevron shape (vertex offsets along
        /// and across the track, tip pointing in the direction of travel).
        /// </summary>
        private void AddChevrons()
        {
            float size = trackWidth * 0.32f;
            float thickness = trackWidth * 0.09f;
            var tip = new Vector2(size * 0.5f, 0f);
            SetBarShape(0, tip, new Vector2(-size * 0.5f, size), thickness);
            SetBarShape(4, tip, new Vector2(-size * 0.5f, -size), thickness);

            _chevronCount = Mathf.Max(1, Mathf.RoundToInt(_perimeter / chevronSpacing));
            _chevronStep = _perimeter / _chevronCount;
            _chevronStart = _vertices.Count;
            for (int i = 0; i < _chevronCount; i++)
            {
                int start = _vertices.Count;
                for (int v = 0; v < ChevronVertexCount; v++)
                    _vertices.Add(Vector3.zero);
                AddQuad(_chevronTriangles, start, start + 1, start + 2, start + 3);
                AddQuad(_chevronTriangles, start + 4, start + 5, start + 6, start + 7);
            }
        }

        private void SetBarShape(int index, Vector2 a, Vector2 b, float thickness)
        {
            Vector2 along = (b - a).normalized;
            Vector2 side = new Vector2(-along.y, along.x) * (thickness * 0.5f);
            Vector2 extend = along * (thickness * 0.5f);
            _chevronShape[index] = a - extend - side;
            _chevronShape[index + 1] = a - extend + side;
            _chevronShape[index + 2] = b + extend + side;
            _chevronShape[index + 3] = b + extend - side;
        }

        /// <summary>
        /// Places every chevron vertex into the cached vertex array: chevron <c>i</c> is centred
        /// <c>scroll + i * step</c> along the loop, and each vertex is offset along the centreline, then across it
        /// (along the local normal), so the chevrons follow the track tangent and bend through the corner arcs.
        /// Allocation-free.
        /// </summary>
        private void WriteChevrons()
        {
            for (int i = 0; i < _chevronCount; i++)
            {
                float centre = _chevronScroll + i * _chevronStep;
                int start = _chevronStart + i * ChevronVertexCount;
                for (int v = 0; v < ChevronVertexCount; v++)
                {
                    Vector2 shape = _chevronShape[v];
                    Vector3 point = TrackPoint(centre + shape.x, out Vector2 tangent);
                    _meshVertices[start + v] = new Vector3(point.x - tangent.y * shape.y,
                        point.y + tangent.x * shape.y, ChevronDepth);
                }
            }
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
