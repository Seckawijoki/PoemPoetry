using System.Collections.Generic;
using PoemPoetry.Core;
using PoemPoetry.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoemPoetry.UI
{
    /// <summary>
    /// One per-question review row (mark + poem + answer) with an inline favorite toggle and
    /// tap-to-open-poem. Shared by the result screen and the record-detail screen.
    /// </summary>
    public static class ReviewRow
    {
        public static void Build(Transform parent, ScreenNavigator nav, QuestionResult item,
            List<string> siblings = null, int index = 0)
        {
            var services = nav.Services;
            var poem = services.Content.GetPoem(item.PoemId);
            string title = poem != null ? poem.Title : "";
            string author = poem != null ? poem.Author : "";

            var row = UiKit.Panel("Row", parent, UiKit.Card);
            UiKit.Pref(row, minH: 160);
            var h = UiKit.HorizontalGroup(row, spacing: 8, pad: 14, align: TextAnchor.MiddleLeft);
            h.childForceExpandWidth = false;

            // Tappable text region -> poem detail.
            var openGo = new GameObject("Open", typeof(RectTransform), typeof(Image), typeof(Button));
            openGo.transform.SetParent(row.transform, false);
            openGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var openLe = UiKit.Pref(openGo, minH: 132);
            openLe.flexibleWidth = 1f;

            string mark = item.IsCorrect ? "<color=#2E8B47>对</color>" : "<color=#BF4038>错</color>";
            string txt = $"[{mark}] 《{title}》 {author}\n{item.CorrectText}";
            if (!item.IsCorrect)
                txt += $"\n<size=70%><color=#9A938C>你选：{item.ChosenText}</color></size>";
            var label = UiKit.Text("T", openGo.transform, txt, 32, TextAlignmentOptions.Left, UiKit.Ink);
            UiKit.StretchFull(label.gameObject, 10);

            string pid = item.PoemId;
            var ctx = item;
            openGo.GetComponent<Button>().onClick.AddListener(
                () => nav.Push<PoemDetailScreen>(new PoemDetailArgs
                {
                    PoemId = pid, ResultContext = ctx, Siblings = siblings, Index = index,
                }));

            // Favorite toggle.
            var favBtn = UiKit.Button("Fav", row.transform, "收藏", out var favLbl, UiKit.CardAlt, 30);
            var favLe = UiKit.Pref(favBtn.gameObject, minW: 170, minH: 120);
            favLe.flexibleWidth = 0f;
            SetFavVisual(services, favLbl, pid);
            favBtn.onClick.AddListener(async () =>
            {
                bool on = await services.Favorites.ToggleAsync(pid);
                ApplyFav(favLbl, on);
            });
        }

        private static async void SetFavVisual(AppServices services, TextMeshProUGUI lbl, string poemId)
        {
            bool fav = await services.Favorites.IsFavoriteAsync(poemId);
            ApplyFav(lbl, fav);
        }

        private static void ApplyFav(TextMeshProUGUI lbl, bool on)
        {
            lbl.text = on ? "已收藏" : "收藏";
            lbl.color = on ? UiKit.Accent : UiKit.Ink;
        }
    }
}
