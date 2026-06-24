using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace PoemPoetry.Editor
{
    /// <summary>
    /// Builds a *trimmed* CJK SDF font atlas containing only the characters the app actually uses,
    /// instead of the full ~20k-glyph atlas (the 55 MB asset). Two steps:
    ///   ⑥ Scan poem data + UI source for every unique character -> writes poem_charset.txt.
    ///   ⑦ Bake a static SDF atlas from a selected source font covering exactly that charset.
    /// The original font files and full atlas are left untouched — the subset is written alongside.
    /// </summary>
    public static class FontSubsetBuilder
    {
        // --- Output locations (original assets are never modified) ---
        private const string OutputFolder = "Assets/Fonts/Subset";
        private const string CharsetPath = OutputFolder + "/poem_charset.txt";

        // --- Atlas bake parameters (tune here if you need higher quality / smaller size) ---
        private const int SamplingPointSize = 64;   // glyph render size; 64 is plenty for SDF
        private const int AtlasPadding = 6;
        private const int AtlasWidth = 4096;
        private const int AtlasHeight = 4096;

        // Folders scanned for characters that must be in the atlas.
        private static readonly string[] ScanFolders =
        {
            "Assets/StreamingAssets/PoemData", // poem / question content (*.json)
            "Assets/Scripts",                  // UI labels written as Chinese string literals (*.cs)
        };

        // ---------------------------------------------------------------------------------------
        // ⑥ Scan: collect the unique character set the app needs.
        // ---------------------------------------------------------------------------------------
        [MenuItem("唐诗宋词/字体/⑥ 扫描诗词字符集（生成精简集）")]
        public static void ScanCharset()
        {
            var chars = CollectCharacters();
            if (chars.Count == 0)
            {
                EditorUtility.DisplayDialog("字体精简",
                    "未扫描到任何字符，请确认 Assets/StreamingAssets/PoemData 下有诗词数据。", "好");
                return;
            }

            // Stable order so the file diffs cleanly in git.
            var sorted = new List<char>(chars);
            sorted.Sort();
            string content = new string(sorted.ToArray());

            EnsureFolder();
            string fullPath = ToFullPath(CharsetPath);
            File.WriteAllText(fullPath, content, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(CharsetPath);

            int cjk = 0;
            foreach (char c in sorted) if (c >= 0x2E80) cjk++;

            Debug.Log($"[FontSubset] 扫描完成：共 {chars.Count} 个唯一字符（其中 CJK {cjk} 个）-> {CharsetPath}");
            EditorUtility.DisplayDialog("字体精简",
                $"扫描完成！\n唯一字符：{chars.Count} 个（CJK {cjk} 个）\n已写入：{CharsetPath}\n\n" +
                "接着在 Project 选中源字体（如 NOTOSERIFCJKSC-REGULAR.OTF），执行菜单⑦导出精简图集。", "好");
        }

        // ---------------------------------------------------------------------------------------
        // ⑦ Export: bake a static SDF atlas from the selected font covering only the scanned chars.
        // ---------------------------------------------------------------------------------------
        [MenuItem("唐诗宋词/字体/⑦ 用源字体导出精简SDF图集")]
        public static void ExportTrimmedAtlas()
        {
            var font = Selection.activeObject as Font;
            if (font == null)
            {
                EditorUtility.DisplayDialog("字体精简",
                    "请先在 Project 窗口选中一个源字体（已导入的 .ttf/.otf Font 资源，\n" +
                    "如 NOTOSERIFCJKSC-REGULAR.OTF），再执行本命令。", "好");
                return;
            }

            string fullCharsetPath = ToFullPath(CharsetPath);
            if (!File.Exists(fullCharsetPath))
            {
                EditorUtility.DisplayDialog("字体精简",
                    "尚未生成字符集。请先执行菜单⑥ 扫描诗词字符集。", "好");
                return;
            }

            string charset = File.ReadAllText(fullCharsetPath, Encoding.UTF8);
            if (string.IsNullOrEmpty(charset))
            {
                EditorUtility.DisplayDialog("字体精简", "字符集文件为空，请重新执行菜单⑥。", "好");
                return;
            }

            TMP_FontAsset fa;
            try
            {
                // Create as Dynamic first so TryAddCharacters can rasterize glyphs, then freeze to Static.
                fa = TMP_FontAsset.CreateFontAsset(
                    font, SamplingPointSize, AtlasPadding, GlyphRenderMode.SDFAA,
                    AtlasWidth, AtlasHeight, AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: true);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("字体精简",
                    "创建字体资源失败。该源字体可能不支持动态 SDF，请改用 Window ▸ TextMeshPro ▸ Font Asset Creator，\n" +
                    $"用 Character File 模式加载 {CharsetPath} 手动烘焙。", "好");
                return;
            }
            if (fa == null) return;

            if (!fa.TryAddCharacters(charset, out string missing))
            {
                Debug.LogWarning("[FontSubset] TryAddCharacters 报告部分失败。");
            }
            int missingCount = string.IsNullOrEmpty(missing) ? 0 : missing.Length;
            if (missingCount > 0)
            {
                Debug.LogWarning($"[FontSubset] 该源字体缺少 {missingCount} 个字形（将由回退字体或方块占位）：{missing}");
            }

            // Freeze: static atlas + drop the source-font reference so the 24 MB OTF is NOT pulled
            // into the build. (Keep the original full atlas/source files for future re-bakes.)
            fa.atlasPopulationMode = AtlasPopulationMode.Static;

            EnsureFolder();
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{OutputFolder}/{font.name}-Subset SDF.asset");
            string nameNoExt = Path.GetFileNameWithoutExtension(assetPath);

            AssetDatabase.CreateAsset(fa, assetPath);

            // Persist every atlas texture + the material as sub-assets so references survive reimport.
            if (fa.atlasTextures != null)
            {
                for (int i = 0; i < fa.atlasTextures.Length; i++)
                {
                    var tex = fa.atlasTextures[i];
                    if (tex == null) continue;
                    tex.name = nameNoExt + " Atlas" + (i == 0 ? "" : " " + i);
                    if (!AssetDatabase.Contains(tex)) AssetDatabase.AddObjectToAsset(tex, fa);
                }
            }
            if (fa.material != null)
            {
                fa.material.name = nameNoExt + " Material";
                if (!AssetDatabase.Contains(fa.material)) AssetDatabase.AddObjectToAsset(fa.material, fa);
            }

            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath);

            // Null the serialized source-font reference AFTER save (removes the build dependency on the OTF).
            var so = new SerializedObject(fa);
            var srcProp = so.FindProperty("m_SourceFontFile");
            if (srcProp != null)
            {
                srcProp.objectReferenceValue = null;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(fa);
                AssetDatabase.SaveAssets();
            }

            int atlasCount = fa.atlasTextures != null ? fa.atlasTextures.Length : 0;
            Debug.Log($"[FontSubset] 导出完成 -> {assetPath}（图集张数 {atlasCount}，缺字 {missingCount}）");
            EditorGUIUtility.PingObject(fa);
            Selection.activeObject = fa;
            EditorUtility.DisplayDialog("字体精简",
                $"导出完成！\n资源：{assetPath}\n图集张数：{atlasCount}　缺字：{missingCount}\n\n" +
                "下一步：用菜单②把它设为 TMP 默认字体（替换 55MB 的完整图集），再重新打包。\n" +
                (missingCount > 0 ? "注意：有缺字，建议保留原完整字体作为回退（菜单⑤）。" : ""), "好");
        }

        // ---------------------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------------------

        /// <summary>Scans poem data (*.json) and UI source (*.cs) for every renderable character,
        /// then unions a base set of ASCII + common CJK punctuation so labels never miss glyphs.</summary>
        private static HashSet<char> CollectCharacters()
        {
            var set = new HashSet<char>();

            // Base: printable ASCII (digits/letters/symbols) used in UI.
            for (char c = (char)0x20; c <= 0x7E; c++) set.Add(c);

            // Base: common Chinese punctuation that may not appear in scanned content.
            const string punctuation = "　、。「」『』《》〈〉（）【】〔〕…—·“”‘’：；！？，．•～";
            foreach (char c in punctuation) set.Add(c);

            foreach (string folder in ScanFolders)
            {
                string fullFolder = ToFullPath(folder);
                if (!Directory.Exists(fullFolder)) continue;

                string pattern = folder.EndsWith("PoemData") ? "*.json" : "*.cs";
                foreach (string file in Directory.GetFiles(fullFolder, pattern, SearchOption.AllDirectories))
                {
                    string text;
                    try { text = File.ReadAllText(file, Encoding.UTF8); }
                    catch (Exception e) { Debug.LogWarning($"[FontSubset] 读取失败 {file}: {e.Message}"); continue; }

                    foreach (char c in text)
                    {
                        if (char.IsControl(c) || char.IsWhiteSpace(c)) continue;
                        set.Add(c);
                    }
                }
            }

            return set;
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Fonts"))
                AssetDatabase.CreateFolder("Assets", "Fonts");
            if (!AssetDatabase.IsValidFolder(OutputFolder))
                AssetDatabase.CreateFolder("Assets/Fonts", "Subset");
        }

        private static string ToFullPath(string assetPath)
            => Path.Combine(Directory.GetCurrentDirectory(), assetPath);
    }
}
