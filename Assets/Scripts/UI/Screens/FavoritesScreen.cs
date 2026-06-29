using System.Collections.Generic;
using PoemPoetry.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoemPoetry.UI
{
    /// <summary>收藏夹: cards of favorited poems (《题》·作者 · 首句 · 书签), per designs/stitch/收藏夹.</summary>
    public sealed class FavoritesScreen : UIScreen
    {
        private RectTransform _content;

        protected override void OnShow(object args)
        {
            var body = Design.Chrome(gameObject, () => Nav.Pop(), () => Nav.Push<SettingsScreen>(), "收藏夹");
            UiKit.VerticalGroup(body.gameObject, spacing: 12, padX: 28, padY: 16, align: TextAnchor.UpperCenter);
            _content = UiKit.ScrollList("List", body, out _);
            Refresh();
        }

        protected override void OnFocus() => Refresh();

        private async void Refresh()
        {
            if (_content == null) return;
            UiKit.ClearChildren(_content);
            var favs = await Services.Favorites.GetAllAsync();

            var ids = new List<string>();
            foreach (var f in favs)
                if (Services.Content.GetPoem(f.PoemId) != null) ids.Add(f.PoemId);

            if (ids.Count == 0)
            {
                UiKit.Text("Empty", _content, "还没有收藏的诗词\n答题回顾或诗词页点 收藏 即可加入", 30,
                    TextAlignmentOptions.Center, Design.OnSurfaceVariant);
                return;
            }

            var sub = UiKit.Text("Sub", _content, $"— 共收藏 {ids.Count} 首名篇 —", 26, TextAlignmentOptions.Center,
                Design.Alpha(Design.OnSurfaceVariant, 0.7f));
            UiKit.MinHeight(sub.gameObject, 50);

            string mutedHex = ColorUtility.ToHtmlStringRGB(Design.OnSurfaceVariant);
            for (int i = 0; i < ids.Count; i++)
            {
                int idx = i;
                var pid = ids[i];
                var poem = Services.Content.GetPoem(pid);
                var card = Design.CardButton("Fav", _content, out var btn, 150);
                Design.Corners(card, Design.Alpha(Design.Outline, 0.3f), arm: 24, thick: 1, inset: 10);

                Design.CardText("Title", card.transform,
                    $"<b>《{PoemFormat.DisplayTitle(poem.Title)}》</b>　<size=72%><color=#{mutedHex}>{poem.Dynasty}·{poem.Author}</color></size>",
                    32, Design.Primary, TextAlignmentOptions.Left,
                    new Vector2(0, 1), new Vector2(30, -28), new Vector2(600, 44));

                string preview = (poem.Type == "词" && !string.IsNullOrEmpty(poem.Cipai))
                    ? $"词牌 · {poem.Cipai}"
                    : $"「{poem.FirstLineText}」";
                Design.CardText("Line", card.transform, preview, 28, Design.Alpha(Design.OnSurfaceVariant, 0.9f),
                    TextAlignmentOptions.Left, new Vector2(0, 1), new Vector2(30, -86), new Vector2(640, 44));

                // Filled bookmark marker (all entries are favorites).
                var mark = UiKit.Panel("Mark", card.transform, Design.Primary);
                mark.GetComponent<Image>().raycastTarget = false;
                var mrt = UiKit.Rect(mark);
                mrt.anchorMin = mrt.anchorMax = new Vector2(1, 0.5f); mrt.pivot = new Vector2(1, 0.5f);
                mrt.sizeDelta = new Vector2(22, 40); mrt.anchoredPosition = new Vector2(-34, 0);

                btn.onClick.AddListener(() => Nav.Push<PoemDetailScreen>(
                    new PoemDetailArgs { PoemId = pid, Siblings = ids, Index = idx }));
            }
        }
    }
}
