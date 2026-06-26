using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using PoemPoetry.Core;
using PoemPoetry.Data;
using PoemPoetry.Services;
using PoemPoetry.UI;
using TMPro;
using UnityEngine;

namespace PoemPoetry.App
{
    /// <summary>
    /// Self-contained automation that walks through every directly-reachable screen and saves a
    /// PNG of each into the project's <c>screenshots/</c> folder. Drop it on a lone GameObject in
    /// an otherwise-empty scene and press Play (or use the <c>PoemPoetry/截图</c> menu).
    ///
    /// It boots the app independently of <see cref="AppBootstrapper"/>: content is loaded from
    /// StreamingAssets as usual, but user data (records / favorites / 错题) is written to a throwaway
    /// temp directory and seeded with sample entries, so the real save file is never touched and
    /// data-driven screens (历史记录 / 记录详情 / 错题本 / 收藏夹 / 答题结果) render with content.
    ///
    /// Capture is resolution-independent: the UI canvas is rendered through a dedicated camera into
    /// a fixed 1080×1920 RenderTexture, so the output matches the design reference regardless of the
    /// Game view size.
    /// </summary>
    public sealed class ScreenshotRunner : MonoBehaviour
    {
        [Tooltip("截图输出分辨率（竖屏）。")]
        public int Width = 1080;
        public int Height = 1920;

        [Tooltip("每屏在截图前等待的秒数，供异步数据载入与布局稳定。")]
        public float SettleSeconds = 0.5f;

        [Tooltip("跑完后在编辑器里自动退出 Play 模式。")]
        public bool ExitPlayModeWhenDone = true;

        private ScreenNavigator _nav;
        private Camera _shotCam;
        private RenderTexture _rt;
        private string _outDir;
        private readonly List<string> _saved = new List<string>();

        private async void Start()
        {
            EnsureAsciiFallback();

            _outDir = ResolveOutputDir();
            Directory.CreateDirectory(_outDir);

            var (canvas, root) = UiKit.CreateCanvas("ShotCanvas");
            UiKit.EnsureEventSystem();

            // Render the overlay UI through our own camera into a fixed-size target.
            _rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "ShotRT" };
            _rt.Create();
            var camGo = new GameObject("ShotCamera");
            camGo.transform.SetParent(transform, false);
            _shotCam = camGo.AddComponent<Camera>();
            _shotCam.clearFlags = CameraClearFlags.SolidColor;
            _shotCam.backgroundColor = UiKit.Paper;
            _shotCam.orthographic = true;
            _shotCam.cullingMask = ~0;
            _shotCam.nearClipPlane = 0.1f;
            _shotCam.farClipPlane = 1000f;
            _shotCam.targetTexture = _rt;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _shotCam;
            canvas.planeDistance = 100f;

            AppServices services;
            try
            {
                var content = new JsonContentSource(new StreamingAssetsTextLoader(Application.streamingAssetsPath));
                // Throwaway user-data dir so the real save file is never modified.
                var tempUserDir = Path.Combine(Application.temporaryCachePath, "screenshot_userdata");
                if (Directory.Exists(tempUserDir)) Directory.Delete(tempUserDir, true);
                Directory.CreateDirectory(tempUserDir);
                services = await AppServices.CreateAsync(content, tempUserDir);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Finish(error: "内容加载失败：" + e.Message);
                return;
            }

            GameApp.Services = services;
            var seed = await SeedSampleDataAsync(services);

            var navGo = new GameObject("ScreenNavigator");
            navGo.transform.SetParent(transform, false);
            _nav = navGo.AddComponent<ScreenNavigator>();
            _nav.Init(root, services);

            StartCoroutine(CaptureAll(seed));
        }

        // ── Sample data so data-driven screens render with real-looking content. ────────────────
        private sealed class Seed
        {
            public string RecordId;
            public List<string> RecordSiblings = new List<string>();
            public ChallengeRecord ResultRecord;
            public string PoemId;
        }

        private static async Task<Seed> SeedSampleDataAsync(AppServices s)
        {
            var seed = new Seed();
            var poems = s.Content.Poems;
            if (poems != null && poems.Count > 0)
            {
                seed.PoemId = poems[0].Id;
                // Favorite a few poems so the 收藏夹 isn't empty.
                int favs = Math.Min(5, poems.Count);
                for (int i = 0; i < favs; i++) await s.Favorites.ToggleAsync(poems[i].Id);
            }

            // Two finished challenge runs → 历史记录 / 记录详情 / 答题结果 have content, and the second
            // run intentionally misses some questions to populate the 错题本.
            for (int run = 0; run < 2; run++)
            {
                var settings = new ChallengeSettings { QuestionCount = 6 };
                var pool = s.Content.BuildRuntimeQuestions(settings, new SystemRandomSource(), settings.QuestionCount);
                var session = s.Quiz.BuildSession(pool, settings);
                var results = new List<QuestionResult>(session.Total);
                for (int i = 0; i < session.Total; i++)
                {
                    var qq = session.Questions[i];
                    // Run 0: mostly right (~83%). Run 1: a couple wrong.
                    bool wrong = run == 0 ? (i == 2) : (i % 2 == 1);
                    int chosen = wrong ? (qq.CorrectIndex + 1) % 4 : qq.CorrectIndex;
                    results.Add(s.Quiz.BuildResult(qq, chosen, 2600 + i * 700));
                    if (wrong && qq.Source != null)
                        await s.WrongBook.RegisterWrongAsync(qq.Source.Id, qq.PoemId);
                }
                if (results.Count == 0) continue;
                var rec = await s.Records.SaveCompletedAsync(results, settings, 95 + run * 12, "challenge");
                seed.RecordSiblings.Add(rec.Id);
                if (run == 0) { seed.RecordId = rec.Id; seed.ResultRecord = rec; }
            }
            if (seed.RecordId == null && seed.RecordSiblings.Count > 0) seed.RecordId = seed.RecordSiblings[0];
            return seed;
        }

        // ── The capture sequence. Each step swaps in one screen and snapshots it. ────────────────
        private IEnumerator CaptureAll(Seed seed)
        {
            // File names mirror the curated screenshots/ scheme (组-序-名称).
            yield return Step("1-主界面", () => Go<MainMenuScreen>(null));

            yield return Step("2-1-答题设置", () => Go<QuizConfigScreen>(null));
            yield return Step("2-2-答题", () => Go<QuizScreen>(new QuizStartArgs { QuestionCount = 10, Mode = "challenge" }));
            if (seed.ResultRecord != null)
                yield return Step("2-3-答题结束，逐字填空结束", () => Go<ResultScreen>(new ResultArgs { Record = seed.ResultRecord }));

            yield return Step("3-1-滑动找诗设置", () => Go<SlideConfigScreen>(null));
            // Capture the live board, then force the 结算 overlay onto the same screen for 3-3.
            var slide = Go<SlidePuzzleScreen>(new SlideStartArgs());
            yield return Shoot("3-2-滑动找诗");
            slide.CaptureForceResult();
            yield return Shoot("3-3-滑动找诗结束");

            yield return Step("4-1-逐字填空设置", () => Go<WordClozeConfigScreen>(null));
            yield return Step("4-2-逐字填空", () => Go<WordClozeScreen>(new WordClozeStartArgs { QuestionCount = 10, Mode = "wordcloze" }));

            yield return Step("5-1-历史记录", () => Go<RecordsScreen>(null));
            if (seed.RecordId != null)
                yield return Step("5-2-一组历史记录", () => Go<RecordDetailScreen>(new RecordDetailArgs
                {
                    RecordId = seed.RecordId, Siblings = seed.RecordSiblings, Index = 0,
                }));
            if (seed.PoemId != null)
                yield return Step("5-3-诗词详情", () => Go<PoemDetailScreen>(new PoemDetailArgs { PoemId = seed.PoemId }));

            yield return Step("6-收藏夹", () => Go<FavoritesScreen>(null));
            yield return Step("7-错题本", () => Go<WrongBookScreen>(null));
            yield return Step("8-设置", () => Go<SettingsScreen>(null));

            Finish();
        }

        private T Go<T>(object args) where T : UIScreen =>
            _nav.Depth == 0 ? _nav.Push<T>(args) : _nav.Replace<T>(args);

        private IEnumerator Step(string fileName, Action show)
        {
            show();
            yield return Shoot(fileName);
        }

        /// <summary>Let BuildUI/OnShow, async data loads and layout settle, then snapshot.</summary>
        private IEnumerator Shoot(string fileName)
        {
            yield return null;
            yield return null;
            if (SettleSeconds > 0f) yield return new WaitForSecondsRealtime(SettleSeconds);
            Canvas.ForceUpdateCanvases();
            yield return new WaitForEndOfFrame();
            Capture(fileName);
        }

        private void Capture(string fileName)
        {
            _shotCam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            var path = Path.Combine(_outDir, fileName + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            _saved.Add(fileName);
            Debug.Log($"[Screenshot] 已保存 {fileName}.png");
        }

        private void Finish(string error = null)
        {
            if (error != null) Debug.LogError("[Screenshot] " + error);
            else Debug.Log($"[Screenshot] 完成，共 {_saved.Count} 张 → {_outDir}");

            if (_rt != null) { _rt.Release(); Destroy(_rt); }

#if UNITY_EDITOR
            if (ExitPlayModeWhenDone) UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        /// <summary>Chain LiberationSans as a TMP fallback so ASCII (digits / % / /) render — mirrors
        /// <see cref="AppBootstrapper"/> so captured text isn't missing glyphs.</summary>
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

        private static string ResolveOutputDir()
        {
#if UNITY_EDITOR
            // Project root is the parent of the Assets folder.
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "screenshots"));
#else
            return Path.Combine(Application.persistentDataPath, "screenshots");
#endif
        }
    }
}
