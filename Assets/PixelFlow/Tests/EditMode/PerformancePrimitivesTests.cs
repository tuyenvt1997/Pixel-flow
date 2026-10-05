using NUnit.Framework;
using PixelFlow.Performance;
using System;
using System.Collections.Generic;

namespace PixelFlow.Tests
{
    /// <summary>
    /// Tests for ObjectPool and TimeSlicedQueue performance primitives.
    /// </summary>
    [TestFixture]
    public class PerformancePrimitivesTests
    {
        // Sink to prevent compiler optimizations
        private object _sink;

        #region AllocAssert Tests

        [Test]
        public void AllocAssert_DetectsAllocation()
        {
            // This test should FAIL to prove AllocAssert.NoAlloc works
            Assert.Throws<AssertionException>(() =>
            {
                AllocAssert.NoAlloc(() => _sink = new object());
            });
        }

        #endregion

        #region ObjectPool Tests

        [Test]
        public void Pool_Prewarm_CreatesInactiveItems()
        {
            int createCallCount = 0;
            var pool = new ObjectPool<object>(
                create: () => { createCallCount++; return new object(); },
                prewarm: 5
            );

            Assert.AreEqual(5, pool.CountAll, "CountAll should be 5 after prewarm");
            Assert.AreEqual(5, pool.CountInactive, "CountInactive should be 5 after prewarm");
            Assert.AreEqual(5, createCallCount, "Create callback should be called exactly 5 times");
        }

        [Test]
        public void Pool_GetAfterRelease_ReusesInstance()
        {
            int createCallCount = 0;
            var pool = new ObjectPool<object>(() => { createCallCount++; return new object(); });

            var first = pool.Get();
            pool.Release(first);
            var second = pool.Get();

            Assert.AreSame(first, second, "Should reuse the same instance");
            Assert.AreEqual(1, createCallCount, "Create should be called only once");
        }

        [Test]
        public void Pool_CallsOnGetAndOnRelease()
        {
            var getCallHistory = new List<object>();
            var releaseCallHistory = new List<object>();

            var pool = new ObjectPool<object>(
                create: () => new object(),
                onGet: obj => getCallHistory.Add(obj),
                onRelease: obj => releaseCallHistory.Add(obj)
            );

            var item = pool.Get();
            Assert.AreEqual(1, getCallHistory.Count, "onGet should be called once");
            Assert.AreSame(item, getCallHistory[0], "onGet should receive the returned item");

            pool.Release(item);
            Assert.AreEqual(1, releaseCallHistory.Count, "onRelease should be called once");
            Assert.AreSame(item, releaseCallHistory[0], "onRelease should receive the released item");
        }

        [Test]
        public void Pool_DoubleRelease_Throws()
        {
            var pool = new ObjectPool<object>(() => new object());
            var item = pool.Get();

            pool.Release(item);

            Assert.Throws<InvalidOperationException>(() => pool.Release(item),
                "Double release should throw InvalidOperationException");
        }

        [Test]
        public void Pool_GetRelease_DoesNotAllocateAfterWarmup()
        {
            var pool = new ObjectPool<object>(() => new object(), prewarm: 16);

            // Warm up: Get and Release all items to initialize internal structures
            var items = new object[16];
            for (int i = 0; i < 16; i++)
            {
                items[i] = pool.Get();
            }
            for (int i = 0; i < 16; i++)
            {
                pool.Release(items[i]);
            }

            // Measure: no allocations during Get/Release cycles
            AllocAssert.NoAlloc(() =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    var item = pool.Get();
                    pool.Release(item);
                }
            });
        }

        #endregion

        #region TimeSlicedQueue Tests

        [Test]
        public void Queue_Process_RespectsBudgetAndOrder()
        {
            var queue = new TimeSlicedQueue<int>();
            var processed = new List<int>();

            // Enqueue 0..9
            for (int i = 0; i < 10; i++)
            {
                queue.Enqueue(i);
            }

            Assert.AreEqual(10, queue.Count, "Queue should have 10 items");

            // Process with budget of 4
            int processedCount = queue.Process(4, item => processed.Add(item));

            Assert.AreEqual(4, processedCount, "Should process exactly 4 items");
            Assert.AreEqual(6, queue.Count, "Queue should have 6 items remaining");
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, processed, "Should process items in FIFO order");

            // Process remaining with large budget
            processed.Clear();
            processedCount = queue.Process(100, item => processed.Add(item));

            Assert.AreEqual(6, processedCount, "Should process remaining 6 items");
            Assert.AreEqual(0, queue.Count, "Queue should be empty");
            CollectionAssert.AreEqual(new[] { 4, 5, 6, 7, 8, 9 }, processed, "Should continue in FIFO order");
        }

        [Test]
        public void Queue_Process_DoesNotAllocate()
        {
            var queue = new TimeSlicedQueue<int>();
            var sum = 0;
            Action<int> handler = item => sum += item;

            // Warm up: enqueue and process 1000 items to initialize internal structures
            for (int i = 0; i < 1000; i++)
            {
                queue.Enqueue(i);
            }
            queue.Process(1000, handler);

            // Measure: no allocations during enqueue + process cycles
            AllocAssert.NoAlloc(() =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    queue.Enqueue(i);
                }
                queue.Process(1000, handler);
            });
        }

        #endregion
    }
}
