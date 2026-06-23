using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PoemPoetry.UI
{
    public sealed class FavoritesScreen : UIScreen
    {
        private RectTransform _content;

        protected override void OnShow(object args)
        {
            var body = UiKit.ScreenRoot(gameObject, "收藏夹", () => Nav.Pop());
            UiKit.VerticalGroup(body.gameObject, spacing: 12, padX: 24, padY: 12, align: TextAnchor.UpperCenter);
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
                UiKit.Text("Empty", _content, "还没有收藏的诗词\n答题回顾或诗词页点 收藏 即可加入", 30, TextAlignmentOptions.Center, UiKit.Muted);
                return;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                int idx = i;
                var pid = ids[i];
                var poem = Services.Content.GetPoem(pid);
                var btn = UiKit.Button("Fav", _content, "", out var lbl, UiKit.Card, 30);
                UiKit.Pref(btn.gameObject, minH: 140);
                lbl.alignment = TextAlignmentOptions.Left;
                // 词 with no question shows its 词牌 rather than the first line.
                string preview = (poem.Type == "词" && !string.IsNullOrEmpty(poem.Cipai))
                    ? "词牌 · " + poem.Cipai
                    : poem.FirstLineText + "...";
                lbl.text = $"《{poem.Title}》  {poem.Dynasty}·{poem.Author}\n<size=85%><color=#9A938C>{preview}</color></size>";
                btn.onClick.AddListener(() => Nav.Push<PoemDetailScreen>(
                    new PoemDetailArgs { PoemId = pid, Siblings = ids, Index = idx }));
            }
        }
    }
}
