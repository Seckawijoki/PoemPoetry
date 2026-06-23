using System.Collections.Generic;
using PoemPoetry.Data;
using TMPro;
using UnityEngine;

namespace PoemPoetry.UI
{
    public sealed class ResultScreen : UIScreen
    {
        protected override void OnShow(object args)
        {
            var record = (args as ResultArgs)?.Record;
            // Back returns to the answer config page (答题主页面), not all the way home.
            var body = UiKit.ScreenRoot(gameObject, "挑战结果", () => Nav.Pop());
            UiKit.VerticalGroup(body.gameObject, spacing: 14, padX: 28, padY: 12, align: TextAnchor.UpperCenter);

            if (record == null)
            {
                UiKit.Text("E", body, "无结果数据", 40, TextAlignmentOptions.Center, UiKit.Muted);
                return;
            }

            var acc = UiKit.Text("Acc", body, $"正确率 {record.AccuracyPercent}%", 76, TextAlignmentOptions.Center, UiKit.Accent);
            UiKit.MinHeight(acc.gameObject, 120);
            var sub = UiKit.Text("Sub", body,
                $"答对 {record.CorrectCount}/{record.Total}    用时 {Fmt(record.DurationSeconds)}    最高连胜 {record.BestStreak}",
                30, TextAlignmentOptions.Center, UiKit.Muted);
            UiKit.MinHeight(sub.gameObject, 48);
            var seal = UiKit.Text("Seal", body, Grade(record.AccuracyPercent), 40, TextAlignmentOptions.Center, UiKit.Ink);
            UiKit.MinHeight(seal.gameObject, 58);

            var revTitle = UiKit.Text("RevTitle", body, "逐题回顾（点击查看 · 收藏）", 28, TextAlignmentOptions.Center, UiKit.Muted);
            UiKit.MinHeight(revTitle.gameObject, 46);

            var content = UiKit.ScrollList("Review", body, out _);
            var sib = new List<string>();
            foreach (var it in record.Items) sib.Add(it.PoemId);
            for (int i = 0; i < record.Items.Count; i++)
                ReviewRow.Build(content, Nav, record.Items[i], sib, i);

            var btnRow = UiKit.Panel("Btns", body);
            var btnRowLe = UiKit.Pref(btnRow, minH: 120);
            btnRowLe.flexibleHeight = 0f; // don't let the row absorb the leftover space
            var hg = UiKit.HorizontalGroup(btnRow, spacing: 16);
            hg.childForceExpandHeight = false;
            var again = UiKit.Button("Again", btnRow.transform, "再来一组", out var againLbl, UiKit.Accent, 38);
            UiKit.Pref(again.gameObject, minH: 110);
            againLbl.color = Color.white;
            var s = record.Settings ?? new ChallengeSettings();
            again.onClick.AddListener(() => Nav.Replace<QuizScreen>(new QuizStartArgs
            {
                QuestionCount = s.QuestionCount,
                Difficulties = s.Difficulties ?? new System.Collections.Generic.List<int>(),
                Dynasties = s.Dynasties ?? new System.Collections.Generic.List<string>(),
                Mode = "challenge",
            }));
            var home = UiKit.Button("Home", btnRow.transform, "返回", out _, UiKit.CardAlt, 38);
            UiKit.Pref(home.gameObject, minH: 110);
            home.onClick.AddListener(() => Nav.Pop());
        }

        private static string Grade(int acc) =>
            acc >= 90 ? "优秀" : acc >= 70 ? "良好" : acc >= 50 ? "继续加油" : "再接再厉";

        private static string Fmt(int seconds)
        {
            int m = seconds / 60, s = seconds % 60;
            return $"{m}:{s:00}";
        }
    }
}
