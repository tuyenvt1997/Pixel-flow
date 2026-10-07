using NUnit.Framework;
using PixelFlow.View;
using UnityEngine;

namespace PixelFlow.Tests
{
    /// <summary>
    /// Tests that <see cref="DebrisFx"/> debris scales with the size of the destroyed pixel.
    /// </summary>
    public sealed class DebrisFxTests
    {
        private GameObject _go;
        private DebrisFx _debris;
        private ParticleSystem _system;

        /// <summary>
        /// Creates a debris effect on a fresh GameObject.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("DebrisFxTest", typeof(ParticleSystem));
            _system = _go.GetComponent<ParticleSystem>();
            _debris = _go.AddComponent<DebrisFx>();
        }

        /// <summary>
        /// Destroys the test GameObject.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        /// <summary>
        /// Every emitted particle's start size is proportional to the cell size passed to Burst
        /// (within <see cref="DebrisFx.MinSizeFactor"/>..<see cref="DebrisFx.MaxSizeFactor"/> of it).
        /// </summary>
        [TestCase(1.125f)]
        [TestCase(0.14f)]
        public void Burst_ParticleSizeScalesWithCellSize(float cellSize)
        {
            _debris.Burst(Vector3.zero, new Color32(255, 0, 0, 255), cellSize, 6);

            var particles = new ParticleSystem.Particle[16];
            int n = _system.GetParticles(particles);
            Assert.AreEqual(6, n, "particle count");
            for (int i = 0; i < n; i++)
            {
                float size = particles[i].startSize;
                Assert.That(size, Is.InRange(cellSize * DebrisFx.MinSizeFactor - 1e-5f, cellSize * DebrisFx.MaxSizeFactor + 1e-5f),
                    $"particle {i} size {size} for cell size {cellSize}");
            }
        }

        /// <summary>
        /// Debris of a big pixel is bigger than debris of a small pixel.
        /// </summary>
        [Test]
        public void Burst_BiggerCell_BiggerDebris()
        {
            var particles = new ParticleSystem.Particle[16];

            _debris.Burst(Vector3.zero, new Color32(255, 0, 0, 255), 0.1f, 1);
            _system.GetParticles(particles);
            float small = particles[0].startSize;
            _debris.ClearParticles();

            _debris.Burst(Vector3.zero, new Color32(255, 0, 0, 255), 1f, 1);
            _system.GetParticles(particles);
            float big = particles[0].startSize;

            Assert.Greater(big, small * 5f);
        }
    }
}
