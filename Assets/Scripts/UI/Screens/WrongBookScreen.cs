using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PoemPoetry.UI
{
    public sealed class WrongBookScreen : UIScreen
    {
        private RectTransform _content;
        private TextMeshProUGUI _reviewLabel;

        protected override void OnShow(object args)
        {
            var body = UiKit.ScreenRoot(gameObject, "错题本", () => Nav.Pop());
            UiKit.VerticalGroup(body.gameObject, spacing: 12, padX: 24, padY: 12, align: TextAnchor.UpperCenter);

            var review = UiKit.Button("Review", body, "开始复习（到期错题）", out _reviewLabel, UiKit.Accent, 36);
            _reviewLabel.color = Color.white;
            UiKit.Pref(review.gameObject, minH: 100);
            review.onClick.AddListener(StartReview);

            _content = UiKit.ScrollList("List", body, out _);
            Refresh();
        }

        protected override void OnFocus() => Refresh();

        private async void Refresh()
        {
            if (_content == null) return;
            UiKit.ClearChildren(_content);
            int due = await Services.WrongBook.GetDueCountAsync();
            _reviewLabel.text = due > 0 ? $"开始复习（{due} 题到期）" : "暂无到期错题";

            var all = await Services.WrongBook.GetAllAsync();
            if (all.Count == 0)
            {
                UiKit.Text("Empty", _content, "还没有错题，继续保持！", 32, TextAlignmentOptions.Center, UiKit.Muted);
                return;
            }
            foreach (var e in all)
            {
                var poem = Services.Content.GetPoem(e.PoemId);
                var pid = e.PoemId;
                var btn = UiKit.Button("WB", _content, "", out var lbl, UiKit.Card, 30);
                UiKit.Pref(btn.gameObject, minH: 130);
                lbl.alignment = TextAlignmentOptions.Left;
                lbl.text = poem != null
                    ? $"《{poem.Title}》 {poem.Author}\n<size=80%><color=#9A938C>盒 {e.Box}/3 · 已复习 {e.ReviewCount} 次</color></size>"
                    : e.QuestionId;
                btn.onClick.AddListener(() => Nav.Push<PoemDetailScreen>(new PoemDetailArgs { PoemId = pid }));
            }
        }

        private async void StartReview()
        {
            var due = await Services.WrongBook.GetDueAsync();
            var ids = new List<string>();
            foreach (var e in due) ids.Add(e.QuestionId);
            if (ids.Count == 0) return;
            Nav.Push<QuizScreen>(new QuizStartArgs { Mode = "wrongbook", QuestionCount = ids.Count, QuestionIds = ids });
        }
    }
}
