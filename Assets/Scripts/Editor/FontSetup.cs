using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace PoemPoetry.Editor
{
    /// <summary>
    /// One-click Chinese TMP font setup. CreateFontAsset builds a Dynamic SDF asset (glyphs
    /// rasterized at runtime — the right mode for CJK), and we wire it as the TMP default.
    /// </summary>
    public static class FontSetup
    {
        [MenuItem("PoemPoetry/字体/① 用选中的TTF创建动态SDF并设为默认")]
        public static void CreateAndSetDefault()
        {
            var font = Selection.activeObject as Font;
            if (font == null)
            {
                EditorUtility.DisplayDialog("字体",
                    "请先在 Project 窗口选中一个已导入的中文字体（.ttf/.otf 的 Font 资源），再执行本命令。", "好");
                return;
            }

            TMP_FontAsset fontAsset;
            try
            {
                fontAsset = TMP_FontAsset.CreateFontAsset(font); // 默认 Dynamic SDF, 1024x1024
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("字体",
                    "自动创建失败，请改用 Window ▸ TextMeshPro ▸ Font Asset Creator 手动创建后，再用菜单②设为默认。", "好");
                return;
            }
            if (fontAsset == null) return;

            const string folder = "Assets/Fonts";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "Fonts");
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{font.name} SDF.asset");
            AssetDatabase.CreateAsset(fontAsset, path);

            // Persist the atlas texture + material as sub-assets so references survive a reimport.
            if (fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0 && fontAsset.atlasTextures[0] != null)
            {
                fontAsset.atlasTextures[0].name = font.name + " Atlas";
                AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
            }
            if (fontAsset.material != null)
            {
                fontAsset.material.name = font.name + " Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path);

            int added = AddAsciiFallbackTo(fontAsset);
            if (SetDefault(fontAsset))
                EditorUtility.DisplayDialog("字体",
                    $"完成！已创建 {path} 并设为 TMP 默认字体（动态 SDF）。\n" +
                    (added > 0 ? "已添加数字/字母回退字体（LiberationSans）。\n" : "未找到 LiberationSans 回退（如数字不显示，请先导入 TMP Essentials 再点菜单④）。\n") +
                    "现在按 Play，中文与数字都应正常显示。", "好");
        }

        [MenuItem("PoemPoetry/字体/② 将选中的 TMP 字体设为默认")]
        public static void SetSelectedAsDefault()
        {
            var fa = Selection.activeObject as TMP_FontAsset;
            if (fa == null)
            {
                EditorUtility.DisplayDialog("字体",
                    "请先在 Project 窗口选中一个 TMP 字体资源（如「XXX SDF.asset」），再执行本命令。", "好");
                return;
            }
            if (SetDefault(fa))
                EditorUtility.DisplayDialog("字体", $"已将「{fa.name}」设为 TMP 默认字体。", "好");
        }

        [MenuItem("PoemPoetry/字体/③ 显示当前默认字体")]
        public static void ShowCurrent()
        {
            var settings = TMP_Settings.instance;
            string name = (settings != null && TMP_Settings.defaultFontAsset != null)
                ? TMP_Settings.defaultFontAsset.name : "(未设置)";
            EditorUtility.DisplayDialog("字体", "当前 TMP 默认字体：" + name, "好");
        }

        [MenuItem("PoemPoetry/字体/④ 给当前默认字体加 数字字母 回退")]
        public static void AddAsciiFallbackToDefault()
        {
            var main = TMP_Settings.defaultFontAsset;
            if (main == null)
            {
                EditorUtility.DisplayDialog("字体", "尚未设置默认字体，请先用菜单①或②。", "好");
                return;
            }
            int added = AddAsciiFallbackTo(main);
            EditorUtility.DisplayDialog("字体", added > 0
                ? $"已为「{main.name}」添加数字/字母回退字体（LiberationSans）。重新 Play 即可。"
                : "找不到 LiberationSans SDF。请先 Window ▸ TextMeshPro ▸ Import TMP Essential Resources。", "好");
        }

        /// <summary>Adds the built-in LiberationSans (which has ASCII/digits) as a fallback so
        /// space/numbers/underscores always render even if the CJK font lacks them.</summary>
        private static int AddAsciiFallbackTo(TMP_FontAsset main)
        {
            var lib = FindLiberation();
            if (lib == null || lib == main) return 0;
            if (main.fallbackFontAssetTable == null)
                main.fallbackFontAssetTable = new List<TMP_FontAsset>();
            if (!main.fallbackFontAssetTable.Contains(lib))
                main.fallbackFontAssetTable.Add(lib);
            EditorUtility.SetDirty(main);
            AssetDatabase.SaveAssets();
            return 1;
        }

        [MenuItem("PoemPoetry/字体/⑤ 把选中的字体加为回退（补缺标点等）")]
        public static void AddSelectedAsFallback()
        {
            var main = TMP_Settings.defaultFontAsset;
            if (main == null)
            {
                EditorUtility.DisplayDialog("字体", "尚未设置默认字体，请先用菜单①或②。", "好");
                return;
            }

            var fb = Selection.activeObject as TMP_FontAsset;
            if (fb == null)
            {
                var font = Selection.activeObject as Font;
                if (font == null)
                {
                    EditorUtility.DisplayDialog("字体", "请在 Project 选中一个字体(.ttf/.otf)或 TMP 字体资源，再执行本命令。", "好");
                    return;
                }
                fb = CreateSdfAsset(font);
            }
            if (fb == null) return;
            if (fb == main)
            {
                EditorUtility.DisplayDialog("字体", "不能把默认字体设为它自己的回退。", "好");
                return;
            }

            if (main.fallbackFontAssetTable == null)
                main.fallbackFontAssetTable = new List<TMP_FontAsset>();
            if (!main.fallbackFontAssetTable.Contains(fb))
                main.fallbackFontAssetTable.Add(fb);
            EditorUtility.SetDirty(main);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("字体",
                $"已将「{fb.name}」加为「{main.name}」的回退字体。\n重新 Play，缺失的中文标点等应由它补上。", "好");
        }

        /// <summary>Creates a Dynamic SDF font asset from a Font and saves it under Assets/Fonts.</summary>
        private static TMP_FontAsset CreateSdfAsset(Font font)
        {
            TMP_FontAsset fa;
            try { fa = TMP_FontAsset.CreateFontAsset(font); }
            catch (System.Exception e) { Debug.LogException(e); return null; }
            if (fa == null) return null;

            const string folder = "Assets/Fonts";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "Fonts");
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{font.name} SDF.asset");
            AssetDatabase.CreateAsset(fa, path);
            if (fa.atlasTextures != null && fa.atlasTextures.Length > 0 && fa.atlasTextures[0] != null)
            {
                fa.atlasTextures[0].name = font.name + " Atlas";
                AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            }
            if (fa.material != null)
            {
                fa.material.name = font.name + " Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
            }
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path);
            return fa;
        }

        private static TMP_FontAsset FindLiberation()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(p);
                if (fa != null && fa.name.Contains("LiberationSans")) return fa;
            }
            return null;
        }

        private static bool SetDefault(TMP_FontAsset fontAsset)
        {
            var settings = TMP_Settings.instance;
            if (settings == null)
            {
                EditorUtility.DisplayDialog("字体",
                    "未找到 TMP Settings。请先 Window ▸ TextMeshPro ▸ Import TMP Essential Resources，再重试。", "好");
                return false;
            }
            var so = new SerializedObject(settings);
            var prop = so.FindProperty("m_defaultFontAsset");
            if (prop == null)
            {
                EditorUtility.DisplayDialog("字体",
                    "无法定位默认字体字段，请改用 TMP Project Settings 手动设置 Default Font Asset。", "好");
                return false;
            }
            prop.objectReferenceValue = fontAsset;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return true;
        }
    }
}
