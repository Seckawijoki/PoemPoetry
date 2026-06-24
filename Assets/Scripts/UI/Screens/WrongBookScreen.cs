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
            // 逐词填空 (wc-) and line-quiz (q-) errors use different screens; review one kind per session.
            var quizIds = new List<string>();
            var clozeIds = new List<string>();
            foreach (var e in due)
            {
                if (e.QuestionId != null && e.QuestionId.StartsWith("wc-")) clozeIds.Add(e.QuestionId);
                else quizIds.Add(e.QuestionId);
            }
            if (quizIds.Count > 0)
                Nav.Push<QuizScreen>(new QuizStartArgs { Mode = "wrongbook", QuestionCount = quizIds.Count, QuestionIds = quizIds });
            else if (clozeIds.Count > 0)
                Nav.Push<WordClozeScreen>(new WordClozeStartArgs { Mode = "wrongbook", QuestionCount = clozeIds.Count, QuestionIds = clozeIds });
        }
    }
}
