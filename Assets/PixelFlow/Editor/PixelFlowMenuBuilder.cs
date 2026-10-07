using PixelFlow.Meta;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PixelFlow.EditorTools
{
    /// <summary>
    /// Generates <c>Loading.unity</c> (logo, glossy cubes, progress bar; loads the menu) and <c>Menu.unity</c>
    /// (level path of hexagon nodes, Play, settings gear and popup) with every serialized reference assigned, then
    /// applies the Build Settings order Loading, Menu, Game. Rerunning overwrites both scenes.
    /// </summary>
    public static class PixelFlowMenuBuilder
    {
        private const string HexagonPath = MetaUiBuilder.SpriteFolder + "Components/Frame/BubbleFrame01_Hexagon_Bg_Blue.png";
        private const string HexagonGlowPath = MetaUiBuilder.SpriteFolder + "Components/Frame/BubbleFrame01_Hexagon_FocusGlow.png";
        private const string GlowCirclePath = MetaUiBuilder.SpriteFolder + "Demo/Demo_Image/Glow_Circle02.png";
        private const string PlayButtonPath = MetaUiBuilder.ComponentFolder + "Prefabs_Component_Buttons/Button_Round01_BtnText_Yellow.prefab";
        private const string LoadingBarPath = MetaUiBuilder.ComponentFolder + "Prefabs_Component_Sliders/Slider_Basic04_White.prefab";
        private const string BarFontPath = MetaUiBuilder.FontFolder + "Cairo_Line_Black SDF.asset";

        /// <summary>Number of hexagon nodes on the menu path (bottom = current level).</summary>
        public const int PathNodeCount = 4;

        // Colours picked from the reference screenshots.
        private static readonly Color MenuBottom = new Color32(0x1E, 0x7C, 0xF2, 0xFF);
        private static readonly Color MenuMiddle = new Color32(0x1C, 0xB2, 0xFF, 0xFF);
        private static readonly Color MenuTop = new Color32(0x24, 0x46, 0xE0, 0xFF);
        private static readonly Color LoadingBottom = new Color32(0x6F, 0xE6, 0xFF, 0xFF);
        private static readonly Color LoadingMiddle = new Color32(0x2E, 0xB6, 0xFF, 0xFF);
        private static readonly Color LoadingTop = new Color32(0x1F, 0x4C, 0xE6, 0xFF);
        private static readonly Color PathColor = new Color32(0xFF, 0xC4, 0x1E, 0xFF);
        private static readonly Color LogoTop = new Color32(0x4F, 0xD8, 0xFF, 0xFF);
        private static readonly Color LogoBottom = new Color32(0xFF, 0xCF, 0x2A, 0xFF);
        private static readonly Color BarFill = new Color32(0x3D, 0xDB, 0x4C, 0xFF);

        private static readonly Color[] CubeColors =
        {
            new Color32(0xFF, 0x4F, 0xB8, 0xFF), // pink
            new Color32(0xA2, 0x5B, 0xF5, 0xFF), // purple
            new Color32(0xFF, 0xC9, 0x2E, 0xFF), // yellow
            new Color32(0x2F, 0x8B, 0xFF, 0xFF), // blue
            new Color32(0x3F, 0xE0, 0xF0, 0xFF), // cyan
        };

        /// <summary>
        /// Menu command / <c>-executeMethod</c> entry point: builds the loading and menu scenes.
        /// </summary>
        [MenuItem("PixelFlow/Build Loading & Menu Scenes")]
        public static void BuildMenuScenes()
        {
            EnsureFolders();
            Sprite cube = MetaUiBuilder.CreateOrUpdateCubeSprite();
            Sprite loadingGradient = MetaUiBuilder.CreateOrUpdateGradient(MetaUiBuilder.LoadingGradientPath,
                LoadingBottom, LoadingMiddle, LoadingTop, 0.45f);
            Sprite menuGradient = MetaUiBuilder.CreateOrUpdateGradient(MetaUiBuilder.MenuGradientPath,
                MenuBottom, MenuMiddle, MenuTop, 0.5f);

            Scene loading = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildLoading(loadingGradient, cube);
            EditorSceneManager.SaveScene(loading, MetaUiBuilder.LoadingScenePath);

            Scene menu = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildMenu(menuGradient);
            EditorSceneManager.SaveScene(menu, MetaUiBuilder.MenuScenePath);

            MetaUiBuilder.ApplyBuildSettingsOrder();
            AssetDatabase.SaveAssets();
            Debug.Log($"[PixelFlowMenuBuilder] Built {MetaUiBuilder.LoadingScenePath} and {MetaUiBuilder.MenuScenePath}.");
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/PixelFlow/Scenes"))
                AssetDatabase.CreateFolder("Assets/PixelFlow", "Scenes");
            if (!AssetDatabase.IsValidFolder(PixelFlowSceneBuilder.TextureFolder))
                AssetDatabase.CreateFolder("Assets/PixelFlow", "Textures");
        }

        private static void BuildLoading(Sprite gradient, Sprite cube)
        {
            MetaUiBuilder.CreateUiCamera(LoadingTop);
            Canvas canvas = MetaUiBuilder.CreateCanvas("LoadingCanvas");
            Transform root = canvas.transform;
            MetaUiBuilder.CreateBackground(root, gradient);

            // Glossy cubes: a pile along the bottom plus a few floating ones (fixed seed, so rebuilds are stable).
            var cubes = MetaUiBuilder.CreateRect(root, "Cubes", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            MetaUiBuilder.Stretch(cubes);
            var random = new System.Random(11);
            for (int i = 0; i < 8; i++)
            {
                float x = -560f + i * 160f + random.Next(-30, 30);
                float y = -760f + random.Next(-40, 40);
                AddCube(cubes, cube, i, new Vector2(x, y), random.Next(230, 300), random.Next(-20, 20), random);
            }
            for (int i = 0; i < 7; i++)
            {
                float x = -500f + i * 170f + random.Next(-30, 30);
                float y = -930f + random.Next(-30, 30);
                AddCube(cubes, cube, 10 + i, new Vector2(x, y), random.Next(300, 380), random.Next(-16, 16), random);
            }
            AddCube(cubes, cube, 20, new Vector2(-470f, 260f), 150f, 14f, random);
            AddCube(cubes, cube, 21, new Vector2(500f, 80f), 190f, -12f, random);
            AddCube(cubes, cube, 22, new Vector2(-390f, -240f), 120f, 22f, random);
            AddCube(cubes, cube, 23, new Vector2(420f, -420f), 140f, -20f, random);
            AddCube(cubes, cube, 24, new Vector2(-250f, 860f), 70f, 18f, random);
            AddCube(cubes, cube, 25, new Vector2(230f, 900f), 60f, -14f, random);
            AddCube(cubes, cube, 26, new Vector2(330f, 820f), 46f, 10f, random);

            // Two-tone logo.
            TMP_FontAsset titleFont = MetaUiBuilder.LoadFont(MetaUiBuilder.TitleFontPath);
            var logo = MetaUiBuilder.CreateRect(root, "Logo", new Vector2(0.5f, 0.5f), new Vector2(0f, 560f),
                new Vector2(900f, 460f));
            TextMeshProUGUI pixel = MetaUiBuilder.CreateText(logo, "Pixel", titleFont, "PIXEL", 210f,
                new Vector2(0f, 105f), new Vector2(900f, 230f), LogoTop);
            pixel.characterSpacing = -4f;
            TextMeshProUGUI flow = MetaUiBuilder.CreateText(logo, "Flow", titleFont, "FLOW", 240f,
                new Vector2(0f, -110f), new Vector2(900f, 250f), LogoBottom);
            flow.characterSpacing = -4f;

            // Progress bar with the "Loading" label.
            GameObject barGo = MetaUiBuilder.InstantiateUnpacked(LoadingBarPath, root, "LoadingBar");
            var barRect = (RectTransform)barGo.transform;
            barRect.anchorMin = barRect.anchorMax = new Vector2(0.5f, 0f);
            barRect.anchoredPosition = new Vector2(0f, 230f);
            barRect.sizeDelta = new Vector2(860f, 110f);
            var slider = barGo.GetComponent<Slider>();
            slider.interactable = false;
            slider.transition = Selectable.Transition.None;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0f;
            Transform fill = barGo.transform.Find("Fill Area/Fill");
            if (fill != null)
                fill.GetComponent<Image>().color = BarFill;
            var label = barGo.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = "Loading";
                label.font = MetaUiBuilder.LoadFont(BarFontPath);
                label.fontSize = 60f;
                label.enableAutoSizing = false;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.rectTransform.sizeDelta = new Vector2(500f, 100f);
                label.raycastTarget = false;
            }

            MetaUiBuilder.CreateEventSystem();

            var screen = new GameObject("LoadingScreen").AddComponent<LoadingScreen>();
            var so = new SerializedObject(screen);
            so.FindProperty("progressBar").objectReferenceValue = slider;
            so.FindProperty("minimumSeconds").floatValue = 1.5f;
            so.FindProperty("sceneName").stringValue = SceneFlow.MenuScene;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddCube(Transform parent, Sprite sprite, int index, Vector2 position, float size,
            float angle, System.Random random)
        {
            Color color = CubeColors[random.Next(CubeColors.Length)];
            Image image = MetaUiBuilder.CreateImage(parent, "Cube" + index, sprite, position, new Vector2(size, size), color);
            image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private static void BuildMenu(Sprite gradient)
        {
            MetaUiBuilder.CreateUiCamera(MenuTop);
            Canvas canvas = MetaUiBuilder.CreateCanvas("MenuCanvas");
            Transform root = canvas.transform;
            MetaUiBuilder.CreateBackground(root, gradient);

            TMP_FontAsset titleFont = MetaUiBuilder.LoadFont(MetaUiBuilder.TitleFontPath);
            Sprite hexagon = MetaUiBuilder.LoadSprite(HexagonPath);
            Sprite hexagonGlow = MetaUiBuilder.LoadSprite(HexagonGlowPath);
            Sprite glowCircle = MetaUiBuilder.LoadSprite(GlowCirclePath);

            // Level path: a yellow bar behind the hexagons, the current level at the bottom with a glow.
            var path = MetaUiBuilder.CreateRect(root, "LevelPath", new Vector2(0.5f, 0.5f), new Vector2(0f, 120f),
                new Vector2(600f, 1300f));
            float[] nodeY = { -380f, 0f, 360f, 690f };
            float[] nodeSize = { 300f, 250f, 250f, 250f };
            // The farthest node is darkened (opaque, so the bar does not show through).
            Color[] nodeTint = { Color.white, Color.white, Color.white, new Color(0.55f, 0.68f, 0.95f, 1f) };
            Image bar = MetaUiBuilder.CreateImage(path, "Bar", null, new Vector2(0f, (nodeY[0] + nodeY[3]) * 0.5f),
                new Vector2(34f, nodeY[3] - nodeY[0]), PathColor);
            bar.rectTransform.SetAsFirstSibling();
            MetaUiBuilder.CreateImage(path, "Glow", glowCircle, new Vector2(0f, nodeY[0]), new Vector2(760f, 760f),
                new Color(0.62f, 0.96f, 1f, 0.75f));

            var labels = new TMP_Text[PathNodeCount];
            for (int i = 0; i < PathNodeCount; i++)
            {
                float size = nodeSize[i];
                Color tint = nodeTint[i];
                var node = MetaUiBuilder.CreateRect(path, "Node" + i, new Vector2(0.5f, 0.5f), new Vector2(0f, nodeY[i]),
                    new Vector2(size, size * 0.95f));
                if (i == 0)
                    MetaUiBuilder.CreateImage(node, "FocusGlow", hexagonGlow, Vector2.zero,
                        new Vector2(size * 1.25f, size * 1.2f), Color.white);
                Image hex = MetaUiBuilder.CreateImage(node, "Hexagon", hexagon, Vector2.zero, node.sizeDelta, tint);
                hex.preserveAspect = true;
                labels[i] = MetaUiBuilder.CreateText(node, "Number", titleFont, (i + 1).ToString(), i == 0 ? 130f : 110f,
                    new Vector2(0f, 6f), node.sizeDelta, tint);
            }

            // Play.
            GameObject playGo = MetaUiBuilder.InstantiateUnpacked(PlayButtonPath, root, "PlayButton");
            var playRect = (RectTransform)playGo.transform;
            playRect.anchorMin = playRect.anchorMax = new Vector2(0.5f, 0f);
            playRect.anchoredPosition = new Vector2(0f, 330f);
            playRect.sizeDelta = new Vector2(600f, 220f);
            var playText = playGo.GetComponentInChildren<TMP_Text>(true);
            playText.text = "Play";
            playText.font = titleFont;
            playText.color = Color.white;
            playText.fontSize = 120f;
            playText.enableAutoSizing = false;
            playText.raycastTarget = false;
            Button play = MetaUiBuilder.NoNavigation(playGo.GetComponent<Button>());

            Button gear = MetaUiBuilder.CreateGearButton(root);

            // The popup lives on its own canvas above the menu.
            Canvas popupCanvas = MetaUiBuilder.CreateCanvas("PopupCanvas", 10);
            SettingsPopup popup = MetaUiBuilder.CreateSettingsPopup(popupCanvas.transform, false);

            MetaUiBuilder.CreateEventSystem();

            var screen = canvas.gameObject.AddComponent<MenuScreen>();
            var so = new SerializedObject(screen);
            SerializedProperty labelArray = so.FindProperty("nodeLabels");
            labelArray.arraySize = labels.Length;
            for (int i = 0; i < labels.Length; i++)
                labelArray.GetArrayElementAtIndex(i).objectReferenceValue = labels[i];
            so.FindProperty("playButton").objectReferenceValue = play;
            so.FindProperty("settingsButton").objectReferenceValue = gear;
            so.FindProperty("settingsPopup").objectReferenceValue = popup;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
