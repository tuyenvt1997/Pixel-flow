using System.Collections.Generic;
using PixelFlow.Core;

namespace PixelFlow.Tests
{
    /// <summary>
    /// Deterministic bot used by tests to check that a level can be won with a simple greedy strategy.
    /// </summary>
    public static class AutoPlayer
    {
        /// <summary>
        /// Plays the session until it is no longer <see cref="GameState.Playing"/> or the step budget runs out.
        /// Each step: if any tray tank can fire, run one shooting step; otherwise, if the tray is not full,
        /// move the lane-front tank with the smallest <c>Id</c> into the tray.
        /// </summary>
        /// <param name="s">The session to play (mutated in place).</param>
        /// <param name="maxSteps">Maximum number of loop iterations before giving up.</param>
        /// <returns>The final game state (<see cref="GameState.Playing"/> if the step budget ran out).</returns>
        public static GameState Play(LevelSession s, int maxSteps = 100000)
        {
            var shots = new List<ShotEvent>(s.Tray.Capacity);
            for (int step = 0; step < maxSteps; step++)
            {
                GameState state = GameRules.Evaluate(s.Grid, s.Tray, s.Supply, s.Shooting);
                if (state != GameState.Playing)
                {
                    return state;
                }

                if (s.Shooting.CanAnyTankFire())
                {
                    shots.Clear();
                    s.Shooting.Step(shots);
                }
                else if (!s.Tray.IsFull)
                {
                    int bestLane = -1;
                    int bestId = int.MaxValue;
                    for (int lane = 0; lane < s.Supply.LaneCount; lane++)
                    {
                        ColorTankModel front = s.Supply.PeekFront(lane);
                        if (front != null && front.Id < bestId)
                        {
                            bestId = front.Id;
                            bestLane = lane;
                        }
                    }

                    // Evaluate reports Lost when nothing can fire and supply is empty, so a lane front exists here.
                    s.Supply.TryTakeFront(bestLane, out ColorTankModel tank);
                    s.Tray.TryAdd(tank);
                }
            }

            return GameRules.Evaluate(s.Grid, s.Tray, s.Supply, s.Shooting);
        }
    }
}
