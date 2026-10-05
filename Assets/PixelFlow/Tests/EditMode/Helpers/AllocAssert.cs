using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace PixelFlow.Tests
{
    /// <summary>
    /// Helper for asserting zero-allocation requirements in tests.
    /// </summary>
    public static class AllocAssert
    {
        /// <summary>
        /// Asserts that the given action does not allocate GC memory.
        /// Uses Unity's built-in constraint for reliable detection under Mono.
        /// </summary>
        /// <param name="action">The action to test for allocations.</param>
        public static void NoAlloc(TestDelegate action)
        {
            Assert.That(action, Is.Not.AllocatingGCMemory());
        }
    }
}
