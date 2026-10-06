namespace PixelFlow.Core
{
    /// <summary>
    /// Represents the current state of the game.
    /// </summary>
    public enum GameState
    {
        /// <summary>
        /// The game is still in progress.
        /// </summary>
        Playing,

        /// <summary>
        /// The player has won (all pixels cleared).
        /// </summary>
        Won,

        /// <summary>
        /// The player has lost (a waiting-slot overflow, or stuck with no useful move).
        /// </summary>
        Lost
    }

    /// <summary>
    /// Evaluates game state based on the current board and tank configuration.
    /// </summary>
    public static class GameRules
    {
        /// <summary>
        /// Evaluates the game state under the conveyor-belt rules.
        /// Lost (checked first, latched immediately): a tank overflowed the waiting slots, even if no pixels remain.
        /// Won: no pixels remain.
        /// Lost (stuck): the supply, belt and entrance queue are all empty and no waiting-slot tank's colour is on
        /// any front (this includes empty waiting slots).
        /// Playing: otherwise. Allocation-free.
        /// </summary>
        /// <param name="grid">The pixel grid model.</param>
        /// <param name="belt">The belt model (riding and queued tanks).</param>
        /// <param name="slots">The waiting slots.</param>
        /// <param name="supply">The supply model.</param>
        /// <param name="shooting">The belt shooting logic (overflow latch and waiting-tank check).</param>
        /// <returns>The current game state.</returns>
        public static GameState Evaluate(PixelGridModel grid, BeltModel belt, SlotQueueManager slots, SupplyModel supply, BeltShootingLogic shooting)
        {
            // Overflow is latched immediately, so it takes precedence over a board cleared in the same tick.
            if (shooting.Overflowed)
            {
                return GameState.Lost;
            }

            if (grid.RemainingCount == 0)
            {
                return GameState.Won;
            }

            if (supply.IsEmpty && belt.Count == 0 && belt.QueuedCount == 0 && !shooting.CanAnyWaitingTankHit())
            {
                return GameState.Lost;
            }

            return GameState.Playing;
        }
    }
}
