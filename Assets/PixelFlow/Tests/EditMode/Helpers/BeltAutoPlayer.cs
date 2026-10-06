using System.Collections.Generic;
using PixelFlow.Core;

namespace PixelFlow.Tests
{
    /// <summary>
    /// Deterministic bot used by tests to check that a level can be won under the conveyor-belt rules.
    /// </summary>
    public static class BeltAutoPlayer
    {
        /// <summary>
        /// Plays the session until it is no longer <see cref="GameState.Playing"/> or the tick budget runs out.
        /// Each tick: (1) if the belt plus queue is below capacity, launch the first waiting-slot tank whose colour
        /// has a front; (2) otherwise, if still below capacity, launch the lane-front tank with the smallest
        /// <c>Id</c>; (3) run one belt tick.
        /// </summary>
        /// <param name="s">The session to play (mutated in place).</param>
        /// <param name="maxTicks">Maximum number of ticks before giving up.</param>
        /// <returns>The final game state (<see cref="GameState.Playing"/> if the tick budget ran out).</returns>
        public static GameState Play(LevelSession s, int maxTicks = 500000)
        {
            var shots = new List<ShotEvent>(s.Belt.Capacity);
            for (int tick = 0; tick < maxTicks; tick++)
            {
                GameState state = GameRules.Evaluate(s.Grid, s.Belt, s.Tray, s.Supply, s.BeltShooting);
                if (state != GameState.Playing)
                {
                    return state;
                }

                Step(s, shots);
            }

            return GameRules.Evaluate(s.Grid, s.Belt, s.Tray, s.Supply, s.BeltShooting);
        }

        /// <summary>
        /// Runs one bot tick: (1) if the belt plus queue is below capacity, launch the first waiting-slot tank whose
        /// colour has a front; (2) otherwise, if still below capacity, launch the lane-front tank with the smallest
        /// <c>Id</c>; (3) clear <paramref name="shots"/> and run one belt tick. Allocation-free when
        /// <paramref name="shots"/> has enough capacity.
        /// </summary>
        /// <param name="s">The session to play (mutated in place).</param>
        /// <param name="shots">Scratch list that receives this tick's shots.</param>
        /// <returns>The number of shots fired this tick.</returns>
        public static int Step(LevelSession s, List<ShotEvent> shots)
        {
            if (!s.Belt.IsFull && !TryLaunchFromSlots(s))
            {
                TryLaunchFromLanes(s);
            }

            shots.Clear();
            return s.BeltShooting.Tick(shots);
        }

        private static bool TryLaunchFromSlots(LevelSession s)
        {
            for (int i = 0; i < s.Tray.Count; i++)
            {
                ColorTankModel tank = s.Tray[i];
                if (s.Grid.HasAnyFront(tank.ColorId))
                {
                    s.Tray.TryRemove(tank);
                    s.Belt.TryLaunch(tank);
                    return true;
                }
            }

            return false;
        }

        private static void TryLaunchFromLanes(LevelSession s)
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

            if (bestLane >= 0 && s.Supply.TryTakeFront(bestLane, out ColorTankModel tank))
            {
                s.Belt.TryLaunch(tank);
            }
        }
    }
}
