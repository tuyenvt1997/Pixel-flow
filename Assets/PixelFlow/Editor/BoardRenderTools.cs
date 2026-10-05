using System.IO;
using PixelFlow.Core;
using PixelFlow.Data;
using PixelFlow.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PixelFlow.EditorTools
{
    /// <summary>
    /// Editor utilities for the instanced board: creates the board material and renders an offscreen
    /// capture of Level_001 for visual checks in batch mode (run without -nographics).
    /// </summary>
    public static class BoardRenderTools
    {
        /// <summary>
        /// Project path of the instanced board material.
        /// </summary>
        public const string MaterialPath = "Assets/PixelFlow/Materials/PixelInstanced.mat";

        /// <summary>
        /// Output path (relative to the project root) of <see cref="CaptureLevel001"/>.
        /// </summary>
        public const string CapturePath = "Logs/board_capture.png";

        private const string ShaderName = "PixelFlow/InstancedColor";

        /// <summary>
        /// Creates (or updates) <see cref="MaterialPath"/> using the PixelFlow/InstancedColor shader
        /// with GPU instancing enabled.
        /// </summary>
        [MenuItem("PixelFlow/Create Board Material")]
        public static void CreateMaterial()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[BoardRenderTools] Shader '{ShaderName}' not found.");
                return;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            material.shader = shader;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BoardRenderTools] Material ready at {MaterialPath} (instancing on).");
        }

        /// <summary>
        /// Builds Level_001 with a temporary <see cref="PixelGridRenderer"/>, renders it through a temporary
        /// orthographic camera (looking down +Z) into a RenderTexture and writes <see cref="CapturePath"/>.
        /// Hides the bottom-left 8x8 cells to make HideCell visible in the capture.
        /// </summary>
        [MenuItem("PixelFlow/Capture Level_001 Board")]
        public static void CaptureLevel001()
        {
            var level = AssetDatabase.LoadAssetAtPath<LevelData>(SampleLevelFactory.AssetPath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (level == null || material == null)
            {
                Debug.LogError("[BoardRenderTools] Level_001 or board material missing.");
                return;
            }

            const int size = 512;
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var boardGo = new GameObject("BoardCapture");
            var camGo = new GameObject("BoardCaptureCamera");
            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                primitive.GetComponent<Renderer>().enabled = false;
                var renderer = boardGo.AddComponent<PixelGridRenderer>();
                var so = new SerializedObject(renderer);
                so.FindProperty("cubeMesh").objectReferenceValue = primitive.GetComponent<MeshFilter>().sharedMesh;
                so.FindProperty("material").objectReferenceValue = material;
                so.ApplyModifiedPropertiesWithoutUndo();

                var session = LevelSession.Create(level);
                renderer.Build(session.Grid, session.Palette, BoardLayout.Fit(level.width, level.height, new Rect(-5f, -5f, 10f, 10f)));
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 8; x++)
                        renderer.HideCell(new Vector2Int(x, y));

                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 5.5f;
                cam.transform.position = new Vector3(0f, 0f, -10f);
                cam.transform.rotation = Quaternion.identity;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 50f;
                cam.targetTexture = rt;

                renderer.Draw();
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request))
                    RenderPipeline.SubmitRenderRequest(cam, request);
                else
                    cam.Render();

                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                tex.Apply();
                RenderTexture.active = previous;

                File.WriteAllBytes(CapturePath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Debug.Log($"[BoardRenderTools] Wrote {CapturePath}: {renderer.InstanceCount} instances in {renderer.BatchCount} batches.");
            }
            finally
            {
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(boardGo);
                Object.DestroyImmediate(primitive);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }
}
