using System.Collections.Generic;
using PoemPoetry.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoemPoetry.UI
{
    /// <summary>答题结束 (测评结果): score hero + 用时/连胜/答对 stats + 逐题回顾, per designs/stitch/答题结束.</summary>
    public sealed class ResultScreen : UIScreen
    {
        protected override void OnShow(object args)
        {
            var record = (args as ResultArgs)?.Record;
            // Back returns to the answer config page (答题主页面), not all the way home.
            var body = Design.Chrome(gameObject, () => Nav.Pop(), () => Nav.Push<SettingsScreen>(), "答题结果");
            UiKit.VerticalGroup(body.gameObject, spacing: 12, padX: 28, padY: 16, align: TextAnchor.UpperCenter);

            if (record == null)
            {
                UiKit.Text("E", body, "无结果数据", 40, TextAlignmentOptions.Center, Design.OnSurfaceVariant);
                return;
            }

            // Everything (hero · stats · review · actions) flows in one scroll so short results
            // don't leave a big empty band, and long reviews scroll naturally.
            var content = UiKit.ScrollList("Result", body, out _);

            var acc = UiKit.Text("Acc", content, $"{record.AccuracyPercent}%", 96, TextAlignmentOptions.Center, Design.PrimaryContainer);
            UiKit.MinHeight(acc.gameObject, 124); acc.characterSpacing = -2f;
            var seal = UiKit.Text("Seal", content, Grade(record.AccuracyPercent), 44, TextAlignmentOptions.Center, Design.Primary);
            UiKit.MinHeight(seal.gameObject, 60);
            Spacer(content, 10);

            BuildStats(content, record);
            Spacer(content, 18);

            // Review header (compact single line).
            var head = UiKit.Panel("RevHead", content);
            UiKit.Pref(head, minH: 46);
            var hg = UiKit.HorizontalGroup(head, spacing: 8, pad: 0, align: TextAnchor.MiddleLeft);
            hg.childForceExpandWidth = false; hg.childForceExpandHeight = false;
            var ht = UiKit.Text("HT", head.transform, "题目回顾", 30, TextAlignmentOptions.Left, Design.Ink);
            UiKit.Pref(ht.gameObject, minH: 40).flexibleWidth = 1f;
            var hc = UiKit.Text("HC", head.transform, $"已答 {record.Total} 题", 24, TextAlignmentOptions.Right, Design.OnSurfaceVariant);
            UiKit.Pref(hc.gameObject, minH: 40).flexibleWidth = 0f;

            var sib = new List<string>();
            foreach (var it in record.Items) sib.Add(it.PoemId);
            for (int i = 0; i < record.Items.Count; i++)
                ReviewRow.Build(content, Nav, record.Items[i], sib, i, true, record.Items);

            // Fixed footer: two side-by-side actions, always pinned below the scrolling review.
            var footer = UiKit.Panel("Footer", body);
            UiKit.Pref(footer, minH: 124).flexibleHeight = 0f;
            var fg = UiKit.HorizontalGroup(footer, spacing: 16, pad: 0);
            fg.childForceExpandHeight = true;

            var again = Design.PrimaryButton("Again", footer.transform, "再来一局", out _, 38);
            var s = record.Settings ?? new ChallengeSettings();
            if (record.Mode == "wordcloze")
                again.onClick.AddListener(() => Nav.Replace<WordClozeScreen>(new WordClozeStartArgs
                {
                    QuestionCount = s.QuestionCount,
                    Difficulties = s.Difficulties ?? new List<int>(),
                    Dynasties = s.Dynasties ?? new List<string>(),
                    Types = s.Types ?? new List<string>(),
                    Mode = "wordcloze",
                }));
            else
                again.onClick.AddListener(() => Nav.Replace<QuizScreen>(new QuizStartArgs
                {
                    QuestionCount = s.QuestionCount,
                    Difficulties = s.Difficulties ?? new List<int>(),
                    Dynasties = s.Dynasties ?? new List<string>(),
                    Mode = "challenge",
                }));

            var home = UiKit.Button("Home", footer.transform, "返回主页", out var homeLbl, Design.SecondaryFixed, 38);
            homeLbl.color = Design.Primary;
            home.onClick.AddListener(() => Nav.Pop());
        }

        private static void Spacer(Transform parent, float h) => UiKit.MinHeight(UiKit.Panel("Sp", parent), h);

        /// <summary>用时 · 最高连胜 · 答对, separated by vertical rules, framed by top/bottom hairlines.</summary>
        private static void BuildStats(Transform parent, ChallengeRecord record)
        {
            var row = UiKit.Panel("Stats", parent);
            UiKit.Pref(row, minH: 150).flexibleHeight = 0f;
            var hg = UiKit.HorizontalGroup(row, spacing: 0, pad: 0, align: TextAnchor.MiddleCenter);
            hg.childForceExpandWidth = false; hg.childForceExpandHeight = false;

            StatCell(row.transform, "用时", Fmt(record.DurationSeconds));
            Rule(row.transform);
            StatCell(row.transform, "最高连胜", record.BestStreak.ToString());
            Rule(row.transform);
            StatCell(row.transform, "答对", $"{record.CorrectCount}/{record.Total}");

            HairLine(row, top: true);
            HairLine(row, top: false);
        }

        private static void StatCell(Transform parent, string cap, string val)
        {
            var cell = UiKit.Panel("Cell", parent);
            UiKit.Pref(cell, minH: 120).flexibleWidth = 1f;
            var vg = UiKit.VerticalGroup(cell, spacing: 8, padX: 0, padY: 18, align: TextAnchor.MiddleCenter);
            vg.childForceExpandHeight = false;
            var c = UiKit.Text("Cap", cell.transform, cap, 22, TextAlignmentOptions.Center, Design.OnSurfaceVariant);
            c.characterSpacing = 4f;
            UiKit.Pref(c.gameObject, minH: 28);
            var v = UiKit.Text("Val", cell.transform, val, 40, TextAlignmentOptions.Center, Design.Primary);
            UiKit.Pref(v.gameObject, minH: 50);
        }

        private static void Rule(Transform parent)
        {
            var r = UiKit.Panel("VRule", parent, Design.Alpha(Design.Outline, 0.20f));
            var le = UiKit.Pref(r, minW: 2, minH: 56); le.flexibleWidth = 0f; le.flexibleHeight = 0f;
        }

        private static void HairLine(GameObject row, bool top)
        {
            var ln = UiKit.Panel("Hair", row.transform, Design.Alpha(Design.Outline, 0.18f));
            ln.GetComponent<Image>().raycastTarget = false;
            ln.AddComponent<LayoutElement>().ignoreLayout = true;
            var rt = UiKit.Rect(ln);
            rt.anchorMin = new Vector2(0, top ? 1 : 0); rt.anchorMax = new Vector2(1, top ? 1 : 0);
            rt.pivot = new Vector2(0.5f, top ? 1 : 0); rt.sizeDelta = new Vector2(0, 2); rt.anchoredPosition = Vector2.zero;
        }

        private static string Grade(int acc) =>
            acc >= 90 ? "妙笔生花" : acc >= 75 ? "出类拔萃" : acc >= 60 ? "渐入佳境" : "勤能补拙";

        private static string Fmt(int seconds)
        {
            int m = seconds / 60, s = seconds % 60;
            return $"{m}:{s:00}";
        }
    }
}
