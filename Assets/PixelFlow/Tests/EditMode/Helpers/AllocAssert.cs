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
        /// Maximum number of measured calls before the assertion fails.
        /// </summary>
        public const int MaxAttempts = 3;

        /// <summary>
        /// Asserts that the given action does not allocate GC memory.
        /// Uses Unity's built-in constraint for reliable detection under Mono.
        /// The action is invoked once before the measured calls so that one-off runtime work on the first run of the
        /// delegate (JIT, Mono MethodInfo/metadata creation) is not counted.
        /// Unity's constraint counts "GC.Alloc" profiler sample blocks, which occasionally reports a stray sample even
        /// for an empty delegate (observed about 1 in 200 measurements in a full EditMode run). To keep that artefact
        /// from failing tests, the action is measured up to <see cref="MaxAttempts"/> times and passes on the first
        /// clean measurement; the last attempt uses the plain constraint so the failure message is unchanged.
        /// An action that allocates on every call still fails. Actions must therefore tolerate being run up to
        /// <see cref="MaxAttempts"/> + 1 times.
        /// </summary>
        /// <param name="action">The action to test for allocations.</param>
        public static void NoAlloc(TestDelegate action)
        {
            action();

            for (int attempt = 1; attempt < MaxAttempts; attempt++)
            {
                // The bare constraint means "allocates", so a failed result is a clean measurement.
                if (!new AllocatingGCMemoryConstraint().ApplyTo(action).IsSuccess)
                {
                    return;
                }
            }

            Assert.That(action, Is.Not.AllocatingGCMemory());
        }
    }
}
