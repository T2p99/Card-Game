using System;
using UnityEditor;
using UnityEngine;

namespace CardGame.Editor
{
    public sealed class CardArtTexturePostprocessor : AssetPostprocessor
    {
        public override uint GetVersion() => 2;

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Resources/CardGame/CardArt/", StringComparison.Ordinal) ||
                !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return;

            var importer = (TextureImporter)assetImporter;
            importer.npotScale = TextureImporterNPOTScale.None;
            // Mipmapped NPOT images fall back to RGBA32 even with an ASTC override.
            importer.mipmapEnabled = false;
            importer.GetSourceTextureWidthAndHeight(out var width, out var height);
            importer.maxTextureSize = Mathf.Max(importer.maxTextureSize,
                Mathf.NextPowerOfTwo(Mathf.Max(width, height)));

            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = importer.maxTextureSize;
            android.format = TextureImporterFormat.ASTC_4x4;
            android.textureCompression = TextureImporterCompression.CompressedHQ;
            android.compressionQuality = 100;
            android.crunchedCompression = false;
            importer.SetPlatformTextureSettings(android);
        }
    }
}
