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
    /// instead of the full ~20k-glyph atlas (the 55 MB asset). The source font is always
    /// <see cref="FontSetup.SourceFontPath"/> (NOTOSERIFCJKSC-REGULAR) — no manual selection needed.
    ///
    /// 一键命令会顺序执行：扫描字符集 → 用 NOTOSERIFCJKSC 烘焙精简图集 → 设为 TMP 默认字体。
    /// 单步命令保留，便于排查。原始字体与完整图集始终不被修改。
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
        // ① One-click: scan → bake subset from NOTOSERIFCJKSC → set as TMP default.
        // ---------------------------------------------------------------------------------------
        [MenuItem("PoemPoetry/字体/① 一键：生成精简图集并应用 (NOTOSERIFCJKSC)", priority = 20)]
        public static void BuildSubsetAndApply()
        {
            string charset = ScanAndWriteCharset(out int total, out int cjk);
            if (charset == null) return;

            var font = FontSetup.LoadSourceFont();
            if (font == null) return;

            var fa = BakeSubset(font, charset, out int missing, out string assetPath, out int atlasCount);
            if (fa == null) return;

            if (!FontSetup.ApplyAsDefault(fa, out int asciiAdded)) return;

            EditorGUIUtility.PingObject(fa);
            Selection.activeObject = fa;
            EditorUtility.DisplayDialog("字体精简",
                $"完成！已生成精简图集并设为 TMP 默认字体。\n" +
                $"资源：{assetPath}\n" +
                $"字符：{total} 个（CJK {cjk}）　图集张数：{atlasCount}　缺字：{missing}\n" +
                (asciiAdded > 0 ? "已添加数字/字母回退（LiberationSans）。\n" : "") +
                (missing > 0 ? "注意：有缺字，建议把完整字体加为回退（菜单：把选中的字体加为回退）。\n" : "") +
                "重新 Play / 打包即可。", "好");
        }

        // ---------------------------------------------------------------------------------------
        // Scan only: collect the unique character set the app needs.
        // ---------------------------------------------------------------------------------------
        [MenuItem("PoemPoetry/字体/扫描诗词字符集", priority = 35)]
        public static void ScanCharset()
        {
            string charset = ScanAndWriteCharset(out int total, out int cjk);
            if (charset == null) return;
            Debug.Log($"[FontSubset] 扫描完成：共 {total} 个唯一字符（其中 CJK {cjk} 个）-> {CharsetPath}");
            EditorUtility.DisplayDialog("字体精简",
                $"扫描完成！\n唯一字符：{total} 个（CJK {cjk} 个）\n已写入：{CharsetPath}\n\n" +
                "接着执行菜单「导出精简图集 (NOTOSERIFCJKSC)」，或直接用「① 一键」。", "好");
        }

        // ---------------------------------------------------------------------------------------
        // Bake only: build a static SDF atlas from NOTOSERIFCJKSC covering the scanned charset.
        // ---------------------------------------------------------------------------------------
        [MenuItem("PoemPoetry/字体/导出精简图集 (NOTOSERIFCJKSC)", priority = 36)]
        public static void ExportTrimmedAtlas()
        {
            string fullCharsetPath = ToFullPath(CharsetPath);
            if (!File.Exists(fullCharsetPath))
            {
                EditorUtility.DisplayDialog("字体精简", "尚未生成字符集。请先执行菜单「扫描诗词字符集」。", "好");
                return;
            }
            string charset = File.ReadAllText(fullCharsetPath, Encoding.UTF8);
            if (string.IsNullOrEmpty(charset))
            {
                EditorUtility.DisplayDialog("字体精简", "字符集文件为空，请重新执行「扫描诗词字符集」。", "好");
                return;
            }

            var font = FontSetup.LoadSourceFont();
            if (font == null) return;

            var fa = BakeSubset(font, charset, out int missing, out string assetPath, out int atlasCount);
            if (fa == null) return;

            EditorGUIUtility.PingObject(fa);
            Selection.activeObject = fa;
            EditorUtility.DisplayDialog("字体精简",
                $"导出完成！\n资源：{assetPath}\n图集张数：{atlasCount}　缺字：{missing}\n\n" +
                "下一步：用菜单「将选中的 TMP 字体设为默认」应用它，或下次直接用「① 一键」。\n" +
                (missing > 0 ? "注意：有缺字，建议把完整字体加为回退。" : ""), "好");
        }

        // ---------------------------------------------------------------------------------------
        // Core helpers (UI-free, reused by the menus above)
        // ---------------------------------------------------------------------------------------

        /// <summary>Scans the charset, writes it to <see cref="CharsetPath"/>, and returns it.
        /// Returns null (after a dialog) if nothing was found.</summary>
        private static string ScanAndWriteCharset(out int total, out int cjk)
        {
            total = 0; cjk = 0;
            var chars = CollectCharacters();
            if (chars.Count == 0)
            {
                EditorUtility.DisplayDialog("字体精简",
                    "未扫描到任何字符，请确认 Assets/StreamingAssets/PoemData 下有诗词数据。", "好");
                return null;
            }

            // Stable order so the file diffs cleanly in git.
            var sorted = new List<char>(chars);
            sorted.Sort();
            string content = new string(sorted.ToArray());

            EnsureFolder();
            File.WriteAllText(ToFullPath(CharsetPath), content, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(CharsetPath);

            total = chars.Count;
            foreach (char c in sorted) if (c >= 0x2E80) cjk++;
            return content;
        }

        /// <summary>Bakes a static subset SDF asset from <paramref name="font"/> covering
        /// <paramref name="charset"/>. Returns the asset (or null after a dialog on failure).</summary>
        private static TMP_FontAsset BakeSubset(
            Font font, string charset, out int missingCount, out string assetPath, out int atlasCount)
        {
            missingCount = 0; assetPath = null; atlasCount = 0;

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
                return null;
            }
            if (fa == null) return null;

            if (!fa.TryAddCharacters(charset, out string missing))
                Debug.LogWarning("[FontSubset] TryAddCharacters 报告部分失败。");
            missingCount = string.IsNullOrEmpty(missing) ? 0 : missing.Length;
            if (missingCount > 0)
                Debug.LogWarning($"[FontSubset] 该源字体缺少 {missingCount} 个字形（将由回退字体或方块占位）：{missing}");

            // Freeze: static atlas + drop the source-font reference so the 24 MB OTF is NOT pulled
            // into the build. (Keep the original full atlas/source files for future re-bakes.)
            fa.atlasPopulationMode = AtlasPopulationMode.Static;

            EnsureFolder();
            // Overwrite a single canonical asset; never accumulate “…-Subset SDF 1/2/3.asset”.
            // PrepareOverwrite also clears stale numbered duplicates from earlier runs.
            assetPath = $"{OutputFolder}/{font.name}-Subset SDF.asset";
            bool wasDefault = FontSetup.PrepareOverwrite(assetPath);
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

            // Overwrite gave the asset a fresh GUID, so re-wire it as TMP default if it was before
            // (otherwise TMP_Settings would point at the just-deleted asset).
            if (wasDefault) FontSetup.ApplyAsDefault(fa, out _);

            atlasCount = fa.atlasTextures != null ? fa.atlasTextures.Length : 0;
            Debug.Log($"[FontSubset] 导出完成 -> {assetPath}（图集张数 {atlasCount}，缺字 {missingCount}）");
            return fa;
        }

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
                // From poem data (*.json) take every character; from source (*.cs) take only CJK,
                // so comment/dialog symbols like ▸ ✓ ← don't pollute the atlas (ASCII + common
                // punctuation are already covered by the base set above).
                bool cjkOnly = pattern == "*.cs";
                foreach (string file in Directory.GetFiles(fullFolder, pattern, SearchOption.AllDirectories))
                {
                    string text;
                    try { text = File.ReadAllText(file, Encoding.UTF8); }
                    catch (Exception e) { Debug.LogWarning($"[FontSubset] 读取失败 {file}: {e.Message}"); continue; }

                    foreach (char c in text)
                    {
                        if (char.IsControl(c) || char.IsWhiteSpace(c)) continue;
                        if (cjkOnly && c < 0x2E80) continue;
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
