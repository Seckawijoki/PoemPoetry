using System.Collections.Generic;
using System.Text;
using PoemPoetry.Core;
using PoemPoetry.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoemPoetry.UI
{
    /// <summary>
    /// One per-question review card (status dot · 《题》·作者 · the line in cinnabar · 你选 note) with
    /// a favorite toggle and tap-to-open-poem, styled per designs/stitch/答题结束. Shared by the
    /// result screen and the record-detail screen.
    /// </summary>
    public static class ReviewRow
    {
        // showChosen: render the "你选" note for wrong answers. 滑动找诗 has no per-question choice
        // (a line is either traced out or not), so its review rows pass false to hide it.
        public static void Build(Transform parent, ScreenNavigator nav, QuestionResult item,
            List<string> siblings = null, int index = 0, bool showChosen = true)
        {
            var services = nav.Services;
            var poem = services.Content.GetPoem(item.PoemId);
            string title = poem != null ? poem.Title : "";
            string author = poem != null ? poem.Author : "";

            // Card: soft parchment container, content left + favorite toggle right (top-aligned).
            var card = UiKit.Panel("Row", parent, Design.SurfaceContainer);
            var hlg = UiKit.HorizontalGroup(card, spacing: 10, pad: 0, align: TextAnchor.UpperLeft);
            hlg.padding = new RectOffset(22, 16, 18, 18);
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

            // Tappable text column -> poem detail.
            var openGo = new GameObject("Open", typeof(RectTransform), typeof(Image), typeof(Button));
            openGo.transform.SetParent(card.transform, false);
            openGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var openLe = openGo.AddComponent<LayoutElement>(); openLe.flexibleWidth = 1f;
            var vg = UiKit.VerticalGroup(openGo, spacing: 8, padX: 0, padY: 0, align: TextAnchor.UpperLeft);
            vg.childForceExpandHeight = false; vg.childControlHeight = true;

            // Header: status dot + 《题》 · 作者.
            var hdr = UiKit.Panel("Hdr", openGo.transform);
            var hhg = UiKit.HorizontalGroup(hdr, spacing: 12, pad: 0, align: TextAnchor.MiddleLeft);
            hhg.childForceExpandWidth = false; hhg.childForceExpandHeight = false;
            var dot = UiKit.Panel("Dot", hdr.transform, item.IsCorrect ? UiKit.Good : UiKit.Bad);
            dot.GetComponent<Image>().raycastTarget = false;
            var dle = UiKit.Pref(dot, minW: 18, minH: 18); dle.flexibleWidth = 0f; dle.flexibleHeight = 0f;
            string authorHex = ColorUtility.ToHtmlStringRGB(Design.OnSurfaceVariant);
            var titleTxt = UiKit.Text("Title", hdr.transform,
                $"<b>《{title}》</b>  <size=80%><color=#{authorHex}>· {author}</color></size>",
                30, TextAlignmentOptions.Left, Design.Ink, wrap: true);
            // flexible width only — let TMP report its own (possibly wrapped) height.
            titleTxt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // The tested line(s), cinnabar italic. 逐字填空 spans several lines/groups, so break
            // rows at 句号 group boundaries (same group joined by space, new group on a new row).
            UiKit.Text("Line", openGo.transform, $"<i>{GroupedVerse(poem, item.CorrectText)}</i>", 32,
                TextAlignmentOptions.Left, Design.Primary, wrap: true);

            if (!item.IsCorrect && showChosen)
                UiKit.Text("Note", openGo.transform, $"你选：{item.ChosenText}", 24,
                    TextAlignmentOptions.Left, Design.Alpha(Design.OnSurfaceVariant, 0.75f), wrap: true);

            string pid = item.PoemId;
            var ctx = item;
            openGo.GetComponent<Button>().onClick.AddListener(
                () => nav.Push<PoemDetailScreen>(new PoemDetailArgs
                {
                    PoemId = pid, ResultContext = ctx, Siblings = siblings, Index = index,
                }));

            // Favorite toggle (top-right), borderless like the design's bookmark.
            var favBtn = new GameObject("Fav", typeof(RectTransform), typeof(Image), typeof(Button));
            favBtn.transform.SetParent(card.transform, false);
            favBtn.GetComponent<Image>().color = Design.Alpha(Design.SecondaryFixed, 0.6f);
            var favLe = favBtn.AddComponent<LayoutElement>();
            favLe.minWidth = 130; favLe.preferredWidth = 130; favLe.minHeight = 92; favLe.flexibleWidth = 0f;
            var favLbl = UiKit.Text("L", favBtn.transform, "收藏", 28, TextAlignmentOptions.Center, Design.Ink);
            UiKit.StretchFull(favLbl.gameObject, 6);
            SetFavVisual(services, favLbl, pid);
            favBtn.GetComponent<Button>().onClick.AddListener(async () =>
            {
                bool on = await services.Favorites.ToggleAsync(pid);
                ApplyFav(favLbl, on);
            });
        }

        // CorrectText is one or more poem lines joined by a space (逐字填空 shows several). Re-break it
        // into rows by 句号 group: consecutive lines in the same group share a row; a new group starts
        // a new row. Falls back to the raw text when the poem/lines can't be resolved.
        private static string GroupedVerse(Poem poem, string correctText)
        {
            if (string.IsNullOrEmpty(correctText) || poem == null || poem.Lines == null)
                return correctText;
            var segments = correctText.Split(' ');
            if (segments.Length <= 1) return correctText; // single line → nothing to regroup

            var sb = new StringBuilder();
            int prevGroup = int.MinValue;
            int searchFrom = 0;
            foreach (var seg in segments)
            {
                if (string.IsNullOrEmpty(seg)) continue;
                int group = GroupOf(poem, seg, ref searchFrom);
                bool sameGroup = group >= 0 && group == prevGroup;
                if (sb.Length > 0) sb.Append(sameGroup ? " " : "\n");
                sb.Append(seg);
                prevGroup = group;
            }
            return sb.ToString();
        }

        // Find the group of the poem line matching this text, scanning forward (to keep order /
        // tolerate repeated texts) then wrapping. -1 (unset) keeps the segment on its own row.
        private static int GroupOf(Poem poem, string text, ref int from)
        {
            for (int i = from; i < poem.Lines.Count; i++)
                if (poem.Lines[i].Text == text) { from = i + 1; return poem.Lines[i].Group; }
            for (int i = 0; i < poem.Lines.Count; i++)
                if (poem.Lines[i].Text == text) return poem.Lines[i].Group;
            return int.MinValue; // unknown → its own row
        }

        private static async void SetFavVisual(AppServices services, TextMeshProUGUI lbl, string poemId)
        {
            bool fav = await services.Favorites.IsFavoriteAsync(poemId);
            ApplyFav(lbl, fav);
        }

        private static void ApplyFav(TextMeshProUGUI lbl, bool on)
        {
            lbl.text = on ? "已收藏" : "收藏";
            lbl.color = on ? Design.Primary : Design.Ink;
        }
    }
}
