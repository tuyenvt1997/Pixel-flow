using System.Collections.Generic;
using System.IO;
using PixelFlow.Controller;
using PixelFlow.Data;
using PixelFlow.View;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PixelFlow.EditorTools
{
    /// <summary>
    /// Generates the playable game: view materials, the PixelCell / ColorTank / Projectile / Debris prefabs and
    /// <c>Assets/PixelFlow/Scenes/Game.unity</c> with every serialized reference assigned, then puts the scene at
    /// Build Settings index 0. Rerunning overwrites the generated assets in place (asset GUIDs are kept).
    /// </summary>
    public static class PixelFlowSceneBuilder
    {
        /// <summary>
        /// Project path of the generated game scene.
        /// </summary>
        public const string ScenePath = "Assets/PixelFlow/Scenes/Game.unity";

        /// <summary>
        /// Folder of the generated prefabs.
        /// </summary>
        public const string PrefabFolder = "Assets/PixelFlow/Prefabs";

        /// <summary>
        /// Folder of the generated materials.
        /// </summary>
        public const string MaterialFolder = "Assets/PixelFlow/Materials";

        /// <summary>
        /// TMP font asset used by the tank ammo labels and the HUD.
        /// </summary>
        public const string FontPath = "Assets/Layer Lab/GUI Pro-SuperCasual/ResourcesData/Fonts/Cairo SDF.asset";

        /// <summary>
        /// Camera orthographic size (half the visible height in world units, portrait).
        /// </summary>
        public const float CameraSize = 10f;

        /// <summary>
        /// World rectangle the board is fitted into (top part of the screen).
        /// </summary>
        public static readonly Rect BoardArea = new Rect(-4.5f, -1f, 9f, 9f);

        /// <summary>
        /// Centre of the tray slots (below the board).
        /// </summary>
        public static readonly Vector3 TrayPosition = new Vector3(0f, -2.6f, 0f);

        /// <summary>
        /// Front row of the supply lanes (below the tray); later rows extend further down.
        /// </summary>
        public static readonly Vector3 SupplyPosition = new Vector3(0f, -4.6f, 0f);

        private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        private static readonly Color BackgroundColor = new Color(0.07f, 0.08f, 0.11f, 1f);

        /// <summary>
        /// Menu command / <c>-executeMethod</c> entry point: builds materials, prefabs and the game scene.
        /// </summary>
        [MenuItem("PixelFlow/Build Game Scene")]
        public static void BuildGameScene()
        {
            if (!HasTmpEssentials())
                return;
            EnsureFolder("Assets/PixelFlow", "Prefabs");
            EnsureFolder("Assets/PixelFlow", "Scenes");
            EnsureFolder("Assets/PixelFlow", "Materials");

            // Open the new scene first: NewScene unloads unused assets, which would invalidate references
            // to assets loaded or saved before it.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            var level = AssetDatabase.LoadAssetAtPath<LevelData>(SampleLevelFactory.AssetPath);
            var boardMaterial = AssetDatabase.LoadAssetAtPath<Material>(BoardRenderTools.MaterialPath);
            if (font == null || level == null || boardMaterial == null)
            {
                Debug.LogError($"[PixelFlowSceneBuilder] Missing input asset (font: {font != null}, level: {level != null}, board material: {boardMaterial != null}).");
                return;
            }

            Material viewMaterial = CreateOrUpdateMaterial(MaterialFolder + "/ViewUnlit.mat", "Universal Render Pipeline/Unlit");
            Material particleMaterial = CreateOrUpdateMaterial(MaterialFolder + "/DebrisParticle.mat", "Universal Render Pipeline/Particles/Unlit");
            // The destroy-animation cube uses the board's shader so a cell keeps its exact shading (no brightness
            // pop) when the instanced cell is hidden and the pooled cube takes over. Both colour paths are linear:
            // the board writes Color.linear via SetVectorArray, PixelCellView uses SetColor (gamma -> linear).
            Material cellMaterial = CreateOrUpdateMaterial(MaterialFolder + "/CellInstanced.mat", BoardRenderTools.ShaderName);
            if (viewMaterial == null || particleMaterial == null || cellMaterial == null)
                return;

            PixelCellView cellPrefab = BuildCellPrefab(cellMaterial);
            ColorTankView tankPrefab = BuildTankPrefab(viewMaterial, font);
            Transform projectilePrefab = BuildProjectilePrefab(viewMaterial);
            DebrisFx debrisPrefab = BuildDebrisPrefab(particleMaterial);

            BuildScene(scene, level, boardMaterial, font, cellPrefab, tankPrefab, projectilePrefab, debrisPrefab);
            AddSceneToBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log($"[PixelFlowSceneBuilder] Built {ScenePath} and prefabs in {PrefabFolder}.");
        }

        private static bool HasTmpEssentials()
        {
            if (File.Exists(TmpSettingsPath))
                return true;

            // The TMP SDF shaders used by the Cairo font ship with TMP Essential Resources (project assets).
            Debug.LogError("[PixelFlowSceneBuilder] TMP Essential Resources are missing. Import them via " +
                           "Window > TextMeshPro > Import TMP Essential Resources, then rerun the builder.");
            return false;
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name))
                AssetDatabase.CreateFolder(parent, name);
        }

        private static Material CreateOrUpdateMaterial(string path, string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[PixelFlowSceneBuilder] Shader '{shaderName}' not found.");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetColor("_BaseColor", Color.white);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject CreateCube(string name, Material material, bool keepCollider)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!keepCollider)
                Object.DestroyImmediate(go.GetComponent<Collider>());

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        private static T SavePrefab<T>(GameObject go, string fileName) where T : Component
        {
            string path = PrefabFolder + "/" + fileName;
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return asset.GetComponent<T>();
        }

        private static PixelCellView BuildCellPrefab(Material material)
        {
            GameObject go = CreateCube("PixelCell", material, false);
            go.AddComponent<PixelCellView>();
            return SavePrefab<PixelCellView>(go, "PixelCell.prefab");
        }

        private static Transform BuildProjectilePrefab(Material material)
        {
            GameObject go = CreateCube("Projectile", material, false);
            go.transform.localScale = new Vector3(0.22f, 0.22f, 0.22f);
            return SavePrefab<Transform>(go, "Projectile.prefab");
        }

        private static ColorTankView BuildTankPrefab(Material material, TMP_FontAsset font)
        {
            GameObject go = CreateCube("ColorTank", material, true);

            var textGo = new GameObject("AmmoText");
            textGo.transform.SetParent(go.transform, false);
            var text = textGo.AddComponent<TextMeshPro>();
            text.font = font;
            text.text = "20";
            text.fontSize = 5f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.rectTransform.sizeDelta = new Vector2(1f, 0.8f);
            textGo.transform.localPosition = new Vector3(0f, 0f, -0.51f);

            var view = go.AddComponent<ColorTankView>();
            var so = new SerializedObject(view);
            so.FindProperty("body").objectReferenceValue = go.GetComponent<MeshRenderer>();
            so.FindProperty("pickCollider").objectReferenceValue = go.GetComponent<BoxCollider>();
            so.FindProperty("ammoText").objectReferenceValue = text;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab<ColorTankView>(go, "ColorTank.prefab");
        }

        private static DebrisFx BuildDebrisPrefab(Material particleMaterial)
        {
            var go = new GameObject("Debris");
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            main.gravityModifier = 1.5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = DebrisFx.MaxParticles;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = particleMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            go.AddComponent<DebrisFx>();
            return SavePrefab<DebrisFx>(go, "Debris.prefab");
        }

        private static void BuildScene(Scene scene, LevelData level, Material boardMaterial, TMP_FontAsset font,
            PixelCellView cellPrefab, ColorTankView tankPrefab, Transform projectilePrefab, DebrisFx debrisPrefab)
        {
            // Camera: orthographic portrait view of the XY plane, looking down +Z.
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = CameraSize;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BackgroundColor;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            camGo.AddComponent<AudioListener>();

            // Board.
            var boardGo = new GameObject("Board");
            var grid = boardGo.AddComponent<PixelGridRenderer>();
            var gridSo = new SerializedObject(grid);
            gridSo.FindProperty("cubeMesh").objectReferenceValue = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            gridSo.FindProperty("material").objectReferenceValue = boardMaterial;
            gridSo.ApplyModifiedPropertiesWithoutUndo();

            // Tanks: supply lanes + tray.
            var tanksGo = new GameObject("Tanks");
            var trayRoot = new GameObject("TrayRoot").transform;
            trayRoot.SetParent(tanksGo.transform, false);
            trayRoot.localPosition = TrayPosition;
            var supplyRoot = new GameObject("SupplyRoot").transform;
            supplyRoot.SetParent(tanksGo.transform, false);
            supplyRoot.localPosition = SupplyPosition;
            var tankBoard = tanksGo.AddComponent<TankBoardView>();
            var tankSo = new SerializedObject(tankBoard);
            tankSo.FindProperty("tankPrefab").objectReferenceValue = tankPrefab;
            tankSo.FindProperty("supplyRoot").objectReferenceValue = supplyRoot;
            tankSo.FindProperty("trayRoot").objectReferenceValue = trayRoot;
            tankSo.ApplyModifiedPropertiesWithoutUndo();

            // Projectiles.
            var projectilesGo = new GameObject("Projectiles");
            var projectiles = projectilesGo.AddComponent<ProjectileSystem>();
            var projSo = new SerializedObject(projectiles);
            projSo.FindProperty("projectilePrefab").objectReferenceValue = projectilePrefab;
            projSo.ApplyModifiedPropertiesWithoutUndo();

            // Debris.
            var debris = ((GameObject)PrefabUtility.InstantiatePrefab(debrisPrefab.gameObject, scene)).GetComponent<DebrisFx>();

            // HUD + EventSystem.
            GameHudView hud = BuildHud(font);
            var eventSystemGo = new GameObject("EventSystem");
            eventSystemGo.AddComponent<EventSystem>();
            eventSystemGo.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();

            // Controller.
            var controllerGo = new GameObject("GameController");
            var controller = controllerGo.AddComponent<GameController>();
            var ctrlSo = new SerializedObject(controller);
            SerializedProperty levels = ctrlSo.FindProperty("levels");
            levels.arraySize = 1;
            levels.GetArrayElementAtIndex(0).objectReferenceValue = level;
            ctrlSo.FindProperty("gridRenderer").objectReferenceValue = grid;
            ctrlSo.FindProperty("tankBoard").objectReferenceValue = tankBoard;
            ctrlSo.FindProperty("projectiles").objectReferenceValue = projectiles;
            ctrlSo.FindProperty("cellViewPrefab").objectReferenceValue = cellPrefab;
            ctrlSo.FindProperty("debris").objectReferenceValue = debris;
            ctrlSo.FindProperty("hud").objectReferenceValue = hud;
            ctrlSo.FindProperty("cam").objectReferenceValue = cam;
            ctrlSo.FindProperty("boardArea").rectValue = BoardArea;
            ctrlSo.FindProperty("fireInterval").floatValue = 0.06f;
            ctrlSo.FindProperty("destroysPerFrame").intValue = 64;
            ctrlSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static GameHudView BuildHud(TMP_FontAsset font)
        {
            var canvasGo = new GameObject("HUD", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            TextMeshProUGUI levelText = CreateText(canvasGo.transform, "LevelText", font, "Level 1", 72f);
            RectTransform levelRect = levelText.rectTransform;
            levelRect.anchorMin = new Vector2(0.5f, 1f);
            levelRect.anchorMax = new Vector2(0.5f, 1f);
            levelRect.pivot = new Vector2(0.5f, 1f);
            levelRect.anchoredPosition = new Vector2(0f, -40f);
            levelRect.sizeDelta = new Vector2(800f, 110f);

            Button nextButton;
            Button retryButton;
            GameObject winPanel = CreatePanel(canvasGo.transform, "WinPanel", font, "You Win!", "Next", out nextButton);
            GameObject losePanel = CreatePanel(canvasGo.transform, "LosePanel", font, "You Lose", "Retry", out retryButton);

            var hud = canvasGo.AddComponent<GameHudView>();
            var so = new SerializedObject(hud);
            so.FindProperty("levelText").objectReferenceValue = levelText;
            so.FindProperty("winPanel").objectReferenceValue = winPanel;
            so.FindProperty("losePanel").objectReferenceValue = losePanel;
            SetSingle(so.FindProperty("retryButtons"), retryButton);
            SetSingle(so.FindProperty("nextButtons"), nextButton);
            so.ApplyModifiedPropertiesWithoutUndo();
            return hud;
        }

        private static void SetSingle(SerializedProperty array, Object value)
        {
            array.arraySize = 1;
            array.GetArrayElementAtIndex(0).objectReferenceValue = value;
        }

        private static GameObject CreatePanel(Transform parent, string name, TMP_FontAsset font, string title,
            string buttonLabel, out Button button)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

            TextMeshProUGUI titleText = CreateText(panel.transform, "Title", font, title, 140f);
            titleText.rectTransform.anchoredPosition = new Vector2(0f, 200f);
            titleText.rectTransform.sizeDelta = new Vector2(1000f, 220f);

            var buttonGo = new GameObject(buttonLabel + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(panel.transform, false);
            var buttonRect = (RectTransform)buttonGo.transform;
            buttonRect.anchoredPosition = new Vector2(0f, -150f);
            buttonRect.sizeDelta = new Vector2(520f, 170f);
            var image = buttonGo.GetComponent<Image>();
            image.color = new Color(0.22f, 0.66f, 0.33f, 1f);
            button = buttonGo.GetComponent<Button>();
            button.targetGraphic = image;

            TextMeshProUGUI label = CreateText(buttonGo.transform, "Label", font, buttonLabel, 84f);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            panel.SetActive(false);
            return panel;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, TMP_FontAsset font, string value, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static void AddSceneToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
            {
                if (existing.path != ScenePath)
                    scenes.Add(existing);
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
