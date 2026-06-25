using System.Collections.Generic;
using PoemPoetry.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoemPoetry.UI
{
    /// <summary>错题本: 开始复习 CTA + spaced-repetition cards (盒/已复习), per designs/stitch/错题集.</summary>
    public sealed class WrongBookScreen : UIScreen
    {
        private RectTransform _content;
        private TextMeshProUGUI _reviewLabel;

        protected override void OnShow(object args)
        {
            var body = Design.Chrome(gameObject, () => Nav.Pop(), () => Nav.Push<SettingsScreen>(), "错题本");
            UiKit.VerticalGroup(body.gameObject, spacing: 10, padX: 28, padY: 16, align: TextAnchor.UpperCenter);

            var review = Design.PrimaryButton("Review", body, "开始复习（到期错题）", out _reviewLabel, 36);
            UiKit.Pref(review.gameObject, minH: 110).flexibleHeight = 0f;
            review.onClick.AddListener(StartReview);

            var sub = UiKit.Text("Sub", body, "根据艾宾浩斯遗忘曲线提醒复习", 24, TextAlignmentOptions.Center,
                Design.Alpha(Design.OnSurfaceVariant, 0.7f));
            UiKit.MinHeight(sub.gameObject, 44);

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
                UiKit.Text("Empty", _content, "还没有错题，继续保持！", 32, TextAlignmentOptions.Center, Design.OnSurfaceVariant);
                return;
            }
            foreach (var e in all) BuildCard(e);
        }

        private void BuildCard(WrongBookEntry e)
        {
            var poem = Services.Content.GetPoem(e.PoemId);
            var pid = e.PoemId;
            var card = Design.CardButton("WB", _content, out var btn, 222);
            Design.Corners(card, Design.Alpha(Design.Outline, 0.3f), arm: 24, thick: 1, inset: 10);

            string title = poem != null ? $"《{poem.Title}》" : e.QuestionId;
            Design.CardText("Title", card.transform, $"<b>{title}</b>", 30, Design.Primary, TextAlignmentOptions.Left,
                new Vector2(0, 1), new Vector2(28, -26), new Vector2(560, 44));
            if (poem != null)
                Design.CardText("Author", card.transform, poem.Author, 26, Design.OnSurfaceVariant,
                    TextAlignmentOptions.Right, new Vector2(1, 1), new Vector2(-28, -30), new Vector2(280, 40));

            string firstLine = poem != null ? poem.FirstLineText : "";
            Design.CardText("Line", card.transform, $"<i>{firstLine}</i>", 28, Design.Ink, TextAlignmentOptions.Left,
                new Vector2(0, 1), new Vector2(28, -98), new Vector2(640, 46));

            Design.CardText("Box", card.transform, $"盒 {e.Box}/3　·　已复习 {e.ReviewCount} 次", 24, Design.OnSurfaceVariant,
                TextAlignmentOptions.Left, new Vector2(0, 0), new Vector2(28, 22), new Vector2(560, 36));
            Design.CardText("Chev", card.transform, "▸", 30, Design.Alpha(Design.Outline, 0.6f),
                TextAlignmentOptions.Right, new Vector2(1, 0), new Vector2(-28, 22), new Vector2(60, 36));

            btn.onClick.AddListener(() => Nav.Push<PoemDetailScreen>(new PoemDetailArgs { PoemId = pid }));
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
