using System;
using System.Reflection;
using NUnit.Framework;
using PixelFlow.Core;
using PixelFlow.View;
using UnityEngine;

namespace PixelFlow.Tests
{
    /// <summary>
    /// Tests for the belt motion maths: the tick-clock interpolation of belt tanks
    /// (<see cref="TankBoardView.InterpolateBeltPosition"/>), the constant-speed rounded-corner path of
    /// <see cref="BeltView.PositionToWorld"/> and the belt clock that drives the tank phase and the chevron scroll.
    /// </summary>
    public class BeltMotionTests
    {
        private const float Eps = 1e-4f;
        private const int BoardSize = 8;

        private GameObject _go;
        private BeltView _belt;
        private BoardLayout _layout;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("BeltMotionTest");
            _belt = _go.AddComponent<BeltView>();
            _layout = BoardLayout.Fit(BoardSize, BoardSize, new Rect(-4f, -1.2f, 8f, 8f));
            _belt.Build(new BeltPath(BoardSize, BoardSize), _layout);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_go);
        }

        [Test]
        public void Interpolate_IsMonotonicInPhase_AndNeverPassesTarget()
        {
            for (int target = 0; target < 6; target++)
            {
                float previous = float.MinValue;
                for (int i = 0; i <= 100; i++)
                {
                    float position = TankBoardView.InterpolateBeltPosition(target, i / 100f);
                    Assert.GreaterOrEqual(position, previous, $"Backward step at target {target}, phase {i / 100f}.");
                    Assert.LessOrEqual(position, target, $"Overshoot at target {target}, phase {i / 100f}.");
                    previous = position;
                }
            }
        }

        [Test]
        public void Interpolate_MovesOnePositionPerTick_AndIsContinuousAcrossTicks()
        {
            for (int target = 1; target < 6; target++)
            {
                Assert.AreEqual(target - 1f, TankBoardView.InterpolateBeltPosition(target, 0f), Eps);
                Assert.AreEqual(target - 0.5f, TankBoardView.InterpolateBeltPosition(target, 0.5f), Eps);
                Assert.AreEqual(TankBoardView.InterpolateBeltPosition(target + 1, 0f),
                    TankBoardView.InterpolateBeltPosition(target, 1f), Eps, "Jump at the tick boundary.");
            }
        }

        [Test]
        public void Interpolate_JustEntered_StartsAtEntranceCorner()
        {
            Assert.AreEqual(TankBoardView.EntrancePositionOffset, TankBoardView.InterpolateBeltPosition(0, 0f), Eps);
            Assert.AreEqual(TankBoardView.EntrancePositionOffset, TankBoardView.InterpolateBeltPosition(0, 0.4f), Eps);
            Assert.AreEqual(-0.25f, TankBoardView.InterpolateBeltPosition(0, 0.75f), Eps);
            Assert.AreEqual(TankBoardView.InterpolateBeltPosition(1, 0f), TankBoardView.InterpolateBeltPosition(0, 1f),
                Eps, "Jump from the first tick to the second.");
        }

        [Test]
        public void Interpolate_FrozenPhase_HoldsStill()
        {
            float a = TankBoardView.InterpolateBeltPosition(7, 0.37f);
            float b = TankBoardView.InterpolateBeltPosition(7, 0.37f);
            Assert.AreEqual(a, b);
            Assert.AreEqual(TankBoardView.InterpolateBeltPosition(7, 1f), TankBoardView.InterpolateBeltPosition(7, 3f),
                "A phase beyond one tick must not run the tank past its model position.");
        }

        [Test]
        public void PositionToWorld_IntegerPositions_SitNextToTheirLine()
        {
            float half = _layout.CellSize * 0.5f;
            float bottom = _layout.Origin.y - _belt.TrackWidth;
            float right = _layout.Origin.x + BoardSize * _layout.CellSize + _belt.TrackWidth;
            for (int column = 0; column < BoardSize; column++)
            {
                Vector3 p = _belt.PositionToWorld(column);
                Assert.AreEqual(_layout.Origin.x + column * _layout.CellSize + half, p.x, Eps);
                Assert.AreEqual(bottom, p.y, Eps);
            }

            Vector3 firstRight = _belt.PositionToWorld(BoardSize);
            Assert.AreEqual(right, firstRight.x, Eps);
            Assert.AreEqual(_layout.Origin.y + half, firstRight.y, Eps);
        }

        [Test]
        public void PositionToWorld_ConstantSpeedWithinEachStep_AndStrictlyProgressing()
        {
            const int samples = 20;
            int length = _belt.Length;
            for (int k = 0; k < length; k++)
            {
                float first = -1f;
                for (int i = 0; i < samples; i++)
                {
                    float step = Vector3.Distance(_belt.PositionToWorld(k + (float)i / samples),
                        _belt.PositionToWorld(k + (float)(i + 1) / samples));
                    Assert.Greater(step, 0f, $"No progress in step {k}.");
                    if (first < 0f)
                        first = step;
                    // Chords of the corner arc are slightly shorter than the arc; 2 % covers that.
                    Assert.AreEqual(first, step, first * 0.02f, $"Speed changes within step {k}.");
                }
            }

            float straight = Vector3.Distance(_belt.PositionToWorld(1f), _belt.PositionToWorld(2f));
            Assert.AreEqual(_layout.CellSize, straight, Eps, "Straight-edge step is not one cell.");
        }

        [Test]
        public void PositionToWorld_EntranceCorner_IsOnTheRoundedArc()
        {
            float radius = _belt.TrackWidth * 0.7f;
            float left = _layout.Origin.x - _belt.TrackWidth;
            float bottom = _layout.Origin.y - _belt.TrackWidth;
            var centre = new Vector3(left + radius, bottom + radius, 0f);
            Vector3 entrance = _belt.PositionToWorld(TankBoardView.EntrancePositionOffset);
            Assert.AreEqual(radius, Vector3.Distance(centre, entrance), Eps);
            Assert.AreEqual(entrance, _belt.PositionToWorld(_belt.Length + TankBoardView.EntrancePositionOffset));
            Assert.Less(entrance.x, centre.x);
            Assert.Less(entrance.y, centre.y);
        }

        [Test]
        public void SetBeltClock_SetsPhase_AndScrollsChevronsByCellsTravelled()
        {
            Assert.Greater(_belt.ChevronCount, 4);
            Assert.AreEqual(0f, _belt.ChevronScroll);

            _belt.SetBeltClock(0.25f);
            Assert.AreEqual(0.25f, _belt.Phase, Eps);
            Assert.AreEqual(0.25f * _layout.CellSize, _belt.ChevronScroll, Eps);

            _belt.SetBeltClock(0.25f);
            Assert.AreEqual(0.25f * _layout.CellSize, _belt.ChevronScroll, Eps, "A frozen clock moved the chevrons.");

            // Wrapping from the end of the lap to its start keeps moving forward.
            _belt.SetBeltClock(_belt.Length - 0.05f);
            float before = _belt.ChevronScroll;
            _belt.SetBeltClock(0.05f);
            Assert.AreEqual(0.05f, _belt.Phase, Eps);
            float expected = Mathf.Repeat(before + 0.1f * _layout.CellSize, _belt.ChevronStep);
            Assert.AreEqual(expected, _belt.ChevronScroll, 1e-3f);
        }

        [Test]
        public void PerFrameBeltMotion_AllocatesNothing()
        {
            MethodInfo method = typeof(BeltView).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "BeltView.LateUpdate not found.");
            var lateUpdate = (Action)Delegate.CreateDelegate(typeof(Action), _belt, method);
            float clock = 0f;
            float sink = 0f;

            AllocAssert.NoAlloc(() =>
            {
                // One frame of belt motion: the controller pushes the clock, the tank board interpolates a rider and
                // maps it to world, the belt rewrites the chevron vertices.
                clock = Mathf.Repeat(clock + 0.3f, _belt.Length);
                _belt.SetBeltClock(clock);
                float display = TankBoardView.InterpolateBeltPosition(5, _belt.Phase);
                sink += _belt.PositionToWorld(display).x;
                lateUpdate();
            });
            Assert.IsFalse(float.IsNaN(sink));
        }

        [Test]
        public void Build_ResetsBeltClock()
        {
            _belt.SetBeltClock(3.5f);
            _belt.Build(new BeltPath(BoardSize, BoardSize), _layout);
            Assert.AreEqual(0f, _belt.Clock);
            Assert.AreEqual(0f, _belt.ChevronScroll);
        }
    }
}
