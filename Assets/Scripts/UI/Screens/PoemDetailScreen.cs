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
        private List<QuestionResult> _siblingResults;

        protected override void OnShow(object args)
        {
            var a = args as PoemDetailArgs;
            _siblingResults = a?.SiblingResults;
            var poem = a != null ? Services.Content.GetPoem(a.PoemId) : null;
            var body = Design.Chrome(gameObject, () => Nav.Pop(), () => Nav.Push<SettingsScreen>(),
                poem != null ? PoemFormat.DisplayTitle(poem.Title) : "诗词");
            UiKit.VerticalGroup(body.gameObject, spacing: 12, padX: 28, padY: 16, align: TextAnchor.UpperCenter);

            // 标题上方显示「当前/总数」(仅在按列表浏览时)。
            if (a != null && a.Siblings != null && a.Siblings.Count > 1)
            {
                float safeTop = UiKit.SafeTopInset(gameObject);
                var posLbl = UiKit.Text("Pos", gameObject.transform, $"{a.Index + 1} / {a.Siblings.Count}", 22,
                    TextAlignmentOptions.Center, Design.OnSurfaceVariant);
                UiKit.AnchorTop(posLbl.gameObject, height: 24, topOffset: safeTop + 6, sideMargin: 220);
            }

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

            // Left/right swipe flips to next/prev poem (prev/next buttons now live in the footer).
            if (a != null && a.Siblings != null && a.Siblings.Count > 1)
            {
                int n = a.Siblings.Count;
                var swipe = gameObject.AddComponent<SwipeNav>();
                swipe.OnSwipeLeft = () => Open(a.Siblings, (a.Index + 1) % n, +1);
                swipe.OnSwipeRight = () => Open(a.Siblings, (a.Index - 1 + n) % n, -1);
            }

            BuildPoemBody(scroll, poem, a);

            if (a?.ResultContext != null && !a.ResultContext.IsCorrect &&
                !string.IsNullOrEmpty(a.ResultContext.ChosenText))
            {
                var yc = UiKit.Text("Ctx", scroll, $"<color=#BF4038>你的作答：{a.ResultContext.ChosenText}</color>",
                    30, TextAlignmentOptions.Center, Design.OnSurfaceVariant);
                UiKit.MinHeight(yc.gameObject, 56);
            }

            BuildDifficultyEditor(scroll, poem);
            AddSection(scroll, "译文", poem.Translation);
            AddSection(scroll, "赏析", poem.Appreciation);

            BuildFooter(body, poem, a);

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

        private void BuildPoemBody(Transform scroll, Poem poem, PoemDetailArgs a)
        {
            // 全局「按组换行」：开 → 同句号组的诗句并排一行；关 → 每句各占一行（默认竖排阅读）。
            bool grouped = Services.Settings?.Current != null && Services.Settings.Current.GroupedLineBreak;

            // 高亮出题诗句：从结果上下文（答题/逐字填空/滑动找诗）取被考的句子，绿=正确、红=错误；
            // 从收藏夹等无上下文打开时不高亮（默认）。
            HashSet<string> highlight = null;
            string hiHex = null;
            if (a?.ResultContext != null)
            {
                highlight = new HashSet<string>();
                if (!string.IsNullOrEmpty(a.ResultContext.CorrectText))
                    foreach (var seg in a.ResultContext.CorrectText.Split(' '))
                        if (!string.IsNullOrEmpty(seg)) highlight.Add(seg);
                hiHex = ColorUtility.ToHtmlStringRGB(a.ResultContext.IsCorrect ? UiKit.Good : UiKit.Bad);
            }

            var sb = new StringBuilder();
            int prevGroup = int.MinValue;
            for (int i = 0; i < poem.Lines.Count; i++)
            {
                var line = poem.Lines[i];
                if (sb.Length > 0)
                    sb.Append(grouped && line.Group >= 0 && line.Group == prevGroup ? "  " : "\n");
                if (highlight != null && highlight.Contains(line.Text))
                    sb.Append($"<color=#{hiHex}>{line.Text}</color>");
                else
                    sb.Append(line.Text);
                prevGroup = line.Group;
            }
            string body = sb.ToString();

            var box = UiKit.Panel("PoemBox", scroll);
            var poemText = UiKit.Text("Poem", box.transform, body, 48, TextAlignmentOptions.Center, Design.Ink,
                wrap: grouped); // grouped rows can be long → allow wrap
            poemText.lineSpacing = 18f; poemText.characterSpacing = 6f;
            UiKit.StretchFull(poemText.gameObject, 36);
            // Estimate height: unwrapped lines need no width bound; grouped rows can wrap, so bound to
            // an approximate content width (canvas 1080 minus side margins) and add slack.
            float poemH = poemText.GetPreferredValues(body, grouped ? 900f : 100000f, 0f).y + 100f;
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

        private void BuildFooter(Transform body, Poem poem, PoemDetailArgs a)
        {
            string poemId = poem.Id;
            var footer = UiKit.Panel("Footer", body);
            UiKit.Pref(footer, minH: 116).flexibleHeight = 0f;
            UiKit.HorizontalGroup(footer, spacing: 12);

            bool hasNav = a != null && a.Siblings != null && a.Siblings.Count > 1;
            int n = hasNav ? a.Siblings.Count : 0;

            // 上一首 / 收藏 / 下一首 同列一行（收藏在中间、稍宽）。
            if (hasNav)
            {
                var prev = UiKit.Button("Prev", footer.transform, "上一首", out var pl, Design.SurfaceHigh, 30);
                pl.color = Design.Ink;
                prev.gameObject.AddComponent<LayoutElement>().flexibleWidth = 0.8f;
                prev.onClick.AddListener(() => Open(a.Siblings, (a.Index - 1 + n) % n, -1));
            }

            var favBtn = UiKit.Button("Fav", footer.transform, "收藏作品", out var favLbl, Design.SecondaryFixed, 36);
            favLbl.color = Design.Primary;
            if (hasNav) favBtn.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1.4f;
            SetFav(favLbl, poemId);
            favBtn.onClick.AddListener(async () =>
            {
                bool on = await Services.Favorites.ToggleAsync(poemId);
                ApplyFav(favLbl, on);
            });

            if (hasNav)
            {
                var next = UiKit.Button("Next", footer.transform, "下一首", out var nl, Design.SurfaceHigh, 30);
                nl.color = Design.Ink;
                next.gameObject.AddComponent<LayoutElement>().flexibleWidth = 0.8f;
                next.onClick.AddListener(() => Open(a.Siblings, (a.Index + 1) % n, +1));
            }
        }

        private void Open(List<string> siblings, int index, int dir)
        {
            // Carry the parallel per-sibling result so the swiped-to poem keeps highlighting its 出题句.
            var ctx = (_siblingResults != null && index >= 0 && index < _siblingResults.Count)
                ? _siblingResults[index] : null;
            Nav.Replace<PoemDetailScreen>(new PoemDetailArgs
            {
                PoemId = siblings[index], Siblings = siblings, Index = index, SlideFrom = dir,
                SiblingResults = _siblingResults, ResultContext = ctx,
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
