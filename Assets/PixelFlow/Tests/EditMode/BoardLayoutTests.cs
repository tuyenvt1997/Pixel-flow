using NUnit.Framework;
using PixelFlow.View;
using UnityEngine;

namespace PixelFlow.Tests
{
    /// <summary>
    /// Tests for <see cref="BoardLayout"/> fitting and cell-to-world mapping.
    /// </summary>
    public class BoardLayoutTests
    {
        private const float Eps = 1e-5f;

        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.AreEqual(expected.x, actual.x, Eps, "x");
            Assert.AreEqual(expected.y, actual.y, Eps, "y");
            Assert.AreEqual(expected.z, actual.z, Eps, "z");
        }

        [Test]
        public void Fit_ChoosesLimitingDimension_AndCenters()
        {
            var layout = BoardLayout.Fit(10, 5, new Rect(0f, 0f, 20f, 20f));

            Assert.AreEqual(2f, layout.CellSize, Eps);
            AssertVector(new Vector3(1f, 6f, 0f), layout.CellToWorld(new Vector2Int(0, 0)));
            AssertVector(new Vector3(19f, 14f, 0f), layout.CellToWorld(new Vector2Int(9, 4)));
        }

        [Test]
        public void CellToWorld_StepsByCellSize()
        {
            var layout = new BoardLayout { Origin = new Vector3(-3f, 2f, 0f), CellSize = 0.5f };

            Vector3 a = layout.CellToWorld(new Vector2Int(0, 0));
            Vector3 b = layout.CellToWorld(new Vector2Int(1, 0));
            Vector3 c = layout.CellToWorld(new Vector2Int(0, 1));
            Vector3 d = layout.CellToWorld(new Vector2Int(4, 6));

            AssertVector(new Vector3(-2.75f, 2.25f, 0f), a);
            AssertVector(new Vector3(0.5f, 0f, 0f), b - a);
            AssertVector(new Vector3(0f, 0.5f, 0f), c - a);
            AssertVector(new Vector3(2f, 3f, 0f), d - a);
        }
    }
}
