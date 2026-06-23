using System.Collections.Generic;
using System.Text;
using PoemPoetry.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoemPoetry.UI
{
    public sealed class PoemDetailScreen : UIScreen
    {
        protected override void OnShow(object args)
        {
            var a = args as PoemDetailArgs;
            var poem = a != null ? Services.Content.GetPoem(a.PoemId) : null;
            var body = UiKit.ScreenRoot(gameObject, poem != null ? poem.Title : "诗词", () => Nav.Pop());
            UiKit.VerticalGroup(body.gameObject, spacing: 14, padX: 28, padY: 12, align: TextAnchor.UpperCenter);

            if (poem == null)
            {
                UiKit.Text("E", body, "诗词不存在", 36, TextAlignmentOptions.Center, UiKit.Muted);
                return;
            }

            var metaStr = $"{poem.Dynasty} · {poem.Author}" + (string.IsNullOrEmpty(poem.Cipai) ? "" : $" · {poem.Cipai}");
            var meta = UiKit.Text("Meta", body, metaStr, 32, TextAlignmentOptions.Center, UiKit.Muted);
            UiKit.MinHeight(meta.gameObject, 50);

            // Prev/next navigation across the list this poem was opened from.
            if (a != null && a.Siblings != null && a.Siblings.Count > 1)
            {
                int n = a.Siblings.Count;
                var navRow = UiKit.Panel("Nav", body);
                UiKit.Pref(navRow, minH: 78).flexibleHeight = 0f;
                var navHg = UiKit.HorizontalGroup(navRow, spacing: 12);
                navHg.childForceExpandHeight = false;
                var prev = UiKit.Button("Prev", navRow.transform, "上一首", out _, UiKit.CardAlt, 30);
                UiKit.Pref(prev.gameObject, minH: 78);
                prev.onClick.AddListener(() => Open(a.Siblings, (a.Index - 1 + n) % n));
                var pos = UiKit.Text("Pos", navRow.transform, $"{a.Index + 1}/{n}", 28, TextAlignmentOptions.Center, UiKit.Muted);
                pos.raycastTarget = false;
                var next = UiKit.Button("Next", navRow.transform, "下一首", out _, UiKit.CardAlt, 30);
                UiKit.Pref(next.gameObject, minH: 78);
                next.onClick.AddListener(() => Open(a.Siblings, (a.Index + 1) % n));
            }

            var favBtn = UiKit.Button("Fav", body, "收藏", out var favLbl, UiKit.CardAlt, 32);
            UiKit.Pref(favBtn.gameObject, minH: 70);
            SetFav(favLbl, poem.Id);
            favBtn.onClick.AddListener(async () =>
            {
                bool on = await Services.Favorites.ToggleAsync(poem.Id);
                ApplyFav(favLbl, on);
            });

            // Difficulty editor (radio): click a tier to set this poem's difficulty (saved locally).
            UiKit.Text("DiffTitle", body, "难度（点选可改，本地生效）", 24, TextAlignmentOptions.Center, UiKit.Muted)
                .gameObject.AddComponent<LayoutElement>().minHeight = 34;
            var diffRow = UiKit.Panel("DiffRow", body);
            UiKit.Pref(diffRow, minH: 70).flexibleHeight = 0f;
            var diffHg = UiKit.HorizontalGroup(diffRow, spacing: 10);
            diffHg.childForceExpandHeight = false;
            int[] tiers = { 0, 1, 2, 4 };
            var diffBtns = new List<Button>();
            var diffLbls = new List<TextMeshProUGUI>();
            foreach (var t in tiers)
            {
                int tier = t;
                var b = UiKit.Button("D" + t, diffRow.transform, QuizConfigScreen.TierLabel(t), out var lbl, UiKit.Card, 28);
                UiKit.Pref(b.gameObject, minH: 70);
                diffBtns.Add(b);
                diffLbls.Add(lbl);
                b.onClick.AddListener(async () =>
                {
                    await Services.Difficulty.SetAsync(poem.Id, tier);
                    for (int i = 0; i < tiers.Length; i++)
                        UiKit.SetChipSelected(diffBtns[i], diffLbls[i], tiers[i] == tier);
                });
            }
            for (int i = 0; i < tiers.Length; i++)
                UiKit.SetChipSelected(diffBtns[i], diffLbls[i], tiers[i] == poem.Difficulty);

            var scroll = UiKit.ScrollList("Body", body, out _);

            var sb = new StringBuilder();
            foreach (var line in poem.Lines) sb.Append(line.Text).Append('\n');
            var poemText = UiKit.Text("Poem", scroll, sb.ToString().TrimEnd(), 46, TextAlignmentOptions.Center, UiKit.Ink);
            UiKit.Pref(poemText.gameObject, minH: 64 * Mathf.Max(1, poem.Lines.Count));

            if (a?.ResultContext != null && !a.ResultContext.IsCorrect)
            {
                var yc = UiKit.Text("Ctx", scroll, $"<color=#BF4038>你的作答：{a.ResultContext.ChosenText}</color>",
                    30, TextAlignmentOptions.Center, UiKit.Muted);
                UiKit.MinHeight(yc.gameObject, 50);
            }

            AddSection(scroll, "译文", poem.Translation);
            AddSection(scroll, "赏析", poem.Appreciation);
        }

        private void Open(List<string> siblings, int index)
        {
            Nav.Replace<PoemDetailScreen>(new PoemDetailArgs
            {
                PoemId = siblings[index], Siblings = siblings, Index = index,
            });
        }

        private static void AddSection(Transform scroll, string title, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var t = UiKit.Text(title, scroll, title, 30, TextAlignmentOptions.Center, UiKit.Accent);
            UiKit.MinHeight(t.gameObject, 52);
            var body = UiKit.Text(title + "B", scroll, text, 30, TextAlignmentOptions.Left, UiKit.Ink, wrap: true);
            UiKit.Pref(body.gameObject, minH: 110);
        }

        private async void SetFav(TextMeshProUGUI lbl, string poemId)
        {
            bool fav = await Services.Favorites.IsFavoriteAsync(poemId);
            ApplyFav(lbl, fav);
        }

        private static void ApplyFav(TextMeshProUGUI lbl, bool on)
        {
            lbl.text = on ? "已收藏" : "收藏";
            lbl.color = on ? UiKit.Accent : UiKit.Ink;
        }
    }
}
