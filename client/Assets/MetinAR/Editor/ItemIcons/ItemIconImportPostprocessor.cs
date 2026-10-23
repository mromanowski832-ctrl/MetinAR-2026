using UnityEditor;

namespace MetinAR.Editor.ItemIcons
{
    /// <summary>
    /// METINAR-032: UI import settings for item icons in Assets/MetinAR/Textures/ItemIcons (ITEMS_COMBAT_SPEC §8:
    /// 256² PNG, ASTC). Runs after the METINAR-009 pipeline postprocessor and, like it, only on first import
    /// (no .meta yet), so committed .meta files and later Inspector overrides stay authoritative.
    /// </summary>
    public sealed class ItemIconImportPostprocessor : AssetPostprocessor
    {
        public const string Folder = "Assets/MetinAR/Textures/ItemIcons/";
        public const int IconSize = 256;
        public const TextureImporterFormat MobileFormat = TextureImporterFormat.ASTC_4x4;

        public override int GetPostprocessOrder()
        {
            return 10;
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder, System.StringComparison.Ordinal) || !assetImporter.importSettingsMissing)
            {
                return;
            }

            Apply((TextureImporter)assetImporter);
        }

        /// <summary>Sprite (Single), sRGB, alpha is transparency, mipmaps on (icons are shown below 256 px), clamp, 256 max, ASTC 4x4 on Android/iOS.</summary>
        public static void Apply(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.isReadable = false;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.filterMode = UnityEngine.FilterMode.Bilinear;
            importer.maxTextureSize = IconSize;
            importer.textureCompression = TextureImporterCompression.Compressed;
            foreach (string platform in new[] { "Android", "iPhone" })
            {
                TextureImporterPlatformSettings s = importer.GetPlatformTextureSettings(platform);
                s.overridden = true;
                s.maxTextureSize = IconSize;
                s.format = MobileFormat;
                importer.SetPlatformTextureSettings(s);
            }
        }
    }
}
