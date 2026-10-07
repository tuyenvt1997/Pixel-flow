using System;
using PixelFlow.Performance;
using UnityEngine;

namespace PixelFlow.View
{
    /// <summary>
    /// Moves pooled projectile transforms from a tank muzzle to a target cell. All in-flight projectiles live in
    /// one array of <see cref="ProjectileTween"/> structs updated by a single <c>Update</c>; finished entries are
    /// swap-removed, returned to the pool and reported through <see cref="OnArrived"/>.
    /// Flight time is <c>distance / speed</c> with ease-out quad interpolation.
    /// </summary>
    public sealed class ProjectileSystem : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static MaterialPropertyBlock s_props;

        [Tooltip("Projectile prefab (a small mesh). Instantiated only by the pool factory.")]
        [SerializeField] private Transform projectilePrefab;

        [Tooltip("Projectile speed in world units per second.")]
        [SerializeField] private float speed = 30f;

        [Tooltip("Number of projectiles created up front.")]
        [SerializeField] private int prewarm = 128;

        /// <summary>
        /// State of one projectile in flight.
        /// </summary>
        private struct ProjectileTween
        {
            public Transform t;
            public Vector3 from, to;
            public float elapsed, duration;
            public Vector2Int cell;
            public Color32 color;
        }

        private ObjectPool<Transform> _pool;
        private ProjectileTween[] _active;
        private int _activeCount;

        /// <summary>
        /// Raised when a projectile reaches its target. Parameters: (target cell, projectile colour).
        /// Not raised for projectiles discarded by <see cref="ClearAll"/>.
        /// </summary>
        public event Action<Vector2Int, Color32> OnArrived;

        /// <summary>
        /// Number of projectiles currently in flight.
        /// </summary>
        public int ActiveCount => _activeCount;

        private void Awake()
        {
            EnsurePool();
        }

        /// <summary>
        /// Fires a projectile from <paramref name="from"/> to <paramref name="to"/>. When it arrives,
        /// <see cref="OnArrived"/> is raised with <paramref name="cell"/> and <paramref name="color"/>.
        /// The projectile renderer (if any) is tinted via a shared <see cref="MaterialPropertyBlock"/>.
        /// Does not allocate unless the pool or the in-flight array must grow.
        /// </summary>
        /// <param name="from">World-space start (tank muzzle).</param>
        /// <param name="to">World-space end (cell centre).</param>
        /// <param name="cell">Grid cell the projectile is aimed at.</param>
        /// <param name="color">Projectile colour (sRGB palette entry).</param>
        public void Launch(Vector3 from, Vector3 to, Vector2Int cell, Color32 color)
        {
            EnsurePool();

            Transform t = _pool.Get();
            t.position = from;
            if (t.TryGetComponent(out Renderer r))
            {
                if (s_props == null)
                    s_props = new MaterialPropertyBlock();
                s_props.SetColor(BaseColorId, (Color)color);
                r.SetPropertyBlock(s_props);
            }

            if (_activeCount == _active.Length)
                Array.Resize(ref _active, _active.Length * 2);

            float distance = Vector3.Distance(from, to);
            _active[_activeCount++] = new ProjectileTween
            {
                t = t,
                from = from,
                to = to,
                elapsed = 0f,
                duration = speed > 0f ? distance / speed : 0f,
                cell = cell,
                color = color,
            };
        }

        /// <summary>
        /// Returns every in-flight projectile to the pool without raising <see cref="OnArrived"/>.
        /// </summary>
        public void ClearAll()
        {
            for (int i = 0; i < _activeCount; i++)
            {
                _pool.Release(_active[i].t);
                _active[i] = default;
            }
            _activeCount = 0;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            int i = 0;
            while (i < _activeCount)
            {
                ref ProjectileTween p = ref _active[i];
                p.elapsed += dt;
                float k = p.duration > 0f && p.elapsed < p.duration ? p.elapsed / p.duration : 1f;

                if (k < 1f)
                {
                    float eased = 1f - (1f - k) * (1f - k); // ease-out quad
                    p.t.position = Vector3.LerpUnclamped(p.from, p.to, eased);
                    i++;
                    continue;
                }

                // Arrived: copy out before the swap-remove overwrites slot i (p aliases it).
                Transform t = p.t;
                Vector3 to = p.to;
                Vector2Int cell = p.cell;
                Color32 color = p.color;

                _activeCount--;
                _active[i] = _active[_activeCount];
                _active[_activeCount] = default;

                t.position = to;
                _pool.Release(t);
                OnArrived?.Invoke(cell, color);
            }
        }

        private void EnsurePool()
        {
            if (_pool != null)
                return;

            _active = new ProjectileTween[Mathf.Max(prewarm, 16)];
            _pool = new ObjectPool<Transform>(
                () =>
                {
                    Transform created = Instantiate(projectilePrefab, transform);
                    created.gameObject.SetActive(false);
                    return created;
                },
                p => p.gameObject.SetActive(true),
                p => p.gameObject.SetActive(false),
                Mathf.Max(prewarm, 0));
        }
    }
}
