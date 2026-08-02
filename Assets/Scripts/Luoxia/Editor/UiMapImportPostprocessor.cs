#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Luoxia.Editor
{
    /// <summary>
    /// Auto-configures imported map UI sprites (Sprite, alpha, 9-slice for *_9slice names).
    /// </summary>
    public sealed class UiMapImportPostprocessor : AssetPostprocessor
    {
        private const string MapRoot = "Assets/Art/UI/Map/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').StartsWith(MapRoot))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spritePixelsPerUnit = 100f;

            var file = Path.GetFileNameWithoutExtension(assetPath);
            // spriteBorder = L,B,R,T
            if (file.IndexOf("panel_dialogue_input", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                importer.spriteBorder = new Vector4(48, 40, 48, 40);
            }
            else if (file.IndexOf("panel_event_modal", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                importer.spriteBorder = new Vector4(72, 72, 72, 72);
            }
            else if (file.IndexOf("panel_bottom_gradient", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                importer.spriteBorder = new Vector4(0, 80, 0, 200);
            }
            else if (file.IndexOf("button_event_choice", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // 896×160 ornate banner: left plant + right star need ~200px caps.
                // Old 64px borders left the filigree in the stretch zone → flattened ends.
                importer.spriteBorder = new Vector4(200, 28, 200, 28);
            }
            else if (file.IndexOf("button_open_all", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                importer.spriteBorder = new Vector4(96, 32, 96, 32);
            }
            else if (file.IndexOf("deco_event_separator", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                importer.spriteBorder = new Vector4(40, 2, 40, 2);
            }
            else if (file.IndexOf("9slice", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                importer.spriteBorder = new Vector4(32, 32, 32, 32);
            }
            else if (file.StartsWith("panel_avatar_name"))
            {
                importer.spriteBorder = new Vector4(40, 24, 40, 24);
            }
        }

        [MenuItem("Luoxia/UI/Reimport Map Sprites (9-slice)")]
        public static void ReimportAll()
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art/UI/Map" });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }

            Debug.Log($"[Luoxia] Reimported {guids.Length} map UI textures");
        }
    }
}
#endif
