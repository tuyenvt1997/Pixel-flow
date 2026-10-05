using UnityEngine;

namespace PixelFlow.View
{
    /// <summary>
    /// Shared debris particle effect. Wraps a single <see cref="ParticleSystem"/> that simulates in world space,
    /// holds at most <see cref="MaxParticles"/> particles and never emits on its own; every burst is an explicit
    /// <see cref="Burst"/> call that reuses the system's existing particle buffer (no objects are created).
    /// Particle look (lifetime, speed, size, shape, gravity) comes from the ParticleSystem settings on the prefab.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class DebrisFx : MonoBehaviour
    {
        /// <summary>
        /// Maximum number of live debris particles.
        /// </summary>
        public const int MaxParticles = 2000;

        private ParticleSystem _system;
        private ParticleSystem.EmitParams _emitParams;

        private void Awake()
        {
            EnsureInitialised();
        }

        /// <summary>
        /// Emits <paramref name="count"/> debris particles at <paramref name="pos"/> tinted
        /// <paramref name="color"/>. Uses a cached <see cref="ParticleSystem.EmitParams"/>; does not allocate.
        /// </summary>
        /// <param name="pos">World-space emission position.</param>
        /// <param name="color">Particle start colour.</param>
        /// <param name="count">Number of particles to emit.</param>
        public void Burst(Vector3 pos, Color32 color, int count = 6)
        {
            if (count <= 0)
                return;
            if (_system == null)
                EnsureInitialised();

            _emitParams.position = pos;
            _emitParams.startColor = color;
            _system.Emit(_emitParams, count);
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
