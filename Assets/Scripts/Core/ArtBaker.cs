#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RebootEarth
{
    /// <summary>
    /// Edit mode only: saves the procedural sprites / materials as real assets in
    /// Assets/Art/Generated, so scenes built in the editor keep every visual after saving.
    /// </summary>
    public static class ArtBaker
    {
        public const string Root = "Assets/Art/Generated";
        const string SpriteDir = Root + "/Sprites";
        const string MaterialDir = Root + "/Materials";

        static void EnsureDir(string dir)
        {
            if (!AssetDatabase.IsValidFolder(dir))
            {
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        public static Sprite LoadSprite(string key)
            => AssetDatabase.LoadAssetAtPath<Sprite>($"{SpriteDir}/{key}.png");

        public static Sprite BakeSprite(string key, Sprite runtime)
        {
            EnsureDir(SpriteDir);
            string path = $"{SpriteDir}/{key}.png";
            File.WriteAllBytes(path, runtime.texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = runtime.pixelsPerUnit;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Bilinear;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            var s = new TextureImporterSettings();
            imp.ReadTextureSettings(s);
            s.spriteMeshType = SpriteMeshType.FullRect;    // needed for 9-slice (rounded boxes)
            s.spriteAlignment = (int)SpriteAlignment.Center;
            s.spriteBorder = runtime.border;
            imp.SetTextureSettings(s);
            imp.SaveAndReimport();
            var baked = LoadSprite(key);
            return baked != null ? baked : runtime;
        }

        public static Material BakeMaterial(Material m)
        {
            EnsureDir(MaterialDir);
            string path = $"{MaterialDir}/{m.name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
#endif
