# Băng truyền (Conveyor Belt) — Design

**Status:** approved in chat (2026-10-07). This spec replaces the rule "a tray tank shoots the lowest cell of a column" from `docs/superpowers/plans/2026-10-06-pixel-flow-game.md` (section "Luật chơi đã chốt"). Everything else in that plan (layers, Global Constraints, performance rules) still applies.

**Reference:** the user's screenshot of Pixel Flow: the belt runs around the board, a "5/5" counter sits at the belt entrance, 5 empty waiting slots sit below the board, and the supply lanes sit below the slots.

## 1. Game rules

### Belt geometry
- The board is `W × H`. Row `y = 0` is the bottom row.
- The belt is a loop of `L = 2W + 2H` discrete **positions**, one per edge column or row. Positions are numbered from the bottom-left and run counter-clockwise on screen (right along the bottom, up the right side, left along the top, down the left side):

| Positions `p` | Edge | Line | Fires | Hits |
|---|---|---|---|---|
| `0 … W-1` | bottom | column `x = p` | up | the lowest non-empty cell of column x |
| `W … W+H-1` | right | row `y = p - W` | left | the rightmost non-empty cell of row y |
| `W+H … 2W+H-1` | top | column `x = W-1-(p-W-H)` | down | the highest non-empty cell of column x |
| `2W+H … L-1` | left | row `y = H-1-(p-2W-H)` | right | the leftmost non-empty cell of row y |

- The cell a position hits is called that position's **front cell**. It is the first non-empty cell along the line, so empty cells in between are skipped. A position has no front cell when its whole line is empty.

### Flow
1. **Launch.**
   - The player taps the front tank of a supply lane, or any tank sitting in a waiting slot.
   - The launch is accepted only if `tanks on belt + tanks queued at entrance < beltCapacity`, where `beltCapacity = LevelData.slotCount` (5).
   - A launched slot tank leaves its slot immediately, and the remaining slot tanks shift left.
   - A rejected tap changes nothing.
2. **Entrance queue.** A launched tank waits in the entrance queue (FIFO). On a tick, the first queued tank enters at position 0 only if no belt tank is at position 0. The "x/5" counter shows `onBelt + queued`.
3. **Tick.** Every `tickInterval` seconds, belt tanks are processed in entry order:
   - (a) **Fire.** If the tank's current position has a front cell and that cell's colour equals the tank's colour, the cell is removed, 1 ammo is consumed, and a `ShotEvent` is emitted.
   - (b) **Depleted.** If the tank has no ammo left, it leaves the belt.
   - (c) **Count.** Otherwise `visited += 1`. Step (a) counts as a visit whether or not the tank fired.
   - (d) **Lap done.** If `visited == L`, the tank has tried every position once and leaves the belt with ammo left. It goes to the first free waiting slot. If all slots are full, the result is **Lost**.
   - (e) **Advance.** Otherwise the tank moves to `pos + 1`. A tank enters at `pos = 0` with `visited = 0`.
   - After all belt tanks are processed, the entrance queue may admit one tank at position 0, following rule 2.
4. **Speed.** One lap takes `lapSeconds = 4` seconds on every board size, so `tickInterval = lapSeconds / L`:
   - 8×8 board: 32 positions, 125 ms per tick.
   - 64×64 board: 256 positions, 15.6 ms per tick.

   The controller catches up with as many ticks per frame as needed, capped at 16.

### End states
- **Won:** `RemainingCount == 0`.
- **Lost (overflow):** a tank finishes its lap with ammo left while every waiting slot is full. This result is latched immediately.
- **Lost (stuck):** all of the following are true at the same time: the supply is empty, the belt and the entrance queue are empty, the waiting slots are non-empty, and no slot tank's colour is the colour of any front cell on any position.
  - Also Lost: the supply, belt, queue and slots are all empty while pixels remain. Generated levels should never reach this case.
- The win/lose popup appears only after projectiles, the destroy queue and the shrinking cells have all finished. This is unchanged from the current game.

## 2. Code changes

### Core (`Assets/PixelFlow/Scripts/Core/`)
- **`PixelGridModel`**
  - Replace the bottom-only exposure with four-sided fronts:
    - `enum BoardSide { Bottom, Right, Top, Left }`.
    - `bool TryGetFront(BoardSide side, int lineIndex, out Vector2Int cell)`, where `lineIndex` is the column for Bottom and Top and the row for Left and Right.
    - `bool HasAnyFront(int colorId)`: true if any of the `2W + 2H` fronts has this colour.
  - Front indices are four arrays.
    - When a removed cell was the front of a side's line, that pointer moves inward past empty cells. This is amortised O(1).
    - A per-colour front-occurrence counter keeps `HasAnyFront` O(1).
  - `RemoveCell(cell)` accepts any cell that is currently a front from at least one side. Any other cell throws `InvalidOperationException`.
  - `GetCellsOfColor` stays (`Dictionary<int, List<Vector2Int>>`).
  - Everything is allocation-free after construction.
  - Remove the old `TryGetExposedCell` and `HasExposed` and their exposed-column sets.
- **`BeltPath`** (pure struct or static helper): `L`, and `(BoardSide side, int lineIndex) Resolve(int position)` per the table above.
- **`BeltModel`**:
  - Constructor `(int capacity, int lapLength)`.
  - Tracks belt tanks (tank, position, positions visited) and the entrance queue.
  - Methods:
    - `bool TryLaunch(ColorTankModel)`.
    - `int Count`.
    - `int QueuedCount`.
    - `bool IsFull`.
    - Read-only access to `TankAt(i)` and `PositionOf(tank)` for the view.
  - Events:
    - `OnTankEntered(tank)`.
    - `OnTankMoved(tank, position)`.
    - `OnTankLeft(tank, bool lapCompleted)`.
- **`ShootingLogic`** is rewritten for the belt:
  - Constructor `(PixelGridModel grid, BeltModel belt, SlotQueueManager waitingSlots)`.
  - `int Tick(List<ShotEvent> output)` implements Flow step 3. `ShotEvent` keeps `Tank` and `Cell` and adds `BeltPosition`, replacing `SlotIndex`.
  - It exposes `bool Overflowed` for the latched overflow loss.
  - `Tick` must not allocate. Tests must pin this with `AllocAssert.NoAlloc`.
- **`SlotQueueManager`** becomes the waiting slots.
  - Same class with capacity `slotCount`.
  - Tanks arrive from `ShootingLogic` when a lap completes.
  - It gets `bool TryRemove(ColorTankModel)`, which shifts left. That is the launch path for slot tanks.
  - Automatic removal on depletion is no longer needed, because slot tanks don't fire, but it may stay.
- **`GameRules.Evaluate`**: implements the end states above.
- **`LevelSession`**: builds `Belt`, `Slots`, `Supply`, `Shooting` and `Grid`.
- **`TankGenerator`**: the peel simulation becomes a belt sweep.
  - Each round visits positions `0 … L-1` and removes each position's front cell. A cell already removed in the same round is skipped. Each removed cell gets the next peel time `t`.
  - Chunking and sorting are unchanged.

### Levels (`Assets/PixelFlow/Editor/`)
- Regenerate all 6 levels with the new generator. The pictures and the table values stay the same.
- Test helper `AutoPlayer` follows the new rules. Each tick:
  - (1) If the belt plus queue is below capacity, launch the first waiting-slot tank whose colour `HasAnyFront`.
  - (2) Otherwise, if still below capacity, launch the lane-front tank with the smallest `Id`.
  - (3) Then `Tick`.
  - Stop when the state is not `Playing`, bounded by `maxTicks`.
- **Solvability requirement:** `AutoPlayer` must win all 6 levels with each level's own lanes and slots.
  - If a level fails, fix the generator ordering, or change that level's lanes or `ammoPerTank` with a documented reason.
  - Never weaken the test.

### View (`Assets/PixelFlow/Scripts/View/`)
- **`BeltView`** (new):
  - Draws the track as a rounded rectangle around the board, one track-width outside it, with chevron arrows showing the direction.
  - Puts an "x/5" TMP counter at the entrance (bottom-left).
  - `Vector3 PositionToWorld(int position)` gives the track point next to that position's line.
  - Belt tanks move smoothly between ticks. At corners they pass through the corner point instead of cutting diagonally.
- **Board layout:** `boardArea` shrinks so the board, the track and the counter fit above the waiting slots in portrait.
- **`TankBoardView`:**
  - Owns tank views across supply lanes, the entrance queue, the belt and the waiting slots.
  - Draws 5 empty slot frames below the belt.
  - A tank that leaves the belt goes to its waiting slot, or plays its depletion shrink.
  - `TryGetTank` resolves both supply-lane tanks and waiting-slot tanks.
- **Projectiles:** fly from the tank's belt world position to the cell. This is the existing ProjectileSystem.
- Scale tanks on the belt so they fit the track width.

### Controller (`Assets/PixelFlow/Scripts/Controller/GameController.cs`)
- The belt tick timer replaces the fire timer. It has a serialized `lapSeconds = 4f` with `[Min(0.5f)]`. The tick interval is computed per level.
- **Tap handling:**
  - A lane-front tank calls `Supply.TryTakeFront` and then `Belt.TryLaunch`. Check capacity first so no tank is lost.
  - A waiting-slot tank calls `Slots.TryRemove` and then `Belt.TryLaunch`, again checking capacity first.
- Replace the test hook `TryActivateLane(int lane)` with `TryLaunchFromLane(int lane)` and add `TryLaunchFromSlot(int slot)`. Update the existing PlayMode tests.

## 3. Tests

### EditMode
- **`PixelGridModel`**
  - Fronts from all 4 sides on a small board, including gaps in a row or column and fully empty lines.
  - Removing a cell that is the front of two sides updates both.
  - `HasAnyFront` counts update correctly.
  - `RemoveCell` throws on a cell that is not a front.
  - No allocations.
- **`BeltPath`**: `Resolve` gives the exact side and line for every position on a 3×2 board (`L = 10`).
- **`BeltModel` and `ShootingLogic`**
  - The tank fires at the correct cell on each of the 4 edges.
  - It skips a mismatched colour without consuming ammo.
  - A depleted tank leaves immediately.
  - A lap with ammo left sends the tank to the first free slot.
  - Lap completion with the slots full sets `Overflowed`, and the game is `Lost`.
  - Capacity is respected, including queued tanks.
  - The entrance queue waits while position 0 is occupied.
  - A launched slot tank shifts the other slots left.
  - `Tick` does not allocate.
- **`GameRules`**: Won; Lost (overflow); Lost (stuck); Playing when a slot tank can still hit something.
- **Levels**: all 6 are solvable by `AutoPlayer`; ammo equals pixels per colour (existing tests, kept green).

### PlayMode
- Auto-play level 1 and the sun level to Won.
- Overflow: a purpose-built level reaches Lost.
- A tap on a waiting-slot tank (via a synthesized touch) relaunches it.
- Retry mid-flight leaves nothing stale.
- `Logs/belt_capture.png` mid-game for a visual check against the reference screenshot.
- The existing performance test is adapted: still 0 B GC per frame from game code and a main-thread average below 16.6 ms on the sun level.

## 4. Out of scope
"?" mystery tanks, coins, lock buttons, the settings button, belt or slot upgrades.
