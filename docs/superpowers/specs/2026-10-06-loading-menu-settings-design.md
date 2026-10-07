# Loading, Menu & Settings — Design

**Status:** approved in chat (2026-10-06).

**Reference:** two screenshots of Pixel Flow: a title/loading screen (logo "PIXEL FLOW", glossy cubes, a green "Loading" bar) and the lobby (a vertical path of blue hexagon level nodes, the current level glowing at the bottom, a yellow "Play" button, a settings gear top right).

## 1. Flow

- Build Settings order: `Loading` (0) → `Menu` (1) → `Game` (2). Other scenes follow.
- **Loading**: loads `Menu` with `SceneManager.LoadSceneAsync`, keeps the screen up for at least `1.5 s`, drives the progress bar (labelled "Loading"), then activates `Menu`.
- **Menu**: `Play` loads `Game`. The gear opens the settings popup.
- **Game**: a gear button on the HUD opens the settings popup. In the Game scene the popup also has a `Home` button. The win and lose popups also get a `Home` button. Home loads `Menu`. The game is paused while the settings popup is open: the belt does not tick and taps are ignored.

## 2. Progress

- `PlayerProgress.LevelNumber`: 1-based and unbounded. The default is 1. It is stored under the key `pf.levelNumber`.
- The level data index is `(LevelNumber - 1) % levelCount`. After the last level the game wraps around while the number keeps growing (Level 7 plays level 1's data).
- `CompleteLevel(n)` sets `LevelNumber = max(LevelNumber, n + 1)`. Replaying an old number never moves progress back.
- `GameController`:
  - `Start` loads `LevelNumber` (via the new `LoadLevelNumber(int)`).
  - On Won it calls `CompleteLevel(currentNumber)`.
  - Next loads `currentNumber + 1`.
  - Retry and Lost do not change progress.
  - `LoadLevel(int index)` keeps its behaviour and sets the number to `index + 1`.
  - The HUD shows the number.

## 3. Settings

- `GameSettings`: `Music`, `Sfx` and `Vibration` (bool, default on). They are stored under the keys `pf.music`, `pf.sfx` and `pf.vibration` (1/0). A `Changed` event is raised when a value really changes.
- There is no audio in the game yet. Music and SFX are stored and exposed for future audio. Vibration is used now: `TryVibrate()` calls `Handheld.Vibrate()` on Android/iOS when enabled. The controller calls it when a level is won or lost.

## 4. Code (`Assets/PixelFlow/Scripts/Meta/`, assembly `PixelFlow.Meta`)

| Type | Role |
|---|---|
| `IKeyValueStore`, `PlayerPrefsStore`, `MemoryStore` | int key/value persistence. `MemoryStore` is for tests. |
| `MetaServices` | Static access to the shared `Store`, `Progress` and `Settings`. `Use(store)` swaps the store, which tests need. |
| `PlayerProgress`, `GameSettings` | Plain C# over a store. |
| `SceneFlow` | Scene names, `LoadMenu()`, `LoadGame()`. |
| `ToggleSwitchView` | The pack's ON/OFF switch: a button, a fill, a sliding handle and an ON/OFF label. |
| `SettingsPopup` | Three switches, a close button, an optional Home button (`OnHomeClicked`) and `IsOpen`. |
| `LoadingScreen` | The async load plus the progress bar. |
| `MenuScreen` | Labels the hexagon nodes `LevelNumber + i` (bottom = current). Play and gear buttons. |

The View layer (`GameHudView`) gets `settingsButtons` and `homeButtons` with `OnSettingsClicked` and `OnHomeClicked`. View does not depend on Meta. The Controller references Meta.

## 5. Scenes (generated, like `PixelFlowSceneBuilder`)

- The menu item `PixelFlow/Build Loading & Menu Scenes` (`PixelFlowMenuBuilder`) writes `Assets/PixelFlow/Scenes/Loading.unity` and `Menu.unity`.
- Generated textures:
  - `Textures/UiGradient.png`: a vertical blue gradient.
  - `Textures/UiCube.png`: a white glossy rounded square, tinted per cube.
- **Loading**: the gradient background, glossy cubes along the bottom, a two-tone TMP logo ("PIXEL" sky blue, "FLOW" yellow) and the pack's `Slider_Basic04` bar (from `Title_Loading`) with the label "Loading".
- **Menu**: the gradient background and a top bar with the gear (`Button_Round03_Dark` + `Icon_Setting`). It has 4 hexagon nodes (`BubbleFrame01_Hexagon_Bg_Blue`) joined by a yellow bar:
  - the bottom node is the current level, larger, with `FocusGlow`;
  - the 3 nodes above are dimmed.

  Below the nodes is a big yellow `Play` (`Button_Round01_Yellow`).
- **Settings popup** (shared): an instance of the pack's `Settings.prefab`.
  - The pack scripts are removed.
  - Rows:
    - Music and SFX get a switch instead of the slider.
    - Screenshake becomes Vibration.
    - Language, Notification and the four bottom buttons are hidden.
  - The popup height is reduced.
  - A blue `Home` button is added (only active in Game).
- The Game builder adds the HUD gear, the popup with Home, and Home buttons on the win/lose popups. Both builders apply the shared Build Settings order.

## 6. Testing

- EditMode:
  - `PlayerProgress`: default, completion, no regression, index wrap.
  - `GameSettings`: defaults, persistence, `Changed` only on a real change.
  - `MemoryStore`.
- PlayMode: existing tests run with `MetaServices.Use(new MemoryStore())`, so `Start` still loads level index 0 and real PlayerPrefs are untouched.
- Manual: rebuild all scenes, play `Loading → Menu → Game → Home`, and capture screenshots.

## 7. Implementation steps

1. Meta assembly: store, progress, settings, scene flow, with EditMode tests first.
2. UI components: `ToggleSwitchView`, `SettingsPopup`, `LoadingScreen`, `MenuScreen`.
3. `GameHudView` (settings/home events) and `GameController` (progress, pause, home, vibration). Test asmdefs reference Meta; PlayMode tests use `MemoryStore`.
4. Editor: shared UI helpers + `PixelFlowMenuBuilder`; extend `PixelFlowSceneBuilder`; Build Settings order.
5. Build the scenes in Unity, run the tests, play the flow, capture screenshots.
