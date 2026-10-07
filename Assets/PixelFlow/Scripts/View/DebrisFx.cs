using UnityEngine;

namespace PixelFlow.View
{
    /// <summary>
    /// Shared debris particle effect. Wraps a single <see cref="ParticleSystem"/> that simulates in world space,
    /// holds at most <see cref="MaxParticles"/> particles and never emits on its own; every burst is an explicit
    /// <see cref="Burst"/> call that reuses the system's existing particle buffer (no objects are created).
    /// Particle look (lifetime, speed, shape, gravity) comes from the ParticleSystem settings on the prefab; the
    /// particle size is set per burst from the destroyed pixel's size, so big pixels shed big debris.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class DebrisFx : MonoBehaviour
    {
        /// <summary>
        /// Maximum number of live debris particles.
        /// </summary>
        public const int MaxParticles = 2000;

        /// <summary>
        /// Start size of the smaller debris particles, as a fraction of the cell size passed to <see cref="Burst"/>.
        /// </summary>
        public const float MinSizeFactor = 0.3f;

        /// <summary>
        /// Start size of the larger debris particles, as a fraction of the cell size passed to <see cref="Burst"/>.
        /// </summary>
        public const float MaxSizeFactor = 0.55f;

        private ParticleSystem _system;
        private ParticleSystem.EmitParams _emitParams;

        private void Awake()
        {
            EnsureInitialised();
        }

        /// <summary>
        /// Emits <paramref name="count"/> debris particles at <paramref name="pos"/> tinted
        /// <paramref name="color"/>, sized relative to <paramref name="cellSize"/>: half of them (rounded up) at
        /// <see cref="MaxSizeFactor"/> x cellSize, the rest at <see cref="MinSizeFactor"/> x cellSize.
        /// Uses a cached <see cref="ParticleSystem.EmitParams"/>; does not allocate.
        /// </summary>
        /// <param name="pos">World-space emission position.</param>
        /// <param name="color">Particle start colour.</param>
        /// <param name="cellSize">Edge length in world units of the destroyed pixel; scales the particle size.</param>
        /// <param name="count">Number of particles to emit.</param>
        public void Burst(Vector3 pos, Color32 color, float cellSize, int count = 6)
        {
            if (count <= 0)
                return;
            if (_system == null)
                EnsureInitialised();

            _emitParams.position = pos;
            _emitParams.startColor = color;

            int large = (count + 1) / 2;
            _emitParams.startSize = cellSize * MaxSizeFactor;
            _system.Emit(_emitParams, large);
            if (count > large)
            {
                _emitParams.startSize = cellSize * MinSizeFactor;
                _system.Emit(_emitParams, count - large);
            }
        }

        /// <summary>
        /// Removes every live particle immediately (e.g. when a level is torn down).
        /// </summary>
        public void ClearParticles()
        {
            if (_system != null)
                _system.Clear();
        }

        private void EnsureInitialised()
        {
            _system = GetComponent<ParticleSystem>();

            ParticleSystem.MainModule main = _system.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MaxParticles;
            main.playOnAwake = false;

            ParticleSystem.EmissionModule emission = _system.emission;
            emission.enabled = false;

            _emitParams = new ParticleSystem.EmitParams
            {
                applyShapeToPosition = true,
            };

            if (!_system.isPlaying)
                _system.Play();
        }
    }
}
