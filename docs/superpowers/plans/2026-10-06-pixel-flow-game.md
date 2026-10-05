# Pixel Flow (bắn màu xóa tranh pixel) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Game puzzle bắn màu xóa tranh pixel art (1000–5000 cube/màn) chạy 60 FPS trên mobile, kiến trúc phân tầng Data → Core → Performance → View → Controller, Logic và View tách rời hoàn toàn bằng C# event.

**Architecture:** Dữ liệu màn chơi được "nướng" sẵn ở Editor thành `LevelData` (ScriptableObject, cells dạng `byte[]`, có định dạng binary RLE + JSON). Tầng Core là C# thuần (không MonoBehaviour), test được bằng EditMode test, phát event cho View. View vẽ toàn bộ bảng bằng `Graphics.RenderMeshInstanced` + `MaterialPropertyBlock` (không có GameObject cho từng ô); chỉ ô đang vỡ mới mượn một `PixelCellView` từ pool. `GameController` là nơi duy nhất nối các tầng.

**Tech Stack:** Unity 6000.0.84f1, URP 17.0.4 (2D Renderer có sẵn trong project), Input System 1.20 (activeInputHandler = Both), TextMeshPro (trong `com.unity.ugui` 2.0), Unity Test Framework 1.6 (NUnit, EditMode).

**Spec:** `spec.md.txt` (thư mục gốc project). Quyết định bổ sung đã chốt với user: **luật "ô lộ ra"** (xem Global Constraints).

## Luật chơi đã chốt (spec không nêu, user đã chọn)

- Lưới `width × height`, hàng `y = 0` ở **dưới cùng** (gần khay). Ô trống = `255`.
- **Ô lộ ra (exposed)** của một cột = ô không trống có `y` nhỏ nhất trong cột đó. Mỗi cột có tối đa 1 ô lộ ra.
- **Supply**: các thùng trong `LevelData.tanks` được chia round-robin vào `laneCount` làn (tank `i` → làn `i % laneCount`). Người chơi chỉ chạm được thùng **đầu làn**.
- Chạm thùng đầu làn → nếu khay chưa đầy, thùng rời làn vào slot trống đầu tiên của khay (`slotCount` slot). Khay đầy → chạm bị bỏ qua, thùng vẫn ở làn.
- Mỗi nhịp bắn (`fireInterval`), **mỗi** thùng trong khay (theo thứ tự slot trái → phải) bắn 1 viên vào 1 ô lộ ra cùng màu nếu có. Hết đạn (`count == 0`) → thùng biến mất, các thùng phía sau shift left.
- **Thắng**: số pixel còn lại = 0. **Thua**: còn pixel, không thùng nào trong khay bắn được, và (khay đầy **hoặc** supply rỗng).

## Global Constraints

- Unity `6000.0.84f1`; editor tại `E:/unity/6000.0.84f1/Editor/Unity.exe`.
- Mọi code nằm dưới `Assets/PixelFlow/`, namespace gốc `PixelFlow` (`PixelFlow.Data`, `PixelFlow.Core`, `PixelFlow.Performance`, `PixelFlow.View`, `PixelFlow.Controller`, `PixelFlow.EditorTools`, `PixelFlow.Tests`).
- Phụ thuộc asmdef một chiều: `Data` ← `Core` ← `View` ← `Controller`; `Performance` không phụ thuộc ai. `Core`/`Data`/`Performance` **không** chứa MonoBehaviour và không gọi API scene.
- Logic → View chỉ qua C# `event Action<...>`. Model không biết View tồn tại.
- Cấm `Instantiate`/`Destroy` trong gameplay loop (chỉ được dùng khi build level / prewarm pool). Cấm LINQ, closure, boxing trong `Update`/`Step`/`Process`.
- Bảng pixel vẽ bằng GPU instancing: batch tối đa **1023** instance/lệnh vẽ.
- Hằng số: `LevelData.EmptyCell = 255`, tối đa **254** màu/palette, mặc định `laneCount = 3`, `slotCount = 5`, `ammoPerTank = 20`, `fireInterval = 0.06f` s, `destroysPerFrame = 64`, `Application.targetFrameRate = 60`.
- Load level (deserialize + build model) cho 5000 cells phải < **1 giây** (test mục tiêu < 50 ms trong EditMode).
- Mọi public type/member có XML doc comment `///` bằng tiếng Anh (spec yêu cầu chú thích chi tiết).

## Cách chạy test (dùng cho mọi task)

Editor đang mở thì CLI batchmode không chạy được. Hai cách, cách nào cũng được:

1. **Trong Editor** (hoặc qua Unity MCP `Unity_RunCommand`): `Window > General > Test Runner > EditMode`, chọn test/fixture, Run. Đọc kết quả + `Unity_GetConsoleLogs`.
2. **CLI khi Editor đóng:**
   ```bash
   "/e/unity/6000.0.84f1/Editor/Unity.exe" -batchmode -nographics -projectPath "E:/unity/game 1/pixel flow" \
     -runTests -testPlatform EditMode -testFilter "<Fixture hoặc Fixture.Test>" \
     -testResults "E:/unity/game 1/pixel flow/Logs/editmode.xml" -logFile -
   ```
   Pass = exit code 0 và `result="Passed"` trong `Logs/editmode.xml`.

Ký hiệu `RUN <filter>` bên dưới = chạy test theo một trong hai cách trên.

## Review Focus

1. **Chạm thùng khi khay đầy** → không mất thùng, thùng vẫn ở đầu làn; nếu không ai bắn được thì báo Thua (Task 5 `TryAdd_WhenFull_ReturnsFalse_AndLeavesSlotsUnchanged`, Task 6 `Evaluate_TrayFullAndNoneCanFire_IsLost`, Task 10 kiểm tra `IsFull` trước `TryTakeFront`).
2. **Thùng cạn đạn giữa một `Step`** trong khi thùng phía sau còn bắn → không thùng nào bị bỏ lượt hoặc bắn 2 lần, `SlotIndex` của `ShotEvent` đúng (Task 6 `Step_TankDepletesMidStep_OthersStillFireOnceEach`).
3. **Cột có khoảng trống** (ô trống xen giữa) và cột rỗng hoàn toàn → ô lộ ra nhảy qua ô trống; cột rỗng không bao giờ lộ (Task 4 `RemoveCell_SkipsGapsInColumn`, `EmptyColumn_IsNeverExposed`).
4. **Dữ liệu level hỏng** (sai magic, bị cắt cụt, ảnh > 254 màu, tank có colorId ngoài palette) → ném exception rõ ràng, không load rác (Task 2 `FromBytes_Truncated_Throws`, `FromBytes_BadMagic_Throws`; Task 6 `Create_TankColorOutsidePalette_Throws`; Task 7 `Quantize_MoreThan254Colors_Throws`).
5. **Level sửa tay có thùng không còn mục tiêu** (màu không có trên bảng / thừa đạn) → game không treo, thùng nằm khay, báo Thua đúng lúc (Task 6 `Evaluate_TankWithNoPixelsOfItsColor_EventuallyLost`).

---

## File Structure

```
Assets/PixelFlow/
  Scripts/Data/          PixelFlow.Data.asmdef
    ColorTankData.cs       struct (colorId, ammo)
    LevelData.cs           ScriptableObject, cells byte[] + palette + tanks
    LevelSerializer.cs     binary RLE (PXF1) + JSON
  Scripts/Core/          PixelFlow.Core.asmdef (→ Data)
    PixelGridModel.cs      pixel theo màu (Dictionary) + ô lộ ra theo màu O(1)
    ColorTankModel.cs      đạn + event
    SlotQueueManager.cs    khay N slot, shift left
    SupplyModel.cs         các làn thùng chờ
    ShotEvent.cs           readonly struct kết quả 1 phát bắn
    ShootingLogic.cs       điều phối bắn, zero-alloc
    GameRules.cs           GameState Evaluate(...)
    LevelSession.cs        LevelData → toàn bộ model
    TankGenerator.cs       sinh danh sách thùng theo thứ tự "bóc lớp"
  Scripts/Performance/   PixelFlow.Performance.asmdef (không ref)
    ObjectPool.cs
    TimeSlicedQueue.cs
  Scripts/View/          PixelFlow.View.asmdef (→ Data, Core, Performance, Unity.TextMeshPro)
    BoardLayout.cs, PixelGridRenderer.cs, PixelCellView.cs, DebrisFx.cs,
    ProjectileSystem.cs, ColorTankView.cs, TankBoardView.cs, GameHudView.cs
  Scripts/Controller/    PixelFlow.Controller.asmdef (→ tất cả + Unity.InputSystem)
    GameController.cs
  Editor/                PixelFlow.Editor.asmdef (Editor only, → Data, Core, View, Controller)
    TextureQuantizer.cs, SampleLevelFactory.cs, LevelBakerWindow.cs, PixelFlowSceneBuilder.cs
  Shaders/InstancedColor.shader
  Materials/, Prefabs/, Levels/, Scenes/Game.unity   (sinh bởi PixelFlowSceneBuilder)
  Tests/EditMode/        PixelFlow.Tests.EditMode.asmdef (→ Data, Core, Performance, View, Editor; nunit)
    *Tests.cs, Helpers/AutoPlayer.cs, Helpers/TestLevels.cs
```

---

### Task 1: Scaffolding — git, thư mục, asmdef, smoke test

**Files:**
- Create: `.gitignore` (template Unity chuẩn: `Library/ Temp/ Obj/ Build/ Builds/ Logs/ UserSettings/ *.csproj *.sln .vs/ .idea/`)
- Create: 6 file `.asmdef` theo File Structure (tên asmdef = tên file; Editor asmdef `includePlatforms: ["Editor"]`; test asmdef `defineConstraints: ["UNITY_INCLUDE_TESTS"]`, `overrideReferences: true`, `precompiledReferences: ["nunit.framework.dll"]`, references `UnityEngine.TestRunner`, `UnityEditor.TestRunner`, `includePlatforms: ["Editor"]`)
- Test: `Assets/PixelFlow/Tests/EditMode/SmokeTests.cs`

**Interfaces:**
- Produces: các assembly trên compile được; mọi task sau thêm file vào đúng assembly.

- [ ] **Step 1:** `git init` ở thư mục gốc project, thêm `.gitignore`, commit trạng thái hiện tại (`chore: initial Unity project`).
- [ ] **Step 2: Viết smoke test**
  ```csharp
  [Test] public void Assemblies_Compile() => Assert.Pass();
  ```
- [ ] **Step 3:** Mở Editor/refresh, `RUN SmokeTests` → PASS, Console không có lỗi compile.
- [ ] **Step 4: Commit** `chore: add PixelFlow assemblies and test harness`

---

### Task 2: Data layer — `ColorTankData`, `LevelData`, `LevelSerializer`

**Files:**
- Create: `Assets/PixelFlow/Scripts/Data/ColorTankData.cs`, `LevelData.cs`, `LevelSerializer.cs`
- Test: `Assets/PixelFlow/Tests/EditMode/LevelSerializerTests.cs`, `Helpers/TestLevels.cs`

**Interfaces:**
- Produces:
  - `[Serializable] public struct ColorTankData { public byte colorId; public int ammo; }`
  - `[CreateAssetMenu(menuName="PixelFlow/Level Data")] public class LevelData : ScriptableObject` với public fields `int width, height; Color32[] palette; byte[] cells` (row-major, index = `y * width + x`); `ColorTankData[] tanks; int laneCount = 3; int slotCount = 5;` hằng `public const byte EmptyCell = 255;` method `byte GetCell(int x, int y)`.
  - `public static class LevelSerializer`: `byte[] ToBytes(LevelData)`, `void FromBytes(byte[] data, LevelData target)`, `string ToJson(LevelData)`, `void FromJson(string json, LevelData target)`.
  - `TestLevels.Create(string[] rows, Color32[] palette, ColorTankData[] tanks, int lanes = 1, int slots = 5) : LevelData` — `rows[0]` là hàng **trên cùng** (dễ đọc khi viết test), ký tự `'.'` = trống, `'0'..'9'` = colorId. Dùng cho mọi test sau.

Binary format v1 (little-endian, `BinaryWriter`):
`"PXF1"` (4 byte ASCII) · `byte version=1` · `ushort width` · `ushort height` · `byte paletteCount` · palette `RGBA×count` · `int runCount` · runs `(byte colorId, ushort length)` (RLE trên `cells`, run dài > 65535 thì tách) · `ushort tankCount` · tanks `(byte colorId, ushort ammo)` · `byte laneCount` · `byte slotCount`.
JSON: `JsonUtility` trên DTO private `{ width, height, string[] palette ("#RRGGBBAA"), string cellsRle (Base64 của khối runs ở trên), ColorTankData[] tanks, laneCount, slotCount }`.

- [ ] **Step 1: Viết test fail**
  - `ToBytes_FromBytes_RoundTripsAllFields` — level 3×2 có ô trống, 2 màu, 2 tank → mọi field bằng nhau sau round-trip.
  - `ToJson_FromJson_RoundTripsAllFields` — như trên với JSON.
  - `ToBytes_UniformLevel_IsCompressed` — 64×64 một màu → `ToBytes(...).Length < 100`.
  - `FromBytes_5000Cells_LoadsUnder50ms` — 100×50 ngẫu nhiên (seed 42, 8 màu) → `Stopwatch` quanh `FromBytes` < 50 ms (chạy 1 lần warm-up trước).
  - `FromBytes_BadMagic_Throws` → `Assert.Throws<InvalidDataException>`.
  - `FromBytes_Truncated_Throws` — cắt mảng còn một nửa → `InvalidDataException` (bọc `EndOfStreamException`).
  - `FromBytes_RunsDoNotMatchSize_Throws` — tổng length các run ≠ width×height → `InvalidDataException`.
  - `GetCell_UsesBottomRowAsYZero` — với `TestLevels.Create(new[]{"1.", "0."}, ...)`: `GetCell(0,0)==0`, `GetCell(0,1)==1`, `GetCell(1,0)==255`.
- [ ] **Step 2:** `RUN LevelSerializerTests` → FAIL (type chưa tồn tại / compile error).
- [ ] **Step 3:** Implement 3 file Data + `TestLevels`.
- [ ] **Step 4:** `RUN LevelSerializerTests` → PASS.
- [ ] **Step 5: Commit** `feat(data): LevelData with RLE binary and JSON serialization`

---

### Task 3: Performance primitives — `ObjectPool<T>`, `TimeSlicedQueue<T>`

**Files:**
- Create: `Assets/PixelFlow/Scripts/Performance/ObjectPool.cs`, `TimeSlicedQueue.cs`
- Test: `Assets/PixelFlow/Tests/EditMode/PerformancePrimitivesTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class ObjectPool<T> where T : class` — ctor `(Func<T> create, Action<T> onGet = null, Action<T> onRelease = null, int prewarm = 0)`; `T Get()`; `void Release(T item)`; `int CountAll`, `int CountInactive`. Nội bộ: `Stack<T>` + `HashSet<T>` theo dõi phần tử đang inactive để phát hiện double release.
  - `public sealed class TimeSlicedQueue<T>` — `void Enqueue(T item)`; `int Count`; `void Clear()`; `int Process(int budget, Action<T> handler)` xử lý FIFO tối đa `budget` phần tử, trả về số đã xử lý.

- [ ] **Step 1: Viết test fail**
  - `Pool_Prewarm_CreatesInactiveItems` — prewarm 5 → `CountAll==5`, `CountInactive==5`, `create` gọi đúng 5 lần.
  - `Pool_GetAfterRelease_ReusesInstance` — `Get`, `Release`, `Get` → cùng reference, `create` gọi 1 lần.
  - `Pool_CallsOnGetAndOnRelease`.
  - `Pool_DoubleRelease_Throws` → `InvalidOperationException`.
  - `Queue_Process_RespectsBudgetAndOrder` — enqueue 0..9, `Process(4, ...)` → handler nhận `0,1,2,3`, trả 4, `Count==6`; `Process(100, ...)` → trả 6.
  - `Queue_Process_DoesNotAllocate` — sau warm-up (enqueue/process 1000 phần tử), handler cache trong field; `GC.GetAllocatedBytesForCurrentThread()` trước/sau 1000 lần enqueue+`Process` → delta == 0.
  - `Pool_GetRelease_DoesNotAllocateAfterWarmup` — tương tự với pool prewarm 16.
- [ ] **Step 2:** `RUN PerformancePrimitivesTests` → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `RUN PerformancePrimitivesTests` → PASS.
- [ ] **Step 5: Commit** `feat(perf): zero-alloc ObjectPool and TimeSlicedQueue`

---

### Task 4: `PixelGridModel`

**Files:**
- Create: `Assets/PixelFlow/Scripts/Core/PixelGridModel.cs`
- Test: `Assets/PixelFlow/Tests/EditMode/PixelGridModelTests.cs`

**Interfaces:**
- Consumes: `LevelData.EmptyCell`.
- Produces: `public sealed class PixelGridModel`
  - ctor `(int width, int height, byte[] cells)` — copy `cells`; `ArgumentException` nếu `cells.Length != width*height`.
  - `int Width, Height, RemainingCount`; `byte GetCell(int x, int y)`; `int GetRemaining(int colorId)`.
  - `IReadOnlyList<Vector2Int> GetCellsOfColor(int colorId)` — backing `Dictionary<int, List<Vector2Int>>` (yêu cầu spec); xóa bằng swap-remove + bảng `int[] indexInColorList` → O(1). Màu không có → list rỗng dùng chung (không alloc).
  - `bool HasExposed(int colorId)`; `bool TryGetExposedCell(int colorId, out Vector2Int cell)` — O(1), zero-alloc. Backing: `int[] frontRow` mỗi cột + với mỗi màu một tập cột lộ ra có index (mảng + vị trí, swap-remove). Trả về phần tử cuối của tập (deterministic).
  - `void RemoveCell(Vector2Int cell)` — chỉ cho phép ô đang lộ ra, ngược lại `InvalidOperationException`. Cập nhật: dict màu, count, `frontRow` cột đó tiến lên ô không trống kế tiếp (bỏ qua ô trống), tập lộ ra của màu cũ/màu mới.
  - `event Action<Vector2Int, int> OnCellRemoved` (cell, colorId); `event Action OnCleared` (khi `RemainingCount` về 0).

- [ ] **Step 1: Viết test fail** (dùng `TestLevels`, rows[0] = hàng trên)
  - `Build_CountsPerColorAndTotal`.
  - `GetCellsOfColor_ReturnsAllCoordinatesOfThatColor`.
  - `TryGetExposedCell_OnlyBottomMostCellPerColumnIsExposed` — `{"0","1"}`: màu 1 lộ, màu 0 không.
  - `RemoveCell_ExposesNextCellInColumn` — xóa ô màu 1 → màu 0 lộ ra ở `(0,1)`.
  - `RemoveCell_SkipsGapsInColumn` — `{"0", ".", "1"}`: xóa `(0,0)` → `TryGetExposedCell(0)` trả `(0,2)`.
  - `EmptyColumn_IsNeverExposed` — cột toàn `.` → không màu nào lộ ở cột đó; `HasExposed` false cho màu không có ô.
  - `RemoveCell_NotExposed_Throws` → `InvalidOperationException`.
  - `RemoveCell_RaisesOnCellRemoved_AndOnClearedWhenEmpty`.
  - `TryGetExposedCell_DoesNotAllocate` — 64×64 ngẫu nhiên, sau warm-up gọi 10 000 lần → delta alloc 0.
  - `Build_5000Cells_Under20ms` — 100×50 ngẫu nhiên 8 màu.
- [ ] **Step 2:** `RUN PixelGridModelTests` → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `RUN PixelGridModelTests` → PASS.
- [ ] **Step 5: Commit** `feat(core): PixelGridModel with O(1) color and exposure lookup`

---

### Task 5: `ColorTankModel`, `SlotQueueManager`, `SupplyModel`

**Files:**
- Create: `Assets/PixelFlow/Scripts/Core/ColorTankModel.cs`, `SlotQueueManager.cs`, `SupplyModel.cs`
- Test: `Assets/PixelFlow/Tests/EditMode/TankModelsTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class ColorTankModel` — ctor `(int id, byte colorId, int ammo)` (`ArgumentOutOfRangeException` nếu `ammo <= 0`); `int Id`; `byte ColorId`; `int Ammo`; `bool IsDepleted`; `bool TryConsume()` (giảm 1, false nếu đã cạn); `event Action<ColorTankModel, int> OnAmmoChanged` (tank, ammo mới); `event Action<ColorTankModel> OnDepleted` (bắn đúng 1 lần, sau `OnAmmoChanged` về 0).
  - `public sealed class SlotQueueManager` — ctor `(int capacity)`; `int Capacity, Count`; `bool IsFull`; `ColorTankModel this[int index]`; `int IndexOf(ColorTankModel)`; `bool TryAdd(ColorTankModel tank)` (thêm vào cuối, subscribe `OnDepleted`); khi tank cạn → tự remove, unsubscribe, các tank phía sau shift left; `event Action<ColorTankModel, int> OnTankAdded` (tank, slotIndex); `event Action<ColorTankModel, int> OnTankRemoved` (tank, index cũ). Nội bộ `List<ColorTankModel>` capacity cố định.
  - `public sealed class SupplyModel` — ctor `(IReadOnlyList<ColorTankModel> tanks, int laneCount)` chia round-robin; `int LaneCount`; `int TotalRemaining`; `bool IsEmpty`; `IReadOnlyList<ColorTankModel> GetLane(int lane)` (index 0 = đầu làn); `ColorTankModel PeekFront(int lane)` (null nếu rỗng); `int FindLaneWithFront(ColorTankModel tank)` (-1 nếu không phải đầu làn); `bool TryTakeFront(int lane, out ColorTankModel tank)`; `event Action<int> OnLaneChanged`.

- [ ] **Step 1: Viết test fail**
  - `Tank_TryConsume_DecrementsAndRaisesAmmoChanged`.
  - `Tank_LastConsume_RaisesDepletedOnce_ThenTryConsumeReturnsFalse`.
  - `Tank_NonPositiveAmmo_Throws`.
  - `TryAdd_FillsSlotsInOrder_RaisesOnTankAdded` — slotIndex 0,1,2.
  - `TryAdd_WhenFull_ReturnsFalse_AndLeavesSlotsUnchanged` — capacity 2, add 3 → false, `Count==2`, không event.
  - `FirstTankDepleted_ShiftsOthersLeft` — A,B,C; A cạn → `[B,C]`, `OnTankRemoved(A,0)`.
  - `MiddleTankDepleted_ShiftsOnlyTanksBehind` — A,B,C; B cạn → `[A,C]`, `OnTankRemoved(B,1)`.
  - `Supply_DistributesRoundRobin` — 7 tank, 3 lane → lane0 = ids `0,3,6`, lane1 = `1,4`, lane2 = `2,5`.
  - `Supply_TryTakeFront_RemovesFrontAndRaisesLaneChanged`; `Supply_TryTakeFront_EmptyLane_ReturnsFalse`.
  - `Supply_FindLaneWithFront_ReturnsMinusOneForNonFront`.
- [ ] **Step 2:** `RUN TankModelsTests` → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `RUN TankModelsTests` → PASS.
- [ ] **Step 5: Commit** `feat(core): tank, slot queue and supply models`

---

### Task 6: `ShootingLogic`, `GameRules`, `LevelSession`

**Files:**
- Create: `Assets/PixelFlow/Scripts/Core/ShotEvent.cs`, `ShootingLogic.cs`, `GameRules.cs`, `LevelSession.cs`
- Test: `Assets/PixelFlow/Tests/EditMode/ShootingLogicTests.cs`, `GameRulesTests.cs`

**Interfaces:**
- Consumes: Task 4, Task 5.
- Produces:
  - `public readonly struct ShotEvent { public readonly ColorTankModel Tank; public readonly int SlotIndex; public readonly Vector2Int Cell; public readonly byte ColorId; }` + ctor.
  - `public sealed class ShootingLogic` — ctor `(PixelGridModel grid, SlotQueueManager tray)`; `int Step(List<ShotEvent> output)` — chụp các tank trong khay vào buffer `ColorTankModel[]` cấp phát sẵn (size = capacity), với mỗi tank theo thứ tự slot: nếu `grid.TryGetExposedCell(tank.ColorId, out cell)` → `grid.RemoveCell(cell)`, ghi `ShotEvent` (SlotIndex = index lúc chụp), `tank.TryConsume()`. Không clear `output` (caller clear). Trả số phát. `bool CanAnyTankFire()`.
  - `public enum GameState { Playing, Won, Lost }`; `public static class GameRules { public static GameState Evaluate(PixelGridModel grid, SlotQueueManager tray, SupplyModel supply, ShootingLogic shooting); }` theo luật đã chốt.
  - `public sealed class LevelSession` — `PixelGridModel Grid; SlotQueueManager Tray; SupplyModel Supply; ShootingLogic Shooting; Color32[] Palette; int Width, Height;` `static LevelSession Create(LevelData data)` — tank id = index trong `data.tanks`; `ArgumentException` nếu `tank.colorId >= palette.Length` hoặc `laneCount < 1` hoặc `slotCount < 1`.

- [ ] **Step 1: Viết test fail**
  - `Step_TankFiresAtExposedCellOfItsColor` — 1 shot, cell đúng, ammo giảm 1, ô bị xóa.
  - `Step_TankWithoutExposedTarget_DoesNotFire` — `{"0","1"}`, tank màu 0 → 0 shot, ammo giữ nguyên.
  - `Step_EachTrayTankFiresAtMostOncePerStep` — 2 tank cùng màu, 3 cột lộ → 2 shot.
  - `Step_TankDepletesMidStep_OthersStillFireOnceEach` — slot0 ammo 1 màu 0, slot1 màu 1, slot2 màu 0 → 3 shot với SlotIndex `0,1,2`; sau step khay = `[t1, t2]`.
  - `Step_RepeatedUntilDone_ClearsColumnBottomUp` — cột `{"0","0","0"}`, tank ammo 3 → 3 step, cells theo thứ tự y = 0,1,2, tank bị xóa khỏi khay.
  - `Step_DoesNotAllocate` — 64×64 ngẫu nhiên, 5 tank, `List<ShotEvent>` capacity 16 dùng lại; sau warm-up 100 step → delta alloc 0 (tank cạn sẽ tự rời khay — dùng ammo lớn).
  - `Evaluate_NoPixelsLeft_IsWon`.
  - `Evaluate_TrayFullAndNoneCanFire_IsLost`.
  - `Evaluate_SupplyEmptyAndNoneCanFire_IsLost`.
  - `Evaluate_TrayNotFullAndSupplyHasTanks_IsPlaying` — kể cả khi không ai bắn được.
  - `Evaluate_TankWithNoPixelsOfItsColor_EventuallyLost` — palette 3 màu, bảng chỉ màu 0 với 1 ô, tanks `[màu 2 ammo 5, màu 0 ammo 1]`, slot 1, lane 1: lấy tank đầu vào khay → `Lost` (khay đầy, không bắn được).
  - `Create_BuildsModelsFromLevelData` — tank ids 0..n-1, lane/slot đúng.
  - `Create_TankColorOutsidePalette_Throws`.
- [ ] **Step 2:** `RUN ShootingLogicTests` và `RUN GameRulesTests` → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Chạy lại → PASS, và `RUN PixelFlow` (toàn bộ) vẫn PASS.
- [ ] **Step 5: Commit** `feat(core): shooting logic, game rules and level session`

---

### Task 7: Công cụ tạo level — `TankGenerator`, `TextureQuantizer`, `SampleLevelFactory`, `LevelBakerWindow`

**Files:**
- Create: `Assets/PixelFlow/Scripts/Core/TankGenerator.cs`
- Create: `Assets/PixelFlow/Editor/TextureQuantizer.cs`, `SampleLevelFactory.cs`, `LevelBakerWindow.cs`
- Create: `Assets/PixelFlow/Levels/Level_001.asset` (sinh bởi menu ở Step 5)
- Test: `Assets/PixelFlow/Tests/EditMode/LevelToolsTests.cs`, `Helpers/AutoPlayer.cs`

**Interfaces:**
- Consumes: `PixelGridModel`, `LevelSession`, `GameRules`, `LevelData`, `LevelSerializer`.
- Produces:
  - `public static class TankGenerator { public static ColorTankData[] Generate(int width, int height, byte[] cells, int ammoPerTank); }` — Thuật toán: mô phỏng "bóc lớp" trên một `PixelGridModel` tạm: mỗi vòng xóa toàn bộ ô đang lộ ra (duyệt cột 0→width-1), gán mỗi ô một thời điểm `t` tăng dần. Với mỗi màu, sắp các `t` của nó; tank thứ `k` của màu đó chứa `min(ammoPerTank, còn lại)` đạn, khóa sắp xếp = `t` của ô đầu tiên trong phần của nó (index `k*ammoPerTank`). Trả về tất cả tank sắp theo khóa tăng dần (hòa thì theo colorId).
  - `public static class TextureQuantizer { public static void Quantize(Color32[] pixels, int width, int height, out Color32[] palette, out byte[] cells); }` — pixel `a < 128` → trống; mỗi màu RGB khác nhau → một entry palette (theo thứ tự xuất hiện); > 254 màu → `ArgumentException`. `pixels` theo layout của `Texture2D.GetPixels32` (hàng 0 = dưới cùng, khớp với `LevelData`).
  - `public static class SampleLevelFactory { public static LevelData Create64(); [MenuItem("PixelFlow/Create Sample Level")] static void CreateAsset(); }` — 64×64 = 4096 ô, 5 màu, hình vẽ procedural (vd. mặt trời: nền xanh trời, các vòng tròn đồng tâm vàng/cam, mặt đất xanh lá 12 hàng dưới) — không ô trống; tanks từ `TankGenerator.Generate(..., 20)`; lane 3, slot 5. Lưu vào `Assets/PixelFlow/Levels/Level_001.asset`.
  - `LevelBakerWindow : EditorWindow` (menu `PixelFlow/Level Baker`): field Texture2D (phải bật Read/Write), `ammoPerTank` (20), `laneCount` (3), `slotCount` (5), nút **Bake** → `Quantize` → `TankGenerator` → tạo/ghi đè `LevelData` asset cạnh texture, log số ô/màu/tank. Không có logic riêng ngoài nối các hàm trên.
  - Test helper `AutoPlayer.Play(LevelSession s, int maxSteps = 100000) : GameState` — vòng lặp: nếu `Shooting.CanAnyTankFire()` → `Step`; ngược lại nếu khay chưa đầy → lấy thùng đầu làn có `Id` nhỏ nhất; dừng khi `Evaluate != Playing`.

- [ ] **Step 1: Viết test fail**
  - `Generate_AmmoPerColorEqualsPixelCount` — trên level 100×50 ngẫu nhiên.
  - `Generate_NoTankExceedsAmmoPerTank_AndNoneIsEmpty`.
  - `Generate_OrdersTanksByPeelTime` — `{"1","0"}` (0 ở dưới) với ammo 20 → tank[0] màu 0, tank[1] màu 1.
  - `Quantize_MapsColorsAndTransparency` — 2×2 `[red, red, clear, blue]` → palette `[red, blue]`, cells `[0,0,255,1]`.
  - `Quantize_MoreThan254Colors_Throws`.
  - `SampleLevel_Has4096Cells_AndIsSolvableByAutoPlayer` — `AutoPlayer.Play(LevelSession.Create(SampleLevelFactory.Create64())) == GameState.Won`.
  - `SampleLevel_SerializedLoadUnder50ms` — `ToBytes` rồi đo `FromBytes` + `LevelSession.Create` < 50 ms.
- [ ] **Step 2:** `RUN LevelToolsTests` → FAIL.
- [ ] **Step 3:** Implement. Nếu `SampleLevel_..._IsSolvableByAutoPlayer` fail: dùng superpowers:systematic-debugging trên thứ tự của `TankGenerator`, **không** sửa test hay nới slotCount.
- [ ] **Step 4:** `RUN LevelToolsTests` → PASS.
- [ ] **Step 5:** Trong Editor chạy menu `PixelFlow/Create Sample Level` → asset `Assets/PixelFlow/Levels/Level_001.asset` tồn tại, Inspector hiện `width 64, height 64`.
- [ ] **Step 6: Commit** `feat(tools): tank generator, texture baker and sample level`

---

### Task 8: GPU-instanced board — shader, `BoardLayout`, `PixelGridRenderer`

**Files:**
- Create: `Assets/PixelFlow/Shaders/InstancedColor.shader`
- Create: `Assets/PixelFlow/Scripts/View/BoardLayout.cs`, `PixelGridRenderer.cs`
- Test: `Assets/PixelFlow/Tests/EditMode/BoardLayoutTests.cs`

**Interfaces:**
- Consumes: `PixelGridModel`, `LevelSession.Palette`.
- Produces:
  - Shader `"PixelFlow/InstancedColor"`: URP, `Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }`, pass không có LightMode (để chạy cả 2D Renderer lẫn Forward), `#pragma multi_compile_instancing`, `UNITY_INSTANCING_BUFFER_START(Props) UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)`; shading rẻ: `color * (0.65 + 0.35 * saturate(dot(normalWS, normalize(float3(-0.4, 0.6, -0.7)))))` để cube có khối.
  - `[Serializable] public struct BoardLayout { public Vector3 Origin; public float CellSize; public Vector3 CellToWorld(Vector2Int cell); public static BoardLayout Fit(int width, int height, Rect worldArea); }` — `Fit` chọn `CellSize = min(area.width/width, area.height/height)`, căn giữa; `CellToWorld` trả tâm ô, `z = 0`.
  - `public sealed class PixelGridRenderer : MonoBehaviour` — `[SerializeField] Mesh cubeMesh; [SerializeField] Material material;` `void Build(PixelGridModel grid, Color32[] palette, BoardLayout layout)` (cấp phát `Matrix4x4[]`/`Vector4[]` theo batch 1023 + 1 `MaterialPropertyBlock`/batch, bảng `int[] cellToInstance` size W×H; gọi lại khi load level khác thì tái dùng mảng nếu đủ lớn); `void HideCell(Vector2Int cell)` (đặt matrix scale 0, đánh dấu batch dirty → set lại vector array cho batch đó ở LateUpdate); `void Clear()`; `LateUpdate` gọi `Graphics.RenderMeshInstanced(in RenderParams, cubeMesh, 0, matrices, count)` mỗi batch (`RenderParams.matProps` = MPB của batch). Cube scale = `CellSize * 0.92f` (khe hở giữa các ô).

- [ ] **Step 1: Viết test fail**
  - `Fit_ChoosesLimitingDimension_AndCenters` — 10×5 trong `Rect(0,0,20,20)` → `CellSize==2`, tâm ô `(0,0)` = `(1, 6, 0)`, tâm ô `(9,4)` = `(19, 14, 0)`.
  - `CellToWorld_StepsByCellSize`.
- [ ] **Step 2:** `RUN BoardLayoutTests` → FAIL.
- [ ] **Step 3:** Implement shader + 2 script; tạo `Assets/PixelFlow/Materials/PixelInstanced.mat` dùng shader, **Enable GPU Instancing = on**.
- [ ] **Step 4:** `RUN BoardLayoutTests` → PASS; Console không có lỗi shader.
- [ ] **Step 5: Kiểm tra trực quan** — scene tạm: camera orthographic nhìn `-Z`→`+Z`, GameObject có `PixelGridRenderer`, script test nhỏ (xóa sau) gọi `Build` với `Level_001`. Play → thấy hình 64×64 (dùng `Unity_SceneView_Capture2DScene`/`Unity_Camera_Capture` nếu chạy qua MCP). Frame Debugger: bảng vẽ bằng **≤ 5** draw call instanced (4096/1023).
- [ ] **Step 6: Commit** `feat(view): GPU-instanced pixel board renderer`

---

### Task 9: Views — `PixelCellView`, `DebrisFx`, `ProjectileSystem`, `ColorTankView`, `TankBoardView`, `GameHudView`

**Files:**
- Create: 6 file tương ứng trong `Assets/PixelFlow/Scripts/View/`

**Interfaces:**
- Consumes: `ObjectPool<T>`, `ColorTankModel`, `SupplyModel`, `SlotQueueManager`, `BoardLayout`.
- Produces:
  - `PixelCellView : MonoBehaviour` (prefab cube) — `void Play(Vector3 worldPos, Color32 color, float size, DebrisFx debris, Action<PixelCellView> onFinished)`: màu qua `MaterialPropertyBlock` static dùng chung, thu nhỏ `size → 0` trong `0.12 s` (ease-in) ở `Update`, cuối cùng `debris.Burst(worldPos, color)` rồi `onFinished(this)`. `onFinished` là delegate được cache bởi caller (không lambda mỗi lần).
  - `DebrisFx : MonoBehaviour` — 1 `ParticleSystem` (simulation World, `maxParticles 2000`, không emit tự động); `void Burst(Vector3 pos, Color32 color, int count = 6)` dùng `ParticleSystem.EmitParams` (struct cache trong field) — tái dùng buffer hạt có sẵn, không tạo object.
  - `ProjectileSystem : MonoBehaviour` — `[SerializeField] Transform projectilePrefab; float speed = 30f; int prewarm = 128;` `event Action<Vector2Int, Color32> OnArrived`; `void Launch(Vector3 from, Vector3 to, Vector2Int cell, Color32 color)`; `int ActiveCount`; `void ClearAll()` (trả hết về pool, **không** phát `OnArrived`). Lưu projectile đang bay trong mảng `struct ProjectileTween { Transform t; Vector3 from, to; float elapsed, duration; Vector2Int cell; Color32 color; }` và cập nhật trong **một** `Update` (swap-remove khi tới nơi). `duration = distance / speed`, nội suy ease-out quad.
  - `ColorTankView : MonoBehaviour` (prefab: thân `MeshRenderer`, `BoxCollider`, `TextMeshPro` world-space) — `ColorTankModel Model`; `void Bind(ColorTankModel model, Color32 color)` subscribe `OnAmmoChanged` → cập nhật text; `void Unbind()`; `void MoveTo(Vector3 target, float duration = 0.18f)` (tween trong Update); `Vector3 MuzzlePosition` (đỉnh thùng). Cập nhật text bằng `SetText("{0}", ammo)` (không alloc string).
  - `TankBoardView : MonoBehaviour` — `[SerializeField] ColorTankView tankPrefab; Transform supplyRoot, trayRoot; float laneSpacing, rowSpacing, slotSpacing;` `void Build(LevelSession session)` (lấy view từ `ObjectPool<ColorTankView>`, map `Dictionary<int, ColorTankView>` theo tank id, layout làn và slot, subscribe `Supply.OnLaneChanged`, `Tray.OnTankAdded/OnTankRemoved` → `MoveTo` vị trí mới, tank bị remove → Unbind + trả pool); `void Clear()` (unsubscribe + trả pool hết); `bool TryGetTank(Collider hit, out ColorTankModel tank)`; `Vector3 GetMuzzle(ColorTankModel tank)`.
  - `GameHudView : MonoBehaviour` (uGUI Canvas, có thể dùng prefab popup trong `Assets/Layer Lab/GUI Pro-SuperCasual` làm nền) — `event Action OnRetryClicked, OnNextClicked`; `void SetLevel(int levelNumber)`; `void ShowWin()`; `void ShowLose()`; `void HideAll()`.

- [ ] **Step 1:** Implement 6 script. Không có EditMode test (MonoBehaviour thuần trình bày); hành vi được kiểm ở Task 10.
- [ ] **Step 2:** Console không có lỗi compile; `RUN PixelFlow` vẫn PASS toàn bộ.
- [ ] **Step 3:** Kiểm tra: không script nào trong `Scripts/View` gọi `Instantiate`/`Destroy` ngoài factory của pool (`grep -rn "Instantiate\|Destroy(" Assets/PixelFlow/Scripts/View` chỉ ra các dòng nằm trong lambda `create` của pool).
- [ ] **Step 4: Commit** `feat(view): pooled cell, projectile, tank and HUD views`

---

### Task 10: `GameController` + scene builder — chơi được end-to-end

**Files:**
- Create: `Assets/PixelFlow/Scripts/Controller/GameController.cs`
- Create: `Assets/PixelFlow/Editor/PixelFlowSceneBuilder.cs`
- Create (sinh ra): `Assets/PixelFlow/Scenes/Game.unity`, `Assets/PixelFlow/Prefabs/{PixelCell,ColorTank,Projectile}.prefab`; thêm `Game.unity` vào Build Settings index 0.

**Interfaces:**
- Consumes: mọi thứ ở Task 2–9.
- Produces:
  - `GameController : MonoBehaviour` — `[SerializeField] LevelData[] levels; PixelGridRenderer gridRenderer; TankBoardView tankBoard; ProjectileSystem projectiles; PixelCellView cellViewPrefab; DebrisFx debris; GameHudView hud; Camera cam; Rect boardArea; float fireInterval = 0.06f; int destroysPerFrame = 64;` public `void LoadLevel(int index)`; `GameState State`.
  - Vòng đời:
    - `Awake`: `Application.targetFrameRate = 60`; tạo `ObjectPool<PixelCellView>` (prewarm 128), `TimeSlicedQueue<PendingDestroy>` (`struct PendingDestroy { Vector2Int cell; Color32 color; }`), `List<ShotEvent>` (capacity 16); cache mọi handler delegate vào field.
    - `LoadLevel`: `projectiles.ClearAll()`, `destroyQueue.Clear()`, `tankBoard.Clear()`, `gridRenderer.Clear()` → `LevelSession.Create(levels[index])` → `BoardLayout.Fit` → `gridRenderer.Build`, `tankBoard.Build`, `hud.SetLevel(index + 1)`, `hud.HideAll()`, `State = Playing`. Log thời gian load (`Stopwatch`).
    - `Update` khi `Playing`: (1) input — `Pointer.current.press.wasPressedThisFrame` → `Physics.Raycast` từ `cam` → `tankBoard.TryGetTank` → `lane = Supply.FindLaneWithFront(tank)`; chỉ khi `lane >= 0 && !Tray.IsFull` mới `TryTakeFront` + `Tray.TryAdd`. (2) timer bắn — mỗi `fireInterval`: `shots.Clear(); Shooting.Step(shots)` → mỗi shot `projectiles.Launch(tankBoard.GetMuzzle(shot.Tank), layout.CellToWorld(shot.Cell), shot.Cell, palette[shot.ColorId])`. (3) `projectiles.OnArrived` → `destroyQueue.Enqueue`. (4) `destroyQueue.Process(destroysPerFrame, handleDestroy)` → `gridRenderer.HideCell` + `cellPool.Get().Play(...)`. (5) `GameRules.Evaluate`; nếu `Won`/`Lost` **và** `projectiles.ActiveCount == 0 && destroyQueue.Count == 0` → set `State`, `hud.ShowWin()`/`ShowLose()`.
    - `hud.OnRetryClicked` → `LoadLevel(current)`; `OnNextClicked` → `LoadLevel((current + 1) % levels.Length)`. `OnDestroy` unsubscribe hết.
  - `PixelFlowSceneBuilder` menu `PixelFlow/Build Game Scene`: tạo 3 prefab (cube primitive bỏ collider cho PixelCell/Projectile; ColorTank có BoxCollider + TextMeshPro con dùng font `Assets/Layer Lab/GUI Pro-SuperCasual/ResourcesData/Fonts/Cairo SDF.asset`), scene mới với camera orthographic (portrait, `orthographicSize 10`, nền tối), các GameObject chứa component Task 8–9, `GameController` đã gán reference + `levels = [Level_001]`, `boardArea = Rect(-4.5, -1, 9, 9)`, supply/tray root ở phía dưới; lưu `Game.unity`, thêm vào Build Settings. Chạy lại builder phải ghi đè sạch (idempotent).

- [ ] **Step 1:** Implement 2 file.
- [ ] **Step 2:** Chạy menu `PixelFlow/Build Game Scene` → scene + prefab sinh ra, Console sạch.
- [ ] **Step 3: Kiểm tra end-to-end trong Play mode** (thủ công hoặc qua MCP + capture):
  - Load log < 1000 ms (kỳ vọng vài ms).
  - Chạm thùng đầu làn → thùng trượt vào slot, đạn bay lên đúng ô cùng màu đang lộ, ô thu nhỏ + văng mảnh, số đạn trên thùng giảm real-time.
  - Thùng hết đạn biến mất, thùng sau shift left.
  - Lấp đầy 5 slot bằng thùng không bắn được → popup Thua; Retry → level sạch, không còn đạn bay từ ván cũ.
  - Chơi đúng thứ tự (thùng id nhỏ trước) → popup Thắng khi hết pixel.
- [ ] **Step 4:** `RUN PixelFlow` toàn bộ → PASS.
- [ ] **Step 5: Commit** `feat(controller): GameController and generated Game scene`

---

### Task 11: Kiểm chứng hiệu năng 60 FPS

**Files:**
- Modify: `ProjectSettings` qua Editor (không sửa tay YAML): Android/iOS `Graphics APIs` giữ mặc định có GLES3/Vulkan/Metal (cần cho instancing); Quality mobile: tắt shadows, MSAA 2x hoặc off.
- Create: `Assets/PixelFlow/Tests/EditMode/StressTests.cs`

**Interfaces:**
- Consumes: `SampleLevelFactory`, `TankGenerator`, `LevelSession`, `AutoPlayer`.

- [ ] **Step 1: Viết test**
  - `Stress_5000Cells_FullAutoPlay_StepsUnder2msAverage` — level 100×50 procedural (sinh bằng `TankGenerator`), slot = số tank (luôn giải được); đo tổng thời gian các `Step` / số step < 2 ms, kết quả `Won`.
  - `Stress_FullAutoPlay_NoAllocationInSteadyState` — đo alloc quanh vòng lặp step (bỏ 10 step đầu) == 0.
- [ ] **Step 2:** `RUN StressTests` → PASS (nếu fail: superpowers:systematic-debugging, không nới ngưỡng).
- [ ] **Step 3: Profiler trong Editor** (Play `Game.unity`, bắn liên tục ~10 s): CPU Usage main thread < 16.6 ms/frame, **GC Alloc = 0 B/frame** ở trạng thái bắn ổn định (Hierarchy view, cột GC Alloc), Rendering: batches của bảng ≤ 5. Ghi số đo vào commit message.
- [ ] **Step 4 (nếu có thiết bị Android):** Build Development + Autoconnect Profiler, kiểm tra ≥ 58 FPS trung bình khi bắn. Không có thiết bị → ghi rõ "chưa đo trên thiết bị" trong báo cáo.
- [ ] **Step 5: Commit** `test(perf): stress tests and profiling settings`
