# Conveyor Belt Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the bottom-only tray shooting with a conveyor belt around the board. Tanks ride the belt, shoot inward from all 4 sides, and drop into 5 waiting slots after a lap.

**Architecture:**
- The belt is a discrete loop of `2W+2H` positions in Core, which is pure C# and tested in EditMode.
- `PixelGridModel` gains four-sided front lookup.
- A new `BeltModel` and `BeltShootingLogic` drive ticks.
- The view interpolates belt tanks smoothly.
- The new Core types are added **next to** the old ones (Tasks 1–4) so the project keeps compiling. Task 5 switches View and Controller over and deletes the old tray-shooting code.

**Tech Stack:** Unity 6000.0.84f1, URP 17 (2D Renderer), Input System, TextMeshPro, Unity Test Framework (EditMode + PlayMode).

**Spec:** `docs/superpowers/specs/2026-10-07-conveyor-belt-design.md`. Read it with this plan; its section 1 is the binding rule set. The base project plan, `docs/superpowers/plans/2026-10-06-pixel-flow-game.md`, still governs layers and Global Constraints.

## Global Constraints

- **Layering:** layers stay one-way (Data ← Core ← View ← Controller; Performance has no dependencies). Core, Data and Performance contain no MonoBehaviour and no UnityEditor code. Logic talks to View only through C# events.
- **No allocations** in `Tick`, `TryGetFront`, `HasAnyFront`, `Evaluate`, or any `Update`. Pin each one with `AllocAssert.NoAlloc`.
- **No `Instantiate`/`Destroy`** in the gameplay loop; use the existing `ObjectPool<T>`.
- **Belt numbers:** belt length `L = 2W + 2H`, `beltCapacity = LevelData.slotCount` (5), `lapSeconds = 4f` (`[Min(0.5f)]`), `tickInterval = lapSeconds / L`, at most **16** ticks per frame.
- **Position → line mapping:** exactly the table in spec §1. Position 0 is bottom-left, under column 0.
- **Docs:** every public member has an English XML doc comment.
- **Tests:**
  - Run with the CLI batchmode commands in **Running Unity** below.
  - If the Unity Editor is open on the project, batchmode refuses to start. In that case run tests inside the editor through the Unity MCP tools (`Unity_RunCommand` with `TestRunnerApi`), and never commit temporary helpers.
  - PlayMode runs **with graphics** (no `-nographics`).
- **Git:**
  - Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
  - Commit the `.meta` files for new files.
  - Never commit `Library/`, `Temp/`, `Logs/`, `ProjectSettings/SceneTemplateSettings.json`, a `SENTIS_ANALYTICS_ENABLED` define change, or the stray `a6d8e976-….jpg`.

## Running Unity

```bash
cd "E:/unity/game 1/pixel flow"; U="/e/unity/6000.0.84f1/Editor/Unity.exe"
"$U" -batchmode -nographics -projectPath "E:/unity/game 1/pixel flow" -runTests -testPlatform EditMode -testFilter "<filter>" -testResults "E:/unity/game 1/pixel flow/Logs/editmode.xml" -logFile "E:/unity/game 1/pixel flow/Logs/batch.log"; echo "exit=$?"
"$U" -batchmode -projectPath "E:/unity/game 1/pixel flow" -runTests -testPlatform PlayMode -testResults "E:/unity/game 1/pixel flow/Logs/playmode.xml" -logFile "E:/unity/game 1/pixel flow/Logs/playmode.log"; echo "exit=$?"
"$U" -batchmode -nographics -projectPath "E:/unity/game 1/pixel flow" -executeMethod <Namespace.Class.Method> -quit -logFile "E:/unity/game 1/pixel flow/Logs/exec.log"
grep -E "error CS|warning CS" Logs/*.log | sort -u; grep -oE 'test-run [^>]*' Logs/editmode.xml
```
A run passes when it exits 0, the result is `Passed`, and there is no `error CS`/`warning CS`. Licensing 404 lines are noise. `RUN <filter>` below means an EditMode run with that filter.

## Review Focus

1. **Entrance collision.** A tank is launched while another belt tank sits at position 0. It must wait in the queue, not overlap. Pinned in Task 2: `Queue_WaitsWhilePositionZeroOccupied`.
2. **Corner cells.** A cell that is the front of two sides at once (e.g. `(0,0)` is the bottom front of column 0 and the left front of row 0) is removed from one side. Both sides must advance. Pinned in Task 1: `RemoveCell_CornerFront_UpdatesBothSides`.
3. **Depleting on the last lap position.** A tank uses its last ammo exactly at position `L-1`. It must leave as depleted, not go to a slot. Pinned in Task 3: `Tick_DepletesOnLastPosition_DoesNotGoToSlot`.
4. **Relaunch from a slot while the belt is full.** The tank must stay in its slot. Pinned in Task 5: `TryLaunchFromSlot_BeltFull_ReturnsFalseAndKeepsSlot`. Controller code checks capacity before `TryRemove`.
5. **Retry or Next while tanks are on the belt or queued.** Nothing stale may remain (belt views, queued tanks, projectiles). Pinned in Task 5: `Retry_WithTanksOnBelt_ClearsEverything`.

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `Scripts/Core/PixelGridModel.cs` | modify | add four-sided fronts (T1); delete old bottom-only exposure (T5) |
| `Scripts/Core/BoardSide.cs` | create | `enum BoardSide { Bottom, Right, Top, Left }` |
| `Scripts/Core/BeltPath.cs` | create | position → (side, line) and lap length |
| `Scripts/Core/BeltModel.cs` | create | belt tanks, positions, visit counts, entrance queue, capacity, events |
| `Scripts/Core/BeltShootingLogic.cs` | create | `Tick` per spec §1 Flow 3; overflow latch |
| `Scripts/Core/ShotEvent.cs` | modify | add `BeltPosition` (T3); drop `SlotIndex` (T5) |
| `Scripts/Core/SlotQueueManager.cs` | modify | add `TryRemove` (waiting slots) |
| `Scripts/Core/GameRules.cs` | modify | add belt `Evaluate` overload (T3); delete old one (T5) |
| `Scripts/Core/LevelSession.cs` | modify | add `Belt`, `BeltShooting`; `Tray` becomes the waiting slots |
| `Scripts/Core/TankGenerator.cs` | modify | belt-sweep peel |
| `Scripts/Core/ShootingLogic.cs` | delete (T5) | old tray shooting |
| `Tests/EditMode/Helpers/BeltAutoPlayer.cs` | create | auto-play policy per spec §2 Levels |
| `Scripts/View/BeltView.cs` | create | track mesh/sprites, arrows, counter, `PositionToWorld` |
| `Scripts/View/TankBoardView.cs` | modify | tank views across lanes, queue, belt, slots; slot frames |
| `Scripts/Controller/GameController.cs` | modify | belt tick timer, tap on lane/slot, new test hooks |
| `Editor/PixelFlowSceneBuilder.cs` | modify | belt track, slot frames, new layout |
| `Editor/StarterLevelFactory.cs`, `Levels/*.asset` | regenerate | new tank order |

---

### Task 1: Four-sided fronts in `PixelGridModel`

**Files:**
- Create: `Assets/PixelFlow/Scripts/Core/BoardSide.cs`
- Modify: `Assets/PixelFlow/Scripts/Core/PixelGridModel.cs` (additive: the old `TryGetExposedCell`/`HasExposed`/`RemoveCell` behaviour stays until Task 5)
- Test: `Assets/PixelFlow/Tests/EditMode/PixelGridFrontTests.cs`

**Interfaces:**
- Produces:
  - `public enum BoardSide { Bottom, Right, Top, Left }`
  - `bool PixelGridModel.TryGetFront(BoardSide side, int lineIndex, out Vector2Int cell)`. `lineIndex` is the column for Bottom/Top and the row for Left/Right.
  - `bool PixelGridModel.HasAnyFront(int colorId)`
  - `RemoveCell(Vector2Int)` now accepts any cell that is a front from at least one side, and throws `InvalidOperationException` otherwise. Every front pointer and the per-colour front counts update; the old bottom exposure sets update too.

- [ ] **Step 1: Write failing tests** (use `TestLevels.Create`; `rows[0]` is the top row)
  - `TryGetFront_AllSides_FindFirstNonEmptyCell`, using
    ```
    ".1."
    "2.3"
    ".0."
    ```
    - Bottom col 0 → (0,1); Bottom col 1 → (1,0).
    - Top col 1 → (1,2); Top col 2 → (2,1).
    - Left row 1 → (0,1); Right row 1 → (2,1).
    - Left row 0 → (1,0).
  - `TryGetFront_EmptyLine_ReturnsFalse`: a fully empty column gives false for Bottom and Top.
  - `RemoveCell_CornerFront_UpdatesBothSides`, using `{"00","00"}`:
    - Remove (0,0). Then Bottom col 0 → (0,1) and Left row 0 → (1,0).
  - `RemoveCell_SkipsGaps_FromRight`, using `{"0.1"}`:
    - Remove (2,0). Then Right row 0 → (0,0).
  - `HasAnyFront_TracksColours`:
    - Colour 1 buried in the centre of a 3×3 ring of colour 0 → false.
    - After removing the ring cells above it → true.
  - `RemoveCell_NotAFront_Throws`: removing the centre of a full 3×3 throws `InvalidOperationException`.
  - `TryGetFront_And_HasAnyFront_DoNotAllocate`: 64×64 random grid, warm-up, then `AllocAssert.NoAlloc`.
- [ ] **Step 2:** `RUN PixelFlow.Tests.PixelGridFrontTests` → FAIL (compile error: `BoardSide` and `TryGetFront` don't exist yet).
- [ ] **Step 3: Implement.**
  - Four `int[]` front arrays (`bottom[x]` = min y, `top[x]` = max y, `left[y]` = min x, `right[y]` = max x, `-1` when the line is empty).
  - An `int[256]` per-colour front-occurrence count.
  - On removal, advance each pointer that pointed at the removed cell, skipping empty cells inward, and update the counts.
- [ ] **Step 4:** `RUN PixelFlow.Tests.PixelGridFrontTests` and `RUN PixelFlow.Tests.PixelGridModelTests` → PASS. The old tests must stay green.
- [ ] **Step 5: Commit:** `feat(core): four-sided front lookup in PixelGridModel`

---

### Task 2: `BeltPath` and `BeltModel`

**Files:**
- Create: `Assets/PixelFlow/Scripts/Core/BeltPath.cs`, `Assets/PixelFlow/Scripts/Core/BeltModel.cs`
- Test: `Assets/PixelFlow/Tests/EditMode/BeltModelTests.cs`

**Interfaces:**
- Consumes: `BoardSide` (T1), `ColorTankModel`.
- Produces:
  - `public readonly struct BeltPath`
    - ctor `(int width, int height)`
    - `int Length` (= `2W + 2H`)
    - `void Resolve(int position, out BoardSide side, out int lineIndex)`, which throws `ArgumentOutOfRangeException` outside `[0, Length)`.
  - `public sealed class BeltModel`
    - ctor `(int capacity, BeltPath path)`
    - `int Capacity`, `int Count` (on belt), `int QueuedCount`, `bool IsFull` (`Count + QueuedCount >= Capacity`)
    - `bool TryLaunch(ColorTankModel tank)`: enqueues the tank. Returns false when full. Throws on null, on a depleted tank, or on a tank already on the belt or in the queue.
    - `ColorTankModel TankAt(int index)` (entry order), `int PositionAt(int index)`, `int VisitedAt(int index)`
    - `bool TryGetPosition(ColorTankModel tank, out int position)`
    - `internal`/`public` mutation used only by `BeltShootingLogic`: `void MarkVisited(int index)`, `void Advance(int index)`, `void RemoveAt(int index, bool lapCompleted)`, `bool TryAdmitFromQueue()` (admits the queue head at position 0 if no belt tank is at 0).
    - `void Clear()`
    - Events: `event Action<ColorTankModel> OnTankQueued`, `OnTankEntered`; `event Action<ColorTankModel, int> OnTankMoved` (tank, new position); `event Action<ColorTankModel, bool> OnTankLeft` (tank, lapCompleted).
  - Fixed-size arrays and a ring buffer for the queue, so nothing allocates after construction.

- [ ] **Step 1: Write failing tests**
  - `Resolve_3x2_MapsEveryPosition`. With W=3, H=2, `Length == 10`:
    | Positions | Result |
    |---|---|
    | 0, 1, 2 | Bottom col 0, 1, 2 |
    | 3, 4 | Right row 0, 1 |
    | 5, 6, 7 | Top col 2, 1, 0 |
    | 8, 9 | Left row 1, 0 |
  - `Resolve_OutOfRange_Throws` for -1 and 10.
  - `TryLaunch_RespectsCapacityIncludingQueue`: capacity 2. Launch A and B, then C returns false. `QueuedCount == 2`.
  - `TryAdmitFromQueue_EntersAtPositionZero_RaisesEvents`.
  - `Queue_WaitsWhilePositionZeroOccupied`: admit A; without advancing it, `TryAdmitFromQueue` for B returns false; after `Advance(0)` it returns true.
  - `TryLaunch_DepletedOrDuplicate_Throws`.
  - `Advance_And_RemoveAt_RaiseEvents_AndKeepEntryOrder`.
  - `Clear_EmptiesBeltAndQueue`.
- [ ] **Step 2:** `RUN PixelFlow.Tests.BeltModelTests` → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `RUN PixelFlow.Tests.BeltModelTests` → PASS.
- [ ] **Step 5: Commit:** `feat(core): belt path and belt model`

---

### Task 3: `BeltShootingLogic`, waiting slots, rules, session, auto-player

**Files:**
- Create: `Assets/PixelFlow/Scripts/Core/BeltShootingLogic.cs`, `Assets/PixelFlow/Tests/EditMode/Helpers/BeltAutoPlayer.cs`
- Modify:
  - `ShotEvent.cs`: add a `BeltPosition` field and a second ctor `(tank, beltPosition, cell, colorId)`. Keep the old ctor until T5.
  - `SlotQueueManager.cs`: add `TryRemove`.
  - `GameRules.cs`: add the overload.
  - `LevelSession.cs`: add `Belt`, `BeltShooting`.
- Test: `Assets/PixelFlow/Tests/EditMode/BeltShootingTests.cs`, `Assets/PixelFlow/Tests/EditMode/BeltRulesTests.cs`

**Interfaces:**
- Consumes: T1, T2, `SlotQueueManager`, `SupplyModel`.
- Produces:
  - `public sealed class BeltShootingLogic`
    - ctor `(PixelGridModel grid, BeltModel belt, SlotQueueManager waitingSlots)`
    - `int Tick(List<ShotEvent> output)`: spec §1 Flow 3 (a)–(e), then one `TryAdmitFromQueue`. It does not clear `output`.
    - `bool Overflowed`
    - `bool CanAnyWaitingTankHit()`: some slot tank's colour satisfies `HasAnyFront`.
  - `bool SlotQueueManager.TryRemove(ColorTankModel tank)`: shifts left and raises `OnTankRemoved(tank, oldIndex)`. Returns false if the tank is not in the slots.
  - `GameState GameRules.Evaluate(PixelGridModel grid, BeltModel belt, SlotQueueManager slots, SupplyModel supply, BeltShootingLogic shooting)`, implementing the spec §1 end states.
  - `LevelSession`:
    - `BeltModel Belt` (capacity `slotCount`, path `(W, H)`)
    - `BeltShootingLogic BeltShooting`
    - `Tray` is the waiting-slot `SlotQueueManager` (same capacity).
  - Test helper `BeltAutoPlayer.Play(LevelSession s, int maxTicks = 500000) : GameState`, following the policy in spec §2 Levels.

- [ ] **Step 1: Write failing tests** (small boards via `TestLevels`; tanks via `LevelSession.Create` or built directly)
  - `Tick_FiresAtFrontOnEachSide`: on a 3×3 single-colour board, one tank with large ammo enters. Ticks 1–12 hit the fronts given by `BeltPath.Resolve` in order. Check the cells for positions 0, 3, 6 and 9 exactly.
  - `Tick_MismatchedColour_DoesNotFireOrConsume`.
  - `Tick_DepletedTank_LeavesBelt_NotToSlot`.
  - `Tick_DepletesOnLastPosition_DoesNotGoToSlot`.
  - `Tick_LapWithAmmo_GoesToFirstFreeSlot`: after exactly `L` ticks the tank is in slot 0 and `OnTankLeft(tank, true)` fired.
  - `Tick_LapWithSlotsFull_SetsOverflowed`.
  - `Tick_AdmitsQueuedTankAfterProcessing`.
  - `Tick_DoesNotAllocate`: 64×64 single colour, 5 tanks with ammo 100000, warm-up 50 ticks, then `AllocAssert.NoAlloc`. Assert that shots > 0 in the measured tick.
  - `TryRemove_ShiftsLeft_AndRaisesEvent`; `TryRemove_Absent_ReturnsFalse`.
  - `Evaluate_Won`, `Evaluate_Overflow_IsLost`, `Evaluate_Stuck_IsLost`.
  - `Evaluate_SlotTankCanStillHit_IsPlaying`.
  - `Evaluate_DoesNotAllocate`.
  - `BeltAutoPlayer_WinsSimpleLevel`: `{"01","10"}` with tanks colour 0 ammo 2 and colour 1 ammo 2, 1 lane, 5 slots.
- [ ] **Step 2:** `RUN PixelFlow.Tests.BeltShootingTests` and `RUN PixelFlow.Tests.BeltRulesTests` → FAIL.
- [ ] **Step 3:** Implement. Iterate belt tanks by index in entry order. When `RemoveAt` shifts the array, don't advance the index.
- [ ] **Step 4:** Both filters PASS, and the full EditMode suite stays green, since the old code is untouched.
- [ ] **Step 5: Commit:** `feat(core): belt shooting, waiting slots and rules`

---

### Task 4: Belt-sweep generator and regenerated levels

**Files:**
- Modify: `Assets/PixelFlow/Scripts/Core/TankGenerator.cs`
- Modify tests: `StarterLevelTests.cs`, `LevelToolsTests.cs`, `StressTests.cs` (solvability and stress now use `BeltAutoPlayer`)
- Regenerate: `Assets/PixelFlow/Levels/*.asset` via `-executeMethod PixelFlow.EditorTools.StarterLevelFactory.CreateAllAssets`
- Modify: `Assets/PixelFlow/Tests/PlayMode/GameControllerPlayTests.cs`, `PerformancePlayTests.cs`. Mark the auto-play/perf tests that rely on the old tray rules `[Ignore("Re-enabled in conveyor Task 5")]`. Task 5 must remove every one of these markers.

**Interfaces:**
- Produces: `TankGenerator.Generate(width, height, cells, ammoPerTank)` with the same signature and a new peel order:
  - Each round visits positions `0..L-1` of `BeltPath(width, height)`.
  - At each position it takes that position's current front cell, skipping cells already removed this round, removes it and gives it the next `t`.
  - Rounds repeat until the grid is empty.
  - Chunking and the sort key are unchanged.

- [ ] **Step 1: Write failing tests**
  - `Generate_PeelOrder_FollowsBeltSweep`. Board 2×2 `{"10","00"}` (colour 1 at top-left (0,1)), ammoPerTank 1, L = 8. Trace:

    | Position | Line | Result | t |
    |---|---|---|---|
    | 0 | Bottom col 0 | removes (0,0), colour 0 | 0 |
    | 1 | Bottom col 1 | removes (1,0), colour 0 | 1 |
    | 2 | Right row 0 | row is empty | — |
    | 3 | Right row 1 | removes (1,1), colour 0 | 2 |
    | 4 | Top col 1 | column is empty | — |
    | 5 | Top col 0 | removes (0,1), colour 1 | 3 |

    Assert the tank colours in order are `[0, 0, 0, 1]` and every ammo is 1.
  - Update the solvability tests (`StarterLevelTests.Create_Level_IsSolvableByAutoPlayer`, the sample/sun level test, the stress tests) to `BeltAutoPlayer`. They must FAIL or be unverified before the generator change. Record the actual RED.
- [ ] **Step 2:** `RUN PixelFlow.Tests.LevelToolsTests` → FAIL.
- [ ] **Step 3:** Implement the sweep, regenerate the 6 assets, and commit them with their metas.
  - If a level is unsolvable, follow spec §2 (fix the ordering, or adjust that level's lanes/ammo with a documented reason). Never weaken the test.
- [ ] **Step 4:** Full EditMode suite PASS. PlayMode suite PASS, with the `[Ignore]`d tests listed in the report.
- [ ] **Step 5: Commit:** `feat(levels): belt-sweep tank order and regenerated levels`

---

### Task 5: Belt presentation, controller switch, old-code removal

**Files:**
- Create: `Assets/PixelFlow/Scripts/View/BeltView.cs`
- Modify:
  - `Scripts/View/TankBoardView.cs`
  - `Scripts/Controller/GameController.cs`
  - `Editor/PixelFlowSceneBuilder.cs`
  - `Scripts/Core/LevelSession.cs`, `ShotEvent.cs`, `GameRules.cs`, `PixelGridModel.cs`: delete the old API
  - The `Tests/PlayMode/*` and EditMode tests of the removed API: delete or port them
- Delete: `Scripts/Core/ShootingLogic.cs`, `Tests/EditMode/ShootingLogicTests.cs`, old tray-rule tests in `GameRulesTests.cs`, old `AutoPlayer.cs`. Rename `BeltAutoPlayer` → `AutoPlayer` if that's clean; otherwise keep the name.
- Regenerate: `Scenes/Game.unity`, prefabs (run `PixelFlowSceneBuilder.BuildGameScene`)

**Interfaces:**
- Consumes: T1–T4.
- Produces:
  - `BeltView : MonoBehaviour`
    - `void Build(BeltPath path, BoardLayout layout)`: lays out the track one track-width outside the board, with chevrons and an entrance counter.
    - `Vector3 PositionToWorld(float position)`: a fractional position, interpolated through corner points.
    - `void SetCounter(int used, int capacity)` (text `"{0}/{1}"`, no alloc).
    - `void Clear()`
  - `TankBoardView`:
    - `Build(LevelSession session, BeltView belt)`. It subscribes to `Belt` (`OnTankQueued`/`OnTankEntered`/`OnTankMoved`/`OnTankLeft`), `Tray` (waiting slots) and `Supply`. It draws `slotCount` empty slot frames.
    - `Vector3 GetMuzzle(ColorTankModel tank)` returns the belt world position for belt tanks.
    - `bool TryGetTank(Collider hit, out ColorTankModel tank, out TankLocation where)`, with `enum TankLocation { LaneFront, Slot, Other }`.
  - `GameController`:
    - `[SerializeField, Min(0.5f)] float lapSeconds = 4f` replaces `fireInterval`.
    - `public bool TryLaunchFromLane(int lane)` and `public bool TryLaunchFromSlot(int slot)` replace `TryActivateLane`. Both check `Belt.IsFull` **before** removing the tank from the lane or slot.
    - Ticks are driven at `lapSeconds / L`, capped at 16 per frame.
    - The end-state latch is unchanged: wait for projectiles, the destroy queue and animating cells.
    - Overflow/stuck Lost come from the belt `GameRules.Evaluate`.

- [ ] **Step 1: Write failing PlayMode tests** (port the existing ones to the new hooks first; delete the `[Ignore]` markers from Task 4)
  - `Level1_AutoPlay_ReachesWon`, `SunLevel_AutoPlay_ReachesWon`: the policy matches `BeltAutoPlayer`, driven through `TryLaunchFromSlot`/`TryLaunchFromLane` each frame.
  - `Overflow_ReachesLost`: a purpose-built level with slotCount 1, two tanks whose colour is blocked, both launched. The second lap completion overflows.
  - `TryLaunchFromSlot_BeltFull_ReturnsFalseAndKeepsSlot`.
  - `Tap_SlotTank_Relaunches`: synthesized touch, reusing the existing Touchscreen helper.
  - `Tap_LaneFront_LaunchesOntoBelt`.
  - `Retry_WithTanksOnBelt_ClearsEverything`: belt Count/QueuedCount 0, no active tank views except supply, projectiles 0, `InstanceCount` equals the level's pixel count.
  - `Next_CyclesThroughAllSixLevels` stays.
  - Capture `Logs/belt_capture.png` mid-game on level 3.
- [ ] **Step 2:** Run PlayMode → the new tests FAIL (the hooks don't exist yet).
- [ ] **Step 3:** Implement the View, Controller and scene builder, then delete the old Core API and its tests.
  - Layout: board on top inside the track, the counter at the bottom-left of the track, 5 slot frames below, supply lanes below them. It must fit portrait with ortho size 10. Rerun the builder.
- [ ] **Step 4:** Full EditMode + PlayMode PASS. `grep -rn "TryGetExposedCell\|HasExposed\|ShootingLogic\b\|SlotIndex\|TryActivateLane\|Ignore(\"Re-enabled" Assets/PixelFlow` returns nothing. Read `Logs/belt_capture.png` and compare it with the reference layout (track around the board, chevrons, x/5 counter, slot frames, lanes).
- [ ] **Step 5: Commit** in logical pieces, e.g. `feat(view): conveyor belt view`, `feat(controller): drive the conveyor belt`, `refactor(core): remove tray shooting`.

---

### Task 6: Performance re-verification

**Files:**
- Modify: `Assets/PixelFlow/Tests/PlayMode/PerformancePlayTests.cs`, `Assets/PixelFlow/Tests/EditMode/StressTests.cs` (if Task 4 didn't fully port them)

- [ ] **Step 1:** Port `SteadyStateShooting_NoGCAllocPerFrame_AndFrameBudget` to the belt.
  - Run auto-play on the sun level with a full belt.
  - Keep the `FeedTray` profiler-marker approach, renamed to `FeedBelt`, so the launch path counts as game code.
  - Assert 0 B of game GC per frame, a main-thread average below 16.6 ms, at least 90% of sampled frames with a tick, and `BatchCount <= 5`.
- [ ] **Step 2:** `Stress_5000Cells_FullAutoPlay_TicksUnder2msAverage` and `Stress_FullAutoPlay_NoAllocationInSteadyState` use `BeltAutoPlayer` on the 100×50 stress level.
- [ ] **Step 3:** Run the full EditMode + PlayMode suites → PASS. Put the measured numbers in the commit message.
- [ ] **Step 4: Commit:** `test(perf): conveyor belt performance`
