using System.Collections.Generic;
using PoemPoetry.Core;
using PoemPoetry.Data;
using PoemPoetry.UI;
using TMPro;
using UnityEngine;

namespace PoemPoetry.App
{
    /// <summary>
    /// Single entry point. Builds the canvas, loads content + user data, then shows the main
    /// menu. Drop this on one empty GameObject in a scene and press Play — no prefabs needed.
    /// </summary>
    public sealed class AppBootstrapper : MonoBehaviour
    {
        private ScreenNavigator _nav;

        private async void Start()
        {
            EnsureAsciiFallback();

            var (_, root) = UiKit.CreateCanvas("MainCanvas");
            UiKit.EnsureEventSystem();

            var audioGo = new GameObject("AudioManager");
            audioGo.AddComponent<AudioManager>();

            var loading = UiKit.Text("Loading", root, "载入中…", 48, TMPro.TextAlignmentOptions.Center, UiKit.Ink);
            UiKit.StretchFull(loading.gameObject);

            AppServices services;
            try
            {
                var content = new JsonContentSource(new StreamingAssetsTextLoader(Application.streamingAssetsPath));
                services = await AppServices.CreateAsync(content, Application.persistentDataPath);
            }
            catch (System.Exception e)
            {
                loading.text = "内容加载失败：\n" + e.Message;
                Debug.LogException(e);
                return;
            }

            GameApp.Services = services;
            Destroy(loading.gameObject);

            var navGo = new GameObject("ScreenNavigator");
            navGo.transform.SetParent(transform, false);
            _nav = navGo.AddComponent<ScreenNavigator>();
            _nav.Init(root, services);
            _nav.Push<MainMenuScreen>();
        }

        private void Update()
        {
            // Android hardware back / Esc.
            if (_nav != null && Input.GetKeyDown(KeyCode.Escape))
                _nav.HandleBack();
        }

        /// <summary>
        /// Ensure the active TMP font can render ASCII (space, digits, /, %, _) by chaining the
        /// built-in LiberationSans as a fallback. Many CJK fonts lack ASCII glyphs and TMP stops
        /// rendering a run at the first missing glyph — this prevents that, with no manual setup.
        /// </summary>
        private static void EnsureAsciiFallback()
        {
            var def = TMP_Settings.defaultFontAsset;
            if (def == null) return;
            var lib = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (lib == null || lib == def) return;
            if (def.fallbackFontAssetTable == null)
                def.fallbackFontAssetTable = new List<TMP_FontAsset>();
            if (!def.fallbackFontAssetTable.Contains(lib))
                def.fallbackFontAssetTable.Add(lib);
        }
    }
}
