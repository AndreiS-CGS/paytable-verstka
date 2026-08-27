using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace CGS.PaytableLibrary
{
    /// <summary>
    /// Editor-side steps of the cgs-atlas-builder pipeline (see that skill's SKILL.md for the full
    /// picture — steps 1-2, cropping/resizing/packing the raw PNGs into an atlas PNG + coordinate
    /// JSON, are Python and run BEFORE any of this: skills/cgs-atlas-builder/scripts/).
    /// Every method here operates on files already on disk.
    /// </summary>
    public static class PaytableAtlasBuilder
    {
        [Serializable]
        public class SpriteRect
        {
            public string name;
            public int x, y, w, h;
        }

        [Serializable]
        private class SpriteRectListWrapper
        {
            public SpriteRect[] items;
        }

        /// <summary>Parses pack_atlas.py's output JSON (a raw top-level array).</summary>
        public static SpriteRect[] ReadSpriteRects(string jsonPath)
        {
            string raw = File.ReadAllText(jsonPath).Trim();
            var wrapped = JsonUtility.FromJson<SpriteRectListWrapper>("{\"items\":" + raw + "}");
            return wrapped.items;
        }

        /// <summary>Step 4 — one-time material for a new sprite asset.</summary>
        public static Material CreateSpriteMaterial(string materialPath)
        {
            var shader = Shader.Find("TextMeshPro/Sprite");
            var mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, materialPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        /// <summary>
        /// Step 5 — TMP's own hash formula. NEVER compute this with a naive h*31+c implementation;
        /// it does not match, and mismatched hashes are the #1 cause of "sprite renders as literal
        /// text" bugs.
        /// </summary>
        public static Dictionary<string, int> GetHashCodes(IEnumerable<string> names)
        {
            var result = new Dictionary<string, int>();
            foreach (var n in names)
                result[n] = TMP_TextUtilities.GetSimpleHashCode(n);
            return result;
        }

        /// <summary>
        /// Step 6 — writes the sprite glyph/character tables directly into the .asset YAML. The
        /// C# SerializedObject API (spriteGlyphTable.Add / ApplyModifiedProperties) does NOT
        /// persist these lists correctly in this Unity version — this must stay a direct text
        /// rewrite. `sprites` and `hashes` typically come from ReadSpriteRects + GetHashCodes.
        /// Only touches a freshly-created asset whose tables are still the literal `[]` — safe to
        /// call once per new sprite asset, not idempotent on an already-filled one.
        /// </summary>
        public static void WriteSpriteAssetTables(string assetPath, SpriteRect[] sprites, Dictionary<string, int> hashes)
        {
            string content = File.ReadAllText(assetPath);

            var glyphYaml = new StringBuilder();
            for (int i = 0; i < sprites.Length; i++)
            {
                var sp = sprites[i];
                // bearingY = half the glyph height, NOT the full height: that centres the sprite on
                // the baseline, which is what makes the vertical nudge one constant per font instead
                // of a value that has to be recomputed for every <size=P%>. See cgs-atlas-builder's
                // SKILL.md → "Формулы".
                //
                // Integer division on purpose: a float would be interpolated with the current
                // culture, so a locale that writes decimals with a comma emits "63,5" and corrupts
                // the YAML. Heights are even by the standard (128), so nothing is lost here.
                glyphYaml.Append(
$@"  - m_Index: {i}
    m_Metrics:
      m_Width: {sp.w}
      m_Height: {sp.h}
      m_HorizontalBearingX: 0
      m_HorizontalBearingY: {sp.h / 2}
      m_HorizontalAdvance: {sp.w}
    m_GlyphRect:
      m_X: {sp.x}
      m_Y: {sp.y}
      m_Width: {sp.w}
      m_Height: {sp.h}
    m_Scale: 1
    m_AtlasIndex: 0
    m_ClassDefinitionType: 0
    sprite: {{fileID: 0}}
");
            }

            var charYaml = new StringBuilder();
            for (int i = 0; i < sprites.Length; i++)
            {
                var sp = sprites[i];
                if (!hashes.TryGetValue(sp.name, out var hash))
                    throw new Exception($"No hash provided for symbol '{sp.name}' — call GetHashCodes first and include every name.");
                // character m_Scale MUST be 1. It multiplies into the sprite's rendered size, so any
                // other value silently scales every sprite by that factor and breaks
                // spriteHeight = fontSize × P/100. Per-use sizing belongs in a <size=P%> tag, never
                // baked into the asset.
                charYaml.Append(
$@"  - m_ElementType: 0
    m_Unicode: 65534
    m_GlyphIndex: {i}
    m_Scale: 1
    m_Name: {sp.name}
    m_HashCode: {hash}
");
            }

            // Field name varies by Unity/TMP version: newer TMP serializes the glyph list as
            // "m_GlyphTable", older ones as "m_SpriteGlyphTable" — match whichever is present
            // rather than assuming one.
            string glyphField = Regex.IsMatch(content, @"  m_SpriteGlyphTable: \[\]") ? "m_SpriteGlyphTable" : "m_GlyphTable";
            content = Regex.Replace(content, $@"  {glyphField}: \[\]",
                $"  {glyphField}:\n" + glyphYaml.ToString().TrimEnd());
            content = Regex.Replace(content, @"  m_SpriteCharacterTable: \[\]",
                "  m_SpriteCharacterTable:\n" + charYaml.ToString().TrimEnd());
            File.WriteAllText(assetPath, content);
        }

        /// <summary>
        /// Step 7 — final import + the mandatory four-point usability check. Throws (rather than
        /// silently leaving a half-broken asset) if any condition fails. On success, returns a
        /// human-readable report for logging.
        /// </summary>
        public static string FinalImportAndVerify(string assetPath, string texturePath, string sampleSymbolName)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var sa = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(assetPath);
            sa.UpdateLookupTables();
            EditorUtility.SetDirty(sa);

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (sa.spriteSheet == null)
            {
                var so = new SerializedObject(sa);
                so.FindProperty("spriteSheet").objectReferenceValue = tex;
                so.ApplyModifiedProperties();
            }
            if (sa.material != null && sa.material.mainTexture == null)
                sa.material.mainTexture = tex;
            EditorUtility.SetDirty(sa);
            AssetDatabase.SaveAssets();

            int idx = sa.GetSpriteIndexFromName(sampleSymbolName);
            bool charsOk = sa.spriteCharacterTable.Count > 0;
            bool sheetOk = sa.spriteSheet != null;
            bool matOk = sa.material != null && sa.material.mainTexture != null;
            string report = $"index({sampleSymbolName})={idx} | chars={sa.spriteCharacterTable.Count} | spriteSheet={sheetOk} | matTex={matOk}";
            if (idx < 0 || !charsOk || !sheetOk || !matOk)
                throw new Exception("Sprite asset failed verification: " + report);
            return report;
        }

        /// <summary>
        /// Step 7b — slices the SAME atlas texture into individually-addressable Sprite sub-assets,
        /// reusing the rects already sitting in the sprite asset's own glyph table (no separate
        /// JSON needed at this point). This is what lets a symbol be used as a plain
        /// <c>Image.sprite</c> hero (e.g. inside GoldBox/PayRow), not only as an inline
        /// <c>&lt;sprite name="X"&gt;</c> tag — and it does so without duplicating the texture.
        /// Returns the number of sub-sprites produced.
        /// </summary>
        public static int SliceIntoSubSprites(string texturePath, string spriteAssetPath)
        {
            var sa = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(spriteAssetPath);
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null)
                throw new Exception($"No TextureImporter at {texturePath}");

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;

            var metaList = new List<SpriteMetaData>();
            foreach (var ch in sa.spriteCharacterTable)
            {
                var glyph = sa.spriteGlyphTable.Find(g => g.index == ch.glyphIndex);
                var r = glyph.glyphRect;
                metaList.Add(new SpriteMetaData
                {
                    name = ch.name,
                    rect = new Rect(r.x, r.y, r.width, r.height),
                    alignment = (int)SpriteAlignment.Center
                });
            }
            importer.spritesheet = metaList.ToArray();
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
            return metaList.Count;
        }
    }
}
