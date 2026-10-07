using System.IO;
using PixelFlow.Controller;
using PixelFlow.Data;
using PixelFlow.Meta;
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
    /// Generates the playable game: view materials, the PixelCell / ColorTank / SlotFrame / Projectile / Debris prefabs and
    /// <c>Assets/PixelFlow/Scenes/Game.unity</c> with every serialized reference assigned, then applies the Build
    /// Settings order Loading, Menu, Game (<see cref="MetaUiBuilder.ApplyBuildSettingsOrder"/>). Rerunning overwrites the
    /// generated assets in place (asset GUIDs are kept).
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
        /// Folder of the generated textures.
        /// </summary>
        public const string TextureFolder = "Assets/PixelFlow/Textures";

        /// <summary>
        /// Root folder of the Layer Lab "GUI Pro-SuperCasual" UI pack (referenced, never modified).
        /// </summary>
        public const string UiPackPath = "Assets/Layer Lab/GUI Pro-SuperCasual";

        /// <summary>
        /// TMP font asset (white face, black outline) used by the tank ammo labels and the belt counter.
        /// </summary>
        public const string FontPath = UiPackPath + "/ResourcesData/Fonts/Sen_Line_s_Black SDF.asset";

        /// <summary>
        /// TMP font asset (white face, navy outline) used by the HUD level label.
        /// </summary>
        public const string HudFontPath = UiPackPath + "/ResourcesData/Fonts/Sen_Line_s_Navy SDF.asset";

        /// <summary>
        /// Project path of the generated glossy tile texture shared by the board cells, the destroy cube and the tanks.
        /// </summary>
        public const string TileTexturePath = TextureFolder + "/CellTile.png";

        /// <summary>
        /// Pack prefab instantiated (as a prefab instance) for the win popup.
        /// </summary>
        public const string VictoryPopupPath = UiPackPath + "/Prefabs/Prefabs_DemoScene_Panels/PopupDim_Play_Result_Victory.prefab";

        /// <summary>
        /// Pack prefab instantiated (as a prefab instance) for the lose popup.
        /// </summary>
        public const string DefeatPopupPath = UiPackPath + "/Prefabs/Prefabs_DemoScene_Panels/PopupDim_Play_Result_Defeat.prefab";

        private const string SpritePath = UiPackPath + "/ResourcesData/Sprites/Components/";
        private const string LevelPillSpritePath = SpritePath + "Button/Button01_s_Blue.png";
        private const string SlotFrameSpritePath = SpritePath + "Frame/BasicFrame_Round24.png";
        private const string SlotFrameCopyPath = TextureFolder + "/SlotFrame_BasicFrame_Round24.png";
        private const string SpriteUnlitShader = "Universal Render Pipeline/2D/Sprite-Unlit-Default";
        private const int TileTextureSize = 64;

        /// <summary>
        /// Camera orthographic size (half the visible height in world units, portrait).
        /// </summary>
        public const float CameraSize = 10f;

        /// <summary>
        /// World rectangle the board is fitted into (top part of the screen, inside the belt track).
        /// </summary>
        public static readonly Rect BoardArea = new Rect(-4f, -1.2f, 8f, 8f);

        /// <summary>
        /// Centre of the waiting slots (below the belt and its counter).
        /// </summary>
        public static readonly Vector3 TrayPosition = new Vector3(0f, -4f, 0f);

        /// <summary>
        /// Front row of the supply lanes (below the waiting slots); later rows extend further down.
        /// </summary>
        public static readonly Vector3 SupplyPosition = new Vector3(0f, -5.5f, 0f);

        private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        // Colours picked from the reference screenshot.
        private static readonly Color BackgroundColor = new Color32(0x3D, 0x3C, 0x64, 0xFF);
        private static readonly Color TrackColor = new Color32(0x56, 0x58, 0x94, 0xFF);
        private static readonly Color ChevronColor = new Color32(0x3A, 0x39, 0x66, 0xFF);
        private static readonly Color RimColor = new Color32(0xA6, 0xAB, 0xE8, 0xFF);
        private static readonly Color WellColor = new Color32(0x2E, 0x2D, 0x4F, 0xFF);
        private static readonly Color SlotFrameColor = new Color(0.15f, 0.16f, 0.24f, 0.5f);

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
            EnsureFolder("Assets/PixelFlow", "Textures");

            // Open the new scene first: NewScene unloads unused assets, which would invalidate references
            // to assets loaded or saved before it.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            var hudFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(HudFontPath);
            var pillSprite = AssetDatabase.LoadAssetAtPath<Sprite>(LevelPillSpritePath);
            var frameSprite = LoadSlicedSpriteCopy(SlotFrameSpritePath, SlotFrameCopyPath, new Vector4(24f, 24f, 24f, 24f));
            var victoryPopup = AssetDatabase.LoadAssetAtPath<GameObject>(VictoryPopupPath);
            var defeatPopup = AssetDatabase.LoadAssetAtPath<GameObject>(DefeatPopupPath);
            if (hudFont == null || pillSprite == null || frameSprite == null || victoryPopup == null || defeatPopup == null)
            {
                Debug.LogError($"[PixelFlowSceneBuilder] Missing UI pack asset under {UiPackPath} (HUD font: {hudFont != null}, " +
                               $"pill sprite: {pillSprite != null}, frame sprite: {frameSprite != null}, " +
                               $"victory popup: {victoryPopup != null}, defeat popup: {defeatPopup != null}).");
                return;
            }
            var levels = new LevelData[StarterLevelFactory.AssetPaths.Length];
            string missingLevel = null;
            for (int i = 0; i < levels.Length; i++)
            {
                levels[i] = AssetDatabase.LoadAssetAtPath<LevelData>(StarterLevelFactory.AssetPaths[i]);
                if (levels[i] == null && missingLevel == null)
                    missingLevel = StarterLevelFactory.AssetPaths[i];
            }
            var boardMaterial = AssetDatabase.LoadAssetAtPath<Material>(BoardRenderTools.MaterialPath);
            if (font == null || missingLevel != null || boardMaterial == null)
            {
                Debug.LogError($"[PixelFlowSceneBuilder] Missing input asset (font: {font != null}, " +
                               $"first missing level: {missingLevel ?? "none"} - run PixelFlow/Create Levels, " +
                               $"board material: {boardMaterial != null}).");
                return;
            }

            Material viewMaterial = CreateOrUpdateMaterial(MaterialFolder + "/ViewUnlit.mat", "Universal Render Pipeline/Unlit");
            Material particleMaterial = CreateOrUpdateMaterial(MaterialFolder + "/DebrisParticle.mat", "Universal Render Pipeline/Particles/Unlit");
            // The destroy-animation cube uses the board's shader so a cell keeps its exact shading (no brightness
            // pop) when the instanced cell is hidden and the pooled cube takes over. Both colour paths are linear:
            // the board writes Color.linear via SetVectorArray, PixelCellView uses SetColor (gamma -> linear).
            Material cellMaterial = CreateOrUpdateMaterial(MaterialFolder + "/CellInstanced.mat", BoardRenderTools.ShaderName);
            Material spriteMaterial = CreateOrUpdateMaterial(MaterialFolder + "/SpriteUnlit.mat", SpriteUnlitShader);
            if (viewMaterial == null || particleMaterial == null || cellMaterial == null || spriteMaterial == null)
                return;

            // Board cells, the destroy cube and the tanks share the glossy tile texture, so the destroy cube
            // matches the instanced cell exactly.
            Texture2D tile = CreateOrUpdateTileTexture();
            ApplyTile(boardMaterial, tile);
            ApplyTile(cellMaterial, tile);

            PixelCellView cellPrefab = BuildCellPrefab(cellMaterial);
            ColorTankView tankPrefab = BuildTankPrefab(cellMaterial, font);
            Transform slotFramePrefab = BuildSlotFramePrefab(spriteMaterial, frameSprite);
            Transform projectilePrefab = BuildProjectilePrefab(viewMaterial);
            DebrisFx debrisPrefab = BuildDebrisPrefab(particleMaterial);

            var hudAssets = new HudAssets
            {
                HudFont = hudFont,
                PillSprite = pillSprite,
                VictoryPopup = victoryPopup,
                DefeatPopup = defeatPopup,
            };
            BuildScene(scene, levels, boardMaterial, viewMaterial, font, cellPrefab, tankPrefab, slotFramePrefab,
                projectilePrefab, debrisPrefab, hudAssets);
            MetaUiBuilder.ApplyBuildSettingsOrder();
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

        /// <summary>
        /// UI pack assets used by the HUD.
        /// </summary>
        private struct HudAssets
        {
            public TMP_FontAsset HudFont;
            public Sprite PillSprite;
            public GameObject VictoryPopup;
            public GameObject DefeatPopup;
        }

        /// <summary>
        /// Copies the pack sprite at <paramref name="sourcePath"/> to <paramref name="copyPath"/> (once; the pack itself
        /// is never modified) and imports the copy for a 9-sliced SpriteRenderer: a Full Rect mesh and a
        /// <paramref name="border"/> (left, bottom, right, top, in pixels) that leaves a stretchable centre (the pack's
        /// own borders span the whole sprite, which a sliced SpriteRenderer draws as corners only).
        /// Returns null if the source sprite is missing.
        /// </summary>
        private static Sprite LoadSlicedSpriteCopy(string sourcePath, string copyPath, Vector4 border)
        {
            if (AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath) == null)
                return null;
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(copyPath) == null && !AssetDatabase.CopyAsset(sourcePath, copyPath))
                return null;

            var importer = (TextureImporter)AssetImporter.GetAtPath(copyPath);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            if (settings.spriteMeshType != SpriteMeshType.FullRect || settings.spriteBorder != border)
            {
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spriteBorder = border;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(copyPath);
        }

        private static void ApplyTile(Material material, Texture2D tile)
        {
            material.SetTexture("_TileTex", tile);
            // Small tiles grow back to the full cell and draw the gap in the shader (see InstancedColor.shader).
            material.SetFloat("_FlatGrow", 1f / PixelGridRenderer.CubeScaleFactor);
            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// Writes (or rewrites) <see cref="TileTexturePath"/>: a small linear data texture read by
        /// PixelFlow/InstancedColor. R = shade multiplier (flat face, light top bevel, darker bottom lip), G = 1 - white
        /// highlight (a gloss bar near the top-left), A = rounded-square mask. Mip maps preserve the alpha-test coverage
        /// so the tiles keep their size on the 64x64 board.
        /// </summary>
        private static Texture2D CreateOrUpdateTileTexture()
        {
            const int size = TileTextureSize;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float v = (y + 0.5f) / size;
                    pixels[y * size + x] = TilePixel(u, v, 1f / size);
                }
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(TileTexturePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(TileTexturePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TileTexturePath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.mipMapsPreserveCoverage = true;
            importer.alphaTestReferenceValue = 0.5f;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TileTexturePath);
        }

        private static Color32 TilePixel(float u, float v, float texel)
        {
            // Outer tile: rounded square over the whole face.
            float outer = RoundedRectDistance(u, v, 0f, 0f, 1f, 1f, 0.2f);
            float alpha = Mathf.Clamp01(0.5f - outer / texel);

            // Flat face inset from the bevel: thin sides, a light top edge and a deeper bottom lip.
            float face = RoundedRectDistance(u, v, 0.07f, 0.17f, 0.93f, 0.92f, 0.13f);
            float shade;
            if (face <= 0f)
                shade = Mathf.Lerp(0.86f, 0.95f, v); // slight top-to-bottom falloff on the face
            else if (v > 0.6f)
                shade = 1f;                           // top bevel: full colour, lifted by the highlight below
            else if (v < 0.4f)
                shade = 0.7f;                         // bottom lip
            else
                shade = 0.82f;                        // sides
            float highlight = face > 0f && v > 0.6f ? 0.28f : 0f;

            // Gloss bar near the top-left, as on the pack's Button01 sprites.
            if (RoundedRectDistance(u, v, 0.15f, 0.7f, 0.42f, 0.83f, 0.06f) <= 0f)
                highlight = 0.55f;

            return new Color32((byte)(shade * 255f), (byte)((1f - highlight) * 255f), 0, (byte)(alpha * 255f));
        }

        /// <summary>
        /// Signed distance from (u, v) to a rounded rectangle [x0, x1] x [y0, y1] with corner radius r (negative inside).
        /// </summary>
        private static float RoundedRectDistance(float u, float v, float x0, float y0, float x1, float y1, float r)
        {
            float cx = (x0 + x1) * 0.5f;
            float cy = (y0 + y1) * 0.5f;
            float qx = Mathf.Abs(u - cx) - ((x1 - x0) * 0.5f - r);
            float qy = Mathf.Abs(v - cy) - ((y1 - y0) * 0.5f - r);
            float ox = Mathf.Max(qx, 0f);
            float oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
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

        private static Transform BuildSlotFramePrefab(Material spriteMaterial, Sprite frameSprite)
        {
            // A dark rounded square behind the slot tank (tinted by TankBoardView through the sprite colour); no
            // collider, so taps reach the tank in front of it. Drawn 9-sliced at twice the size and scaled by half
            // so the pack sprite's corner radius stays small relative to the slot.
            var go = new GameObject("SlotFrame");
            var sprite = go.AddComponent<SpriteRenderer>();
            sprite.sprite = frameSprite;
            sprite.sharedMaterial = spriteMaterial;
            sprite.drawMode = SpriteDrawMode.Sliced;
            sprite.size = new Vector2(2.3f, 2.3f);
            sprite.color = SlotFrameColor;
            sprite.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sprite.receiveShadows = false;
            go.transform.localScale = new Vector3(0.5f, 0.5f, 1f);
            return SavePrefab<Transform>(go, "SlotFrame.prefab");
        }

        private static ColorTankView BuildTankPrefab(Material material, TMP_FontAsset font)
        {
            // Same glossy tile material as the board cells, tinted per tank through _BaseColor.
            GameObject go = CreateCube("ColorTank", material, true);

            var textGo = new GameObject("AmmoText");
            textGo.transform.SetParent(go.transform, false);
            var text = textGo.AddComponent<TextMeshPro>();
            text.font = font;
            text.text = "20";
            text.fontSize = 5f;
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

        private static void BuildScene(Scene scene, LevelData[] levelAssets, Material boardMaterial, Material viewMaterial,
            TMP_FontAsset font, PixelCellView cellPrefab, ColorTankView tankPrefab, Transform slotFramePrefab,
            Transform projectilePrefab, DebrisFx debrisPrefab, HudAssets hudAssets)
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

            // Belt: track mesh (track + chevron sub-meshes) and the "used/capacity" counter.
            var beltGo = new GameObject("Belt");
            var trackGo = new GameObject("Track");
            trackGo.transform.SetParent(beltGo.transform, false);
            var trackFilter = trackGo.AddComponent<MeshFilter>();
            var trackRenderer = trackGo.AddComponent<MeshRenderer>();
            trackRenderer.sharedMaterials = new[] { viewMaterial, viewMaterial, viewMaterial, viewMaterial };
            trackRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trackRenderer.receiveShadows = false;
            var counterGo = new GameObject("Counter");
            counterGo.transform.SetParent(beltGo.transform, false);
            var counter = counterGo.AddComponent<TextMeshPro>();
            counter.font = font;
            counter.text = "0/5";
            counter.fontSize = 6f;
            counter.alignment = TextAlignmentOptions.Center;
            counter.color = Color.white;
            counter.textWrappingMode = TextWrappingModes.NoWrap;
            counter.rectTransform.sizeDelta = new Vector2(2f, 0.8f);
            var belt = beltGo.AddComponent<BeltView>();
            var beltSo = new SerializedObject(belt);
            beltSo.FindProperty("trackRenderer").objectReferenceValue = trackRenderer;
            beltSo.FindProperty("trackFilter").objectReferenceValue = trackFilter;
            beltSo.FindProperty("counterText").objectReferenceValue = counter;
            beltSo.FindProperty("trackColor").colorValue = TrackColor;
            beltSo.FindProperty("chevronColor").colorValue = ChevronColor;
            beltSo.FindProperty("rimColor").colorValue = RimColor;
            beltSo.FindProperty("wellColor").colorValue = WellColor;
            beltSo.ApplyModifiedPropertiesWithoutUndo();

            // Tanks: supply lanes, belt tanks and waiting slots.
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
            tankSo.FindProperty("slotFramePrefab").objectReferenceValue = slotFramePrefab;
            tankSo.FindProperty("slotFrameColor").colorValue = SlotFrameColor;
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
            GameHudView hud = BuildHud(hudAssets, out SettingsPopup settingsPopup);
            var eventSystemGo = new GameObject("EventSystem");
            eventSystemGo.AddComponent<EventSystem>();
            eventSystemGo.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();

            // Controller.
            var controllerGo = new GameObject("GameController");
            var controller = controllerGo.AddComponent<GameController>();
            var ctrlSo = new SerializedObject(controller);
            SerializedProperty levels = ctrlSo.FindProperty("levels");
            levels.arraySize = levelAssets.Length;
            for (int i = 0; i < levelAssets.Length; i++)
                levels.GetArrayElementAtIndex(i).objectReferenceValue = levelAssets[i];
            ctrlSo.FindProperty("gridRenderer").objectReferenceValue = grid;
            ctrlSo.FindProperty("tankBoard").objectReferenceValue = tankBoard;
            ctrlSo.FindProperty("projectiles").objectReferenceValue = projectiles;
            ctrlSo.FindProperty("cellViewPrefab").objectReferenceValue = cellPrefab;
            ctrlSo.FindProperty("debris").objectReferenceValue = debris;
            ctrlSo.FindProperty("hud").objectReferenceValue = hud;
            ctrlSo.FindProperty("settingsPopup").objectReferenceValue = settingsPopup;
            ctrlSo.FindProperty("cam").objectReferenceValue = cam;
            ctrlSo.FindProperty("boardArea").rectValue = BoardArea;
            ctrlSo.FindProperty("beltView").objectReferenceValue = belt;
            ctrlSo.FindProperty("lapSeconds").floatValue = 4f;
            ctrlSo.FindProperty("destroysPerFrame").intValue = 64;
            ctrlSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static GameHudView BuildHud(HudAssets assets, out SettingsPopup settingsPopup)
        {
            var canvasGo = new GameObject("HUD", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // "Level N" on the pack's glossy blue pill at the top centre (no coin bar), settings gear top right.
            var pillGo = new GameObject("LevelPill", typeof(RectTransform), typeof(Image));
            pillGo.transform.SetParent(canvasGo.transform, false);
            var pillRect = (RectTransform)pillGo.transform;
            pillRect.anchorMin = new Vector2(0.5f, 1f);
            pillRect.anchorMax = new Vector2(0.5f, 1f);
            pillRect.pivot = new Vector2(0.5f, 1f);
            pillRect.anchoredPosition = new Vector2(0f, -36f);
            pillRect.sizeDelta = new Vector2(400f, 124f);
            var pill = pillGo.GetComponent<Image>();
            pill.sprite = assets.PillSprite;
            pill.type = Image.Type.Sliced;
            pill.raycastTarget = false;

            TextMeshProUGUI levelText = CreateText(pillGo.transform, "LevelText", assets.HudFont, "Level 1", 62f);
            RectTransform levelRect = levelText.rectTransform;
            levelRect.anchorMin = Vector2.zero;
            levelRect.anchorMax = Vector2.one;
            levelRect.offsetMin = new Vector2(20f, 10f);
            levelRect.offsetMax = new Vector2(-20f, -4f);
            levelText.textWrappingMode = TextWrappingModes.NoWrap;

            Button settingsButton = MetaUiBuilder.CreateGearButton(canvasGo.transform);

            GameObject winPanel = CreateResultPopup(canvasGo.transform, assets.VictoryPopup, "WinPanel", "NextButton",
                "Next", out Button nextButton);
            GameObject losePanel = CreateResultPopup(canvasGo.transform, assets.DefeatPopup, "LosePanel", "RetryButton",
                "Retry", out Button retryButton);
            Button winHome = MetaUiBuilder.CreatePillButton(winPanel.transform, "HomeButton", "Home",
                new Vector2(0.5f, 0.5f), new Vector2(0f, -210f), new Vector2(340f, 120f));
            Button loseHome = MetaUiBuilder.CreatePillButton(losePanel.transform, "HomeButton", "Home",
                new Vector2(0.5f, 0.5f), new Vector2(0f, -210f), new Vector2(340f, 120f));

            settingsPopup = MetaUiBuilder.CreateSettingsPopup(canvasGo.transform, true);

            var hud = canvasGo.AddComponent<GameHudView>();
            var so = new SerializedObject(hud);
            so.FindProperty("levelText").objectReferenceValue = levelText;
            so.FindProperty("winPanel").objectReferenceValue = winPanel;
            so.FindProperty("losePanel").objectReferenceValue = losePanel;
            SetArray(so.FindProperty("retryButtons"), retryButton);
            SetArray(so.FindProperty("nextButtons"), nextButton);
            SetArray(so.FindProperty("settingsButtons"), settingsButton);
            SetArray(so.FindProperty("homeButtons"), winHome, loseHome);
            so.ApplyModifiedPropertiesWithoutUndo();
            return hud;
        }

        private static void SetArray(SerializedProperty array, params Object[] values)
        {
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        /// <summary>
        /// Instantiates a pack result popup (PopupDim_Play_Result_Victory / _Defeat) as a prefab instance under the HUD:
        /// keeps the dim, badge art and ribbon title; hides the REWARDS block (the game has no economy); removes the
        /// pack's demo scripts (PanelSuperCasual toggles other demo panels); turns the pack's "Continue" pill into a
        /// real <see cref="Button"/> named <paramref name="buttonName"/> labelled <paramref name="label"/>, moved up
        /// into the space the rewards used. Returned inactive.
        /// </summary>
        private static GameObject CreateResultPopup(Transform parent, GameObject prefab, string name, string buttonName,
            string label, out Button button)
        {
            var popup = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            popup.name = name;

            foreach (MonoBehaviour behaviour in popup.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null && behaviour.GetType().Namespace != null &&
                    behaviour.GetType().Namespace.StartsWith("LayerLab"))
                    Object.DestroyImmediate(behaviour);
            }

            SetChildActive(popup.transform, "Title_Line01", false);
            SetChildActive(popup.transform, "ItemFrame02_Basic", false);

            Transform buttonTransform = popup.transform.Find("Button_124_Blue");
            if (buttonTransform == null)
                throw new System.InvalidOperationException($"[PixelFlowSceneBuilder] '{prefab.name}' has no Button_124_Blue.");
            buttonTransform.name = buttonName;
            var buttonRect = (RectTransform)buttonTransform;
            buttonRect.anchoredPosition = new Vector2(0f, -40f);
            buttonRect.sizeDelta = new Vector2(440f, 150f);

            var image = buttonTransform.GetComponent<Image>();
            image.raycastTarget = true;
            button = buttonTransform.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;

            var text = buttonTransform.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
                text.enableAutoSizing = false;
                text.fontSize = 64f;
                text.raycastTarget = false;
            }

            popup.SetActive(false);
            return popup;
        }

        private static void SetChildActive(Transform root, string childName, bool active)
        {
            Transform child = root.Find(childName);
            if (child != null)
                child.gameObject.SetActive(active);
            else
                Debug.LogWarning($"[PixelFlowSceneBuilder] '{root.name}' has no child '{childName}'.");
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
    }
}
