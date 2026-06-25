using System.Collections;
using System.Collections.Generic;
using System.Text;
using PoemPoetry.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoemPoetry.UI
{
    /// <summary>诗词详情: author band · poem body (lattice corners) · 译文/赏析, with a pinned 收藏 footer.</summary>
    public sealed class PoemDetailScreen : UIScreen
    {
        protected override void OnShow(object args)
        {
            var a = args as PoemDetailArgs;
            var poem = a != null ? Services.Content.GetPoem(a.PoemId) : null;
            var body = Design.Chrome(gameObject, () => Nav.Pop(), () => Nav.Push<SettingsScreen>(),
                poem != null ? poem.Title : "诗词");
            UiKit.VerticalGroup(body.gameObject, spacing: 12, padX: 28, padY: 16, align: TextAnchor.UpperCenter);

            if (poem == null)
            {
                UiKit.Text("E", body, "诗词不存在", 36, TextAlignmentOptions.Center, Design.OnSurfaceVariant);
                return;
            }

            var scroll = UiKit.ScrollList("Body", body, out _);

            // Author band: — 〔朝代〕作者 —
            var showCipai = !string.IsNullOrEmpty(poem.Cipai) && (poem.Title == null || !poem.Title.Contains(poem.Cipai));
            var metaStr = $"〔{poem.Dynasty}〕{poem.Author}" + (showCipai ? $" · {poem.Cipai}" : "");
            var meta = UiKit.Text("Meta", scroll, $"—  {metaStr}  —", 30, TextAlignmentOptions.Center, Design.OnSurfaceVariant);
            meta.characterSpacing = 6f;
            UiKit.MinHeight(meta.gameObject, 60);

            // Prev/next navigation across the list this poem was opened from (+ swipe).
            if (a != null && a.Siblings != null && a.Siblings.Count > 1)
            {
                int n = a.Siblings.Count;
                var navRow = UiKit.Panel("Nav", scroll);
                UiKit.Pref(navRow, minH: 78);
                var navHg = UiKit.HorizontalGroup(navRow, spacing: 12);
                navHg.childForceExpandHeight = false;
                var prev = UiKit.Button("Prev", navRow.transform, "上一首", out _, Design.SurfaceHigh, 30);
                UiKit.Pref(prev.gameObject, minH: 78);
                prev.onClick.AddListener(() => Open(a.Siblings, (a.Index - 1 + n) % n, -1));
                var pos = UiKit.Text("Pos", navRow.transform, $"{a.Index + 1}/{n}", 28, TextAlignmentOptions.Center, Design.OnSurfaceVariant);
                pos.raycastTarget = false;
                var next = UiKit.Button("Next", navRow.transform, "下一首", out _, Design.SurfaceHigh, 30);
                UiKit.Pref(next.gameObject, minH: 78);
                next.onClick.AddListener(() => Open(a.Siblings, (a.Index + 1) % n, +1));

                // Left/right swipe flips to next/prev poem (slide in from the corresponding side).
                var swipe = gameObject.AddComponent<SwipeNav>();
                swipe.OnSwipeLeft = () => Open(a.Siblings, (a.Index + 1) % n, +1);
                swipe.OnSwipeRight = () => Open(a.Siblings, (a.Index - 1 + n) % n, -1);
            }

            BuildPoemBody(scroll, poem);

            if (a?.ResultContext != null && !a.ResultContext.IsCorrect)
            {
                var yc = UiKit.Text("Ctx", scroll, $"<color=#BF4038>你的作答：{a.ResultContext.ChosenText}</color>",
                    30, TextAlignmentOptions.Center, Design.OnSurfaceVariant);
                UiKit.MinHeight(yc.gameObject, 56);
            }

            BuildDifficultyEditor(scroll, poem);
            AddSection(scroll, "译文", poem.Translation);
            AddSection(scroll, "赏析", poem.Appreciation);

            BuildFooter(body, poem.Id);

            if (a != null && a.SlideFrom != 0) StartCoroutine(SlideIn(a.SlideFrom));
        }

        // New page slides in from the side it was summoned from (next = right, prev = left).
        private IEnumerator SlideIn(int dir)
        {
            var rt = (RectTransform)transform;
            float w = rt.rect.width > 1f ? rt.rect.width : 1080f;
            Vector2 from = new Vector2(dir * w, 0f);
            Vector2 to = Vector2.zero;
            const float dur = 0.18f;
            float t = 0f;
            rt.anchoredPosition = from;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                rt.anchoredPosition = Vector2.LerpUnclamped(from, to, k);
                yield return null;
            }
            rt.anchoredPosition = to;
        }

        private void BuildPoemBody(Transform scroll, Poem poem)
        {
            // Each clause on its own line (vertical reading column).
            var sb = new StringBuilder();
            foreach (var line in poem.Lines)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(line.Text);
            }
            var box = UiKit.Panel("PoemBox", scroll);
            var poemText = UiKit.Text("Poem", box.transform, sb.ToString(), 48, TextAlignmentOptions.Center, Design.Ink);
            poemText.lineSpacing = 18f; poemText.characterSpacing = 6f;
            UiKit.StretchFull(poemText.gameObject, 36);
            float poemH = poemText.GetPreferredValues(sb.ToString(), 100000f, 0f).y + 100f;
            UiKit.Pref(box, minH: poemH);
            Design.Corners(box, Design.Alpha(Design.Primary, 0.3f), arm: 34, thick: 2, inset: 8);
        }

        private void BuildDifficultyEditor(Transform scroll, Poem poem)
        {
            Design.SectionHead(scroll, "难度（点选可改 · 本地生效）");
            var row = UiKit.Panel("DiffRow", scroll);
            UiKit.Pref(row, minH: 92);
            var hg = UiKit.HorizontalGroup(row, spacing: 12);
            hg.childForceExpandHeight = false;
            int[] tiers = { 0, 1, 2, 3 };
            var diffBtns = new List<Button>();
            var diffLbls = new List<TextMeshProUGUI>();
            foreach (var t in tiers)
            {
                int tier = t;
                var b = UiKit.Button("D" + t, row.transform, QuizConfigScreen.TierLabel(t), out var lbl, Design.SurfaceHigh, 26);
                UiKit.Pref(b.gameObject, minH: 84);
                diffBtns.Add(b); diffLbls.Add(lbl);
                b.onClick.AddListener(async () =>
                {
                    await Services.Difficulty.SetAsync(poem.Id, tier);
                    for (int i = 0; i < tiers.Length; i++)
                        Design.SetChip(diffBtns[i], diffLbls[i], tiers[i] == tier);
                });
            }
            for (int i = 0; i < tiers.Length; i++)
                Design.SetChip(diffBtns[i], diffLbls[i], tiers[i] == poem.Difficulty);
        }

        private static void AddSection(Transform scroll, string title, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Design.SectionHead(scroll, $"【{title}】");
            var body = UiKit.Text(title + "B", scroll, text, 30, TextAlignmentOptions.Left, Design.Ink, wrap: true);
            body.lineSpacing = 8f;
            // No LayoutElement: let TMP report its own wrapped height to the scroll layout group.
        }

        private void BuildFooter(Transform body, string poemId)
        {
            var footer = UiKit.Panel("Footer", body);
            UiKit.Pref(footer, minH: 116).flexibleHeight = 0f;
            UiKit.HorizontalGroup(footer, spacing: 0);
            var favBtn = UiKit.Button("Fav", footer.transform, "收藏作品", out var favLbl, Design.SecondaryFixed, 36);
            favLbl.color = Design.Primary;
            SetFav(favLbl, poemId);
            favBtn.onClick.AddListener(async () =>
            {
                bool on = await Services.Favorites.ToggleAsync(poemId);
                ApplyFav(favLbl, on);
            });
        }

        private void Open(List<string> siblings, int index, int dir)
        {
            Nav.Replace<PoemDetailScreen>(new PoemDetailArgs
            {
                PoemId = siblings[index], Siblings = siblings, Index = index, SlideFrom = dir,
            });
        }

        private async void SetFav(TextMeshProUGUI lbl, string poemId)
        {
            bool fav = await Services.Favorites.IsFavoriteAsync(poemId);
            ApplyFav(lbl, fav);
        }

        private static void ApplyFav(TextMeshProUGUI lbl, bool on)
        {
            lbl.text = on ? "已收藏作品" : "收藏作品";
            lbl.color = on ? Design.Primary : Design.Ink;
        }
    }
}
