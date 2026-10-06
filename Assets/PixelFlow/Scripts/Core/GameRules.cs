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
        /// The player has lost (stuck with no valid moves).
        /// </summary>
        Lost
    }

    /// <summary>
    /// Evaluates game state based on the current board and tank configuration.
    /// </summary>
    public static class GameRules
    {
        /// <summary>
        /// Evaluates the current game state.
        /// Win: No pixels remain.
        /// Lose: Pixels remain, no tank can fire, and (tray full OR supply empty).
        /// Playing: Otherwise.
        /// </summary>
        /// <param name="grid">The pixel grid model.</param>
        /// <param name="tray">The slot queue manager.</param>
        /// <param name="supply">The supply model.</param>
        /// <param name="shooting">The shooting logic.</param>
        /// <returns>The current game state.</returns>
        public static GameState Evaluate(PixelGridModel grid, SlotQueueManager tray, SupplyModel supply, ShootingLogic shooting)
        {
            // Win condition: no pixels remain
            if (grid.RemainingCount == 0)
            {
                return GameState.Won;
            }

            // Check if any tank can fire
            bool canAnyFire = shooting.CanAnyTankFire();

            // Lose condition: pixels remain, no tank can fire, and (tray full OR supply empty)
            if (!canAnyFire && (tray.IsFull || supply.IsEmpty))
            {
                return GameState.Lost;
            }

            // Otherwise, still playing
            return GameState.Playing;
        }

        /// <summary>
        /// Evaluates the game state under the conveyor-belt rules.
        /// Won: no pixels remain.
        /// Lost: a tank overflowed the waiting slots, or the supply, belt and entrance queue are all empty and no
        /// waiting-slot tank's colour is on any front (this includes empty waiting slots).
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
            if (grid.RemainingCount == 0)
            {
                return GameState.Won;
            }

            if (shooting.Overflowed)
            {
                return GameState.Lost;
            }

            if (supply.IsEmpty && belt.Count == 0 && belt.QueuedCount == 0 && !shooting.CanAnyWaitingTankHit())
            {
                return GameState.Lost;
            }

            return GameState.Playing;
        }
    }
}
