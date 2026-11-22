using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TheDelivery.EditorTools
{
    /// <summary>Renders Clear's embedded idle and walk clips into a transparent PNG sprite sheet.</summary>
    public static class ClearSpriteSheetRenderer
    {
        private const string ModelPath = "Assets/_Project/Models/Clear/Clear.fbx";
        private const string OutputPath = "Assets/_Project/Textures/Clear_SpriteSheet.png";
        private const int Columns = 8;
        private const int Rows = 2;
        private const int CellWidth = 256;
        private const int CellHeight = 384;

        [MenuItem("Tools/The Delivery/Render Clear Sprite Sheet")]
        public static void Render()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) throw new FileNotFoundException("Clear model not found.", ModelPath);

            var clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToArray();
            var idle = clips.FirstOrDefault(c => c.name.ToLowerInvariant().Contains("idle")) ?? clips.FirstOrDefault();
            var walk = clips.FirstOrDefault(c => c.name.ToLowerInvariant().Contains("walk")) ?? clips.LastOrDefault();
            if (idle == null || walk == null) throw new System.InvalidOperationException("Clear clips were not found.");

            var subject = Object.Instantiate(model);
            subject.name = "Clear Sprite Render Subject";
            subject.transform.position = Vector3.zero;
            subject.transform.rotation = Quaternion.identity;

            var cameraGo = new GameObject("Clear Sprite Render Camera");
            var cam = cameraGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.allowHDR = false;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;

            var lightGo = new GameObject("Clear Sprite Key Light");
            var key = lightGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.25f;
            key.color = new Color(1f, .94f, .88f);
            lightGo.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

            var fillGo = new GameObject("Clear Sprite Fill Light");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = .55f;
            fill.color = new Color(.72f, .82f, 1f);
            fillGo.transform.rotation = Quaternion.Euler(35f, 140f, 0f);

            var renderTexture = new RenderTexture(CellWidth, CellHeight, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4
            };
            var cell = new Texture2D(CellWidth, CellHeight, TextureFormat.RGBA32, false);
            var sheet = new Texture2D(Columns * CellWidth, Rows * CellHeight, TextureFormat.RGBA32, false);

            try
            {
                AnimationMode.StartAnimationMode();
                for (var row = 0; row < Rows; row++)
                {
                    var clip = row == 0 ? idle : walk;
                    for (var column = 0; column < Columns; column++)
                    {
                        var time = clip.length * column / Columns;
                        AnimationMode.SampleAnimationClip(subject, clip, time);
                        FrameCamera(subject, cam);
                        cam.targetTexture = renderTexture;
                        cam.Render();
                        RenderTexture.active = renderTexture;
                        cell.ReadPixels(new Rect(0, 0, CellWidth, CellHeight), 0, 0);
                        cell.Apply(false);
                        sheet.SetPixels(column * CellWidth, (Rows - 1 - row) * CellHeight, CellWidth, CellHeight, cell.GetPixels());
                    }
                }
                sheet.Apply(false);
                Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
                File.WriteAllBytes(OutputPath, sheet.EncodeToPNG());
                AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceUpdate);
                var importer = (TextureImporter)AssetImporter.GetAtPath(OutputPath);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.alphaIsTransparency = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
                Debug.Log($"[ClearSpriteSheetRenderer] Created {OutputPath}: {Columns}x{Rows} frames ({CellWidth}x{CellHeight}px each).");
            }
            finally
            {
                if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
                RenderTexture.active = null;
                Object.DestroyImmediate(renderTexture);
                Object.DestroyImmediate(cell);
                Object.DestroyImmediate(sheet);
                Object.DestroyImmediate(subject);
                Object.DestroyImmediate(cameraGo);
                Object.DestroyImmediate(lightGo);
                Object.DestroyImmediate(fillGo);
            }
        }

        private static void FrameCamera(GameObject subject, Camera cam)
        {
            var renderers = subject.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            var centre = bounds.center + Vector3.up * .03f;
            // Clear's FBX faces the negative Z axis, so the camera belongs on positive Z.
            cam.transform.position = centre + new Vector3(0f, 0f, 4f);
            cam.transform.LookAt(centre);
            cam.orthographicSize = Mathf.Max(bounds.size.y * .58f, bounds.size.x * CellHeight / (2f * CellWidth) * 1.1f);
        }
    }
}
