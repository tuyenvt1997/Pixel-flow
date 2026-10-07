using System.Collections.Generic;
using System.IO;
using PixelFlow.Meta;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace PixelFlow.EditorTools
{
    /// <summary>
    /// uGUI building blocks shared by the scene builders: the portrait canvas, pack prefab instances, generated
    /// gradient / glossy cube sprites, the settings popup (from the pack's <c>Settings.prefab</c>) and the Build
    /// Settings order Loading, Menu, Game.
    /// </summary>
    public static class MetaUiBuilder
    {
        /// <summary>Project path of the generated loading scene.</summary>
        public const string LoadingScenePath = "Assets/PixelFlow/Scenes/" + SceneFlow.LoadingScene + ".unity";

        /// <summary>Project path of the generated menu scene.</summary>
        public const string MenuScenePath = "Assets/PixelFlow/Scenes/" + SceneFlow.MenuScene + ".unity";

        /// <summary>Generated vertical gradient sprite of the menu background.</summary>
        public const string MenuGradientPath = PixelFlowSceneBuilder.TextureFolder + "/UiGradient_Menu.png";

        /// <summary>Generated vertical gradient sprite of the loading background.</summary>
        public const string LoadingGradientPath = PixelFlowSceneBuilder.TextureFolder + "/UiGradient_Loading.png";

        /// <summary>Generated white glossy rounded-square sprite, tinted per decorative cube.</summary>
        public const string CubeSpritePath = PixelFlowSceneBuilder.TextureFolder + "/UiCube.png";

        /// <summary>Folder of the pack's demo panel prefabs.</summary>
        public const string PanelFolder = PixelFlowSceneBuilder.UiPackPath + "/Prefabs/Prefabs_DemoScene_Panels/";

        /// <summary>Folder of the pack's component prefabs.</summary>
        public const string ComponentFolder = PixelFlowSceneBuilder.UiPackPath + "/Prefabs/";

        /// <summary>Folder of the pack's sprites.</summary>
        public const string SpriteFolder = PixelFlowSceneBuilder.UiPackPath + "/ResourcesData/Sprites/";

        /// <summary>Folder of the pack's TMP fonts.</summary>
        public const string FontFolder = PixelFlowSceneBuilder.UiPackPath + "/ResourcesData/Fonts/";

        /// <summary>Bold white font with a thick black outline (titles, big buttons, hexagon numbers).</summary>
        public const string TitleFontPath = FontFolder + "Sen_LIne_l_Black SDF.asset";

        /// <summary>Pack Settings panel the popup is made from.</summary>
        public const string SettingsPrefabPath = PanelFolder + "Settings.prefab";

        /// <summary>Pack round dark icon button (the gear).</summary>
        public const string GearButtonPath = ComponentFolder + "Prefabs_Component_Buttons/Button_Round03_BtnIcon_Dark.prefab";

        /// <summary>Pack gear icon.</summary>
        public const string GearIconPath = SpriteFolder + "Demo/Demo_Icon/Icon_Setting.png";

        /// <summary>Pack glossy blue pill sprite (secondary buttons).</summary>
        public const string BluePillPath = SpriteFolder + "Components/Button/Button01_s_Blue.png";

        private const int GradientHeight = 256;
        private const int CubeSize = 128;

        /// <summary>
        /// Creates a Screen Space Overlay canvas scaled for a 1080x1920 portrait reference.
        /// </summary>
        public static Canvas CreateCanvas(string name, int sortingOrder = 0)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>
        /// Creates the EventSystem with the Input System UI module.
        /// </summary>
        public static void CreateEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        /// <summary>
        /// Creates a camera that only clears to <paramref name="background"/> (UI-only scenes).
        /// </summary>
        public static void CreateUiCamera(Color background)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.cullingMask = 0;
            cam.orthographic = true;
            go.AddComponent<AudioListener>();
        }

        /// <summary>
        /// Creates a RectTransform child anchored at <paramref name="anchor"/> with the given position and size.
        /// </summary>
        public static RectTransform CreateRect(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>
        /// Stretches <paramref name="rect"/> over its whole parent.
        /// </summary>
        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// Creates an Image child (no raycasts) centred at <paramref name="position"/>.
        /// </summary>
        public static Image CreateImage(Transform parent, string name, Sprite sprite, Vector2 position, Vector2 size,
            Color color)
        {
            RectTransform rect = CreateRect(parent, name, new Vector2(0.5f, 0.5f), position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            if (sprite != null && sprite.border != Vector4.zero)
                image.type = Image.Type.Sliced;
            return image;
        }

        /// <summary>
        /// Creates a full-screen background Image.
        /// </summary>
        public static Image CreateBackground(Transform parent, Sprite sprite)
        {
            Image image = CreateImage(parent, "Background", sprite, Vector2.zero, Vector2.zero, Color.white);
            Stretch(image.rectTransform);
            return image;
        }

        /// <summary>
        /// Creates a centred TMP label (no raycasts, no wrapping).
        /// </summary>
        public static TextMeshProUGUI CreateText(Transform parent, string name, TMP_FontAsset font, string value,
            float size, Vector2 position, Vector2 rectSize, Color color)
        {
            RectTransform rect = CreateRect(parent, name, new Vector2(0.5f, 0.5f), position, rectSize);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }

        /// <summary>
        /// Instantiates a pack prefab under <paramref name="parent"/>, unpacks it completely (so children can be
        /// removed) and strips the pack's demo scripts.
        /// </summary>
        public static GameObject InstantiateUnpacked(string prefabPath, Transform parent, string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                throw new System.InvalidOperationException($"[MetaUiBuilder] Missing pack prefab '{prefabPath}'.");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            foreach (MonoBehaviour behaviour in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null && behaviour.GetType().Namespace != null &&
                    behaviour.GetType().Namespace.StartsWith("LayerLab"))
                    Object.DestroyImmediate(behaviour);
            }
            return go;
        }

        /// <summary>
        /// The pack's round dark button with the gear icon, anchored to the top-right corner.
        /// </summary>
        public static Button CreateGearButton(Transform parent)
        {
            GameObject go = InstantiateUnpacked(GearButtonPath, parent, "SettingsButton");
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-36f, -40f);
            rect.sizeDelta = new Vector2(140f, 132f);
            DestroyChild(go.transform, "Alert_Dot_Red");
            Transform icon = go.transform.Find("Icon");
            if (icon != null)
            {
                var image = icon.GetComponent<Image>();
                image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(GearIconPath);
                image.raycastTarget = false;
                image.preserveAspect = true;
                ((RectTransform)icon).sizeDelta = new Vector2(84f, 84f);
            }
            return NoNavigation(go.GetComponent<Button>());
        }

        /// <summary>
        /// A glossy blue pill button with a white label.
        /// </summary>
        public static Button CreatePillButton(Transform parent, string name, string label, Vector2 anchor,
            Vector2 position, Vector2 size)
        {
            RectTransform rect = CreateRect(parent, name, anchor, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BluePillPath);
            image.type = Image.Type.Sliced;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            TextMeshProUGUI text = CreateText(rect, "Text", LoadFont(PixelFlowSceneBuilder.HudFontPath), label, 60f,
                new Vector2(0f, 4f), Vector2.zero, Color.white);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(16f, 8f);
            text.rectTransform.offsetMax = new Vector2(-16f, -4f);
            return NoNavigation(button);
        }

        /// <summary>
        /// Builds the settings popup under <paramref name="parent"/> from the pack's Settings panel: Music and SFX
        /// rows get an ON/OFF switch instead of their slider, Screenshake becomes Vibration, the Language and
        /// Notification rows and the four account buttons are removed, the frame is shortened, and (if
        /// <paramref name="withHome"/>) a blue Home button is added. Returned inactive.
        /// </summary>
        public static SettingsPopup CreateSettingsPopup(Transform parent, bool withHome)
        {
            GameObject root = InstantiateUnpacked(SettingsPrefabPath, parent, "SettingsPopup");
            Transform frame = root.transform.Find("Popup08_Topbar_Divided");
            Transform list = frame.Find("Middle/Group_List");

            Transform switchTemplate = list.Find("Notification/Switch_On");
            ToggleSwitchView music = ReplaceSliderWithSwitch(list.Find("Music"), switchTemplate);
            ToggleSwitchView sfx = ReplaceSliderWithSwitch(list.Find("SFX"), switchTemplate);
            Transform vibrationRow = list.Find("Screenshake");
            vibrationRow.name = "Vibration";
            SetRowLabel(vibrationRow, "Vibration :");
            ToggleSwitchView vibration = MakeSwitch(vibrationRow.Find("Switch_Off"));

            DestroyChild(list, "Language");
            DestroyChild(list, "Notification");
            DestroyChild(frame, "Button_Privacy");
            DestroyChild(frame, "Button_TermsOfService");
            DestroyChild(frame, "Button_DeleteAccount");
            DestroyChild(frame, "Button_Help");

            // Shorter frame: top bar (122) + three rows + the optional Home button at the bottom.
            float bottom = withHome ? 250f : 70f;
            var frameRect = (RectTransform)frame;
            frameRect.sizeDelta = new Vector2(frameRect.sizeDelta.x, 122f + 420f + bottom);
            var middle = (RectTransform)frame.Find("Middle");
            middle.offsetMin = new Vector2(middle.offsetMin.x, bottom);
            middle.offsetMax = new Vector2(middle.offsetMax.x, -122f);
            var listRect = (RectTransform)list;
            listRect.sizeDelta = new Vector2(listRect.sizeDelta.x, 380f);
            listRect.anchoredPosition = Vector2.zero;

            Button home = null;
            if (withHome)
                home = CreatePillButton(frame, "HomeButton", "Home", new Vector2(0.5f, 0f), new Vector2(0f, 135f),
                    new Vector2(440f, 130f));

            Button close = NoNavigation(root.transform.Find("Button_Close03").GetComponent<Button>());

            var popup = root.AddComponent<SettingsPopup>();
            var so = new SerializedObject(popup);
            so.FindProperty("musicSwitch").objectReferenceValue = music;
            so.FindProperty("sfxSwitch").objectReferenceValue = sfx;
            so.FindProperty("vibrationSwitch").objectReferenceValue = vibration;
            so.FindProperty("closeButton").objectReferenceValue = close;
            so.FindProperty("homeButton").objectReferenceValue = home;
            so.ApplyModifiedPropertiesWithoutUndo();
            root.SetActive(false);
            return popup;
        }

        private static ToggleSwitchView ReplaceSliderWithSwitch(Transform row, Transform switchTemplate)
        {
            Transform slider = row.Find("Slider_Handle_Pink");
            Object.DestroyImmediate(slider.gameObject);
            Transform copy = Object.Instantiate(switchTemplate, row, false);
            copy.name = "Switch";
            return MakeSwitch(copy);
        }

        private static void SetRowLabel(Transform row, string label)
        {
            var text = row.Find("Text (TMP)").GetComponent<TMP_Text>();
            text.text = label;
        }

        /// <summary>
        /// Turns a pack switch (Bg, Fill, Handle/Text) into a <see cref="ToggleSwitchView"/> with a Button on its Bg.
        /// </summary>
        private static ToggleSwitchView MakeSwitch(Transform switchRoot)
        {
            switchRoot.name = "Switch";
            var bg = switchRoot.Find("Bg").GetComponent<Image>();
            bg.raycastTarget = true;
            var button = switchRoot.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            NoNavigation(button);
            var handle = (RectTransform)switchRoot.Find("Handle");
            handle.GetComponent<Image>().raycastTarget = false;
            var label = handle.GetComponentInChildren<TMP_Text>(true);
            label.raycastTarget = false;
            Transform fill = switchRoot.Find("Fill");
            fill.GetComponent<Image>().raycastTarget = false;

            var view = switchRoot.gameObject.AddComponent<ToggleSwitchView>();
            var so = new SerializedObject(view);
            so.FindProperty("button").objectReferenceValue = button;
            so.FindProperty("fill").objectReferenceValue = fill.gameObject;
            so.FindProperty("handle").objectReferenceValue = handle;
            so.FindProperty("label").objectReferenceValue = label;
            so.FindProperty("handleOffset").floatValue = Mathf.Abs(handle.anchoredPosition.x);
            so.ApplyModifiedPropertiesWithoutUndo();
            return view;
        }

        /// <summary>
        /// Disables keyboard/gamepad navigation (touch-only UI; avoids the selected-state tint after a click).
        /// </summary>
        public static Button NoNavigation(Button button)
        {
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            return button;
        }

        /// <summary>
        /// Destroys the direct child <paramref name="childName"/> of <paramref name="root"/> (warns if missing).
        /// </summary>
        public static void DestroyChild(Transform root, string childName)
        {
            Transform child = root.Find(childName);
            if (child != null)
                Object.DestroyImmediate(child.gameObject);
            else
                Debug.LogWarning($"[MetaUiBuilder] '{root.name}' has no child '{childName}'.");
        }

        /// <summary>
        /// Loads a TMP font asset (throws if missing).
        /// </summary>
        public static TMP_FontAsset LoadFont(string path)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font == null)
                throw new System.InvalidOperationException($"[MetaUiBuilder] Missing font '{path}'.");
            return font;
        }

        /// <summary>
        /// Loads a pack sprite (throws if missing).
        /// </summary>
        public static Sprite LoadSprite(string path)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                throw new System.InvalidOperationException($"[MetaUiBuilder] Missing sprite '{path}'.");
            return sprite;
        }

        /// <summary>
        /// Writes (or rewrites) a vertical three-stop gradient sprite: <paramref name="bottom"/> at the bottom,
        /// <paramref name="middle"/> at <paramref name="middleAt"/> (0..1 from the bottom), <paramref name="top"/> at
        /// the top.
        /// </summary>
        public static Sprite CreateOrUpdateGradient(string path, Color bottom, Color middle, Color top, float middleAt)
        {
            var pixels = new Color32[GradientHeight * 4];
            for (int y = 0; y < GradientHeight; y++)
            {
                float t = y / (GradientHeight - 1f);
                Color c = t < middleAt
                    ? Color.Lerp(bottom, middle, Mathf.SmoothStep(0f, 1f, t / middleAt))
                    : Color.Lerp(middle, top, Mathf.SmoothStep(0f, 1f, (t - middleAt) / (1f - middleAt)));
                for (int x = 0; x < 4; x++)
                    pixels[y * 4 + x] = c;
            }
            return WriteSprite(path, 4, GradientHeight, pixels);
        }

        /// <summary>
        /// Writes (or rewrites) <see cref="CubeSpritePath"/>: a white glossy rounded square (light top face band,
        /// darker bottom lip, soft highlight) meant to be tinted per cube.
        /// </summary>
        public static Sprite CreateOrUpdateCubeSprite()
        {
            const int size = CubeSize;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float v = (y + 0.5f) / size;
                    pixels[y * size + x] = CubePixel(u, v, 1f / size);
                }
            }
            return WriteSprite(CubeSpritePath, size, size, pixels);
        }

        private static Color32 CubePixel(float u, float v, float texel)
        {
            const float radius = 0.22f;
            // Rounded-square signed distance (negative inside).
            float dx = Mathf.Max(Mathf.Abs(u - 0.5f) - (0.5f - radius), 0f);
            float dy = Mathf.Max(Mathf.Abs(v - 0.5f) - (0.5f - radius), 0f);
            float dist = Mathf.Sqrt(dx * dx + dy * dy) - radius;
            float alpha = Mathf.Clamp01(0.5f - dist / texel);

            // Shade: darker lip at the bottom, brighter band at the top, soft vertical falloff.
            float shade = Mathf.Lerp(0.78f, 1f, Mathf.SmoothStep(0f, 1f, v));
            if (v < 0.14f)
                shade *= 0.72f;
            if (v > 0.84f)
                shade = Mathf.Min(1f, shade * 1.08f);

            // Gloss blob near the top-left.
            float gx = (u - 0.3f) / 0.16f;
            float gy = (v - 0.76f) / 0.07f;
            float gloss = Mathf.Clamp01(1f - (gx * gx + gy * gy)) * 0.85f;

            float c = Mathf.Lerp(shade, 1f, gloss);
            byte b = (byte)Mathf.RoundToInt(c * 255f);
            return new Color32(b, b, b, (byte)Mathf.RoundToInt(alpha * 255f));
        }

        private static Sprite WriteSprite(string path, int width, int height, Color32[] pixels)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled ||
                importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>
        /// Puts Loading, Menu and Game (those that exist) first in Build Settings, in that order, keeping every other
        /// scene after them.
        /// </summary>
        public static void ApplyBuildSettingsOrder()
        {
            string[] ordered = { LoadingScenePath, MenuScenePath, PixelFlowSceneBuilder.ScenePath };
            var scenes = new List<EditorBuildSettingsScene>();
            foreach (string path in ordered)
            {
                if (File.Exists(path))
                    scenes.Add(new EditorBuildSettingsScene(path, true));
            }
            foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
            {
                if (System.Array.IndexOf(ordered, existing.path) < 0)
                    scenes.Add(existing);
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
