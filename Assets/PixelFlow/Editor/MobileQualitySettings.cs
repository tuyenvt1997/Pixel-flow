using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PixelFlow.EditorTools
{
    /// <summary>
    /// Applies the mobile performance settings for the 60 FPS target: no shadows and no MSAA on the quality level
    /// Android and iOS use by default, main light shadows off in the URP asset, and default (automatic) graphics
    /// APIs on Android/iOS (Vulkan/GLES3, Metal) so GPU instancing stays available, and a portrait-locked screen.
    /// </summary>
    public static class MobileQualitySettings
    {
        private const string QualitySettingsPath = "ProjectSettings/QualitySettings.asset";

        /// <summary>
        /// Menu command, also runnable with <c>-executeMethod PixelFlow.EditorTools.MobileQualitySettings.Apply</c>.
        /// Idempotent; logs every value it sets.
        /// </summary>
        [MenuItem("PixelFlow/Apply Mobile Quality Settings")]
        public static void Apply()
        {
            ApplyQualityLevels();
            ApplyUrpAsset();
            KeepDefaultGraphicsApis(BuildTarget.Android);
            KeepDefaultGraphicsApis(BuildTarget.iOS);
            LockPortrait();
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Sets shadows = Disable and antiAliasing = 0 on every quality level that is the default of Android or iPhone.
        /// </summary>
        private static void ApplyQualityLevels()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(QualitySettingsPath);
            var so = new SerializedObject(assets[0]);
            SerializedProperty levels = so.FindProperty("m_QualitySettings");
            SerializedProperty defaults = so.FindProperty("m_PerPlatformDefaultQuality");

            for (int i = 0; i < defaults.arraySize; i++)
            {
                SerializedProperty pair = defaults.GetArrayElementAtIndex(i);
                string platform = pair.FindPropertyRelative("first").stringValue;
                if (platform != "Android" && platform != "iPhone")
                    continue;

                int index = pair.FindPropertyRelative("second").intValue;
                SerializedProperty level = levels.GetArrayElementAtIndex(index);
                level.FindPropertyRelative("shadows").intValue = (int)ShadowQuality.Disable;
                level.FindPropertyRelative("antiAliasing").intValue = 0;
                Debug.Log($"[PixelFlow] Quality level {index} '{level.FindPropertyRelative("name").stringValue}' " +
                          $"(default for {platform}): shadows = Disable, antiAliasing = 0.");
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Turns main light shadows off and MSAA to 1x (off) on the active URP asset; the setter of
        /// <c>supportsMainLightShadows</c> is internal, so the serialized fields are edited like the inspector does.
        /// </summary>
        private static void ApplyUrpAsset()
        {
            RenderPipelineAsset rp = GraphicsSettings.defaultRenderPipeline;
            if (rp == null)
            {
                Debug.LogWarning("[PixelFlow] No render pipeline asset assigned; URP settings skipped.");
                return;
            }

            var so = new SerializedObject(rp);
            so.FindProperty("m_MainLightShadowsSupported").boolValue = false;
            so.FindProperty("m_MSAA").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rp);
            Debug.Log($"[PixelFlow] URP asset '{AssetDatabase.GetAssetPath(rp)}': main light shadows off, MSAA 1x (off).");
        }

        /// <summary>
        /// Locks the game to portrait: the board, tray and supply layout and the 1080x1920 HUD are portrait-only.
        /// The autorotation flags are narrowed to portrait as well, in case AutoRotation is selected later.
        /// </summary>
        private static void LockPortrait()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = true;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            Debug.Log($"[PixelFlow] Default orientation = {PlayerSettings.defaultInterfaceOrientation}; " +
                      "autorotation limited to Portrait/PortraitUpsideDown.");
        }

        private static void KeepDefaultGraphicsApis(BuildTarget target)
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(target, true);
            Debug.Log($"[PixelFlow] {target}: default graphics APIs = {PlayerSettings.GetUseDefaultGraphicsAPIs(target)} " +
                      $"({string.Join(", ", PlayerSettings.GetGraphicsAPIs(target))}).");
        }
    }
}
