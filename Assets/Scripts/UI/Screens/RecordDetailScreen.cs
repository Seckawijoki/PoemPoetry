using System.Collections;
using System.Collections.Generic;
using PoemPoetry.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoemPoetry.UI
{
    /// <summary>历史记录详情: 本局表现 hero + 正确/错误/用时 stats + 题目回顾, per designs/stitch/历史记录详情.</summary>
    public sealed class RecordDetailScreen : UIScreen
    {
        protected override void OnShow(object args)
        {
            var a = args as RecordDetailArgs;
            var id = a?.RecordId;
            var body = Design.Chrome(gameObject, () => Nav.Pop(), () => Nav.Push<SettingsScreen>(), "记录详情");
            UiKit.VerticalGroup(body.gameObject, spacing: 12, padX: 28, padY: 16, align: TextAnchor.UpperCenter);

            var scroll = UiKit.ScrollList("Items", body, out _);

            // Prev/next jump across the records list (+ swipe).
            if (a != null && a.Siblings != null && a.Siblings.Count > 1)
            {
                int n = a.Siblings.Count;
                var navRow = UiKit.Panel("Nav", scroll);
                UiKit.Pref(navRow, minH: 78);
                var hg = UiKit.HorizontalGroup(navRow, spacing: 12);
                hg.childForceExpandHeight = false;
                var prev = UiKit.Button("Prev", navRow.transform, "上一组", out _, Design.SurfaceHigh, 30);
                UiKit.Pref(prev.gameObject, minH: 78);
                prev.onClick.AddListener(() => Jump(a.Siblings, (a.Index - 1 + n) % n, -1));
                var pos = UiKit.Text("Pos", navRow.transform, $"{a.Index + 1}/{n}", 28, TextAlignmentOptions.Center, Design.OnSurfaceVariant);
                pos.raycastTarget = false;
                var next = UiKit.Button("Next", navRow.transform, "下一组", out _, Design.SurfaceHigh, 30);
                UiKit.Pref(next.gameObject, minH: 78);
                next.onClick.AddListener(() => Jump(a.Siblings, (a.Index + 1) % n, +1));

                // Left/right swipe flips to next/prev record (slide in from the corresponding side).
                var swipe = gameObject.AddComponent<SwipeNav>();
                swipe.OnSwipeLeft = () => Jump(a.Siblings, (a.Index + 1) % n, +1);
                swipe.OnSwipeRight = () => Jump(a.Siblings, (a.Index - 1 + n) % n, -1);
            }

            Load(scroll, id);

            if (a != null && a.SlideFrom != 0) StartCoroutine(SlideIn(a.SlideFrom));
        }

        // New page slides in from the side it was summoned from (next = right, prev = left).
        private IEnumerator SlideIn(int dir)
        {
            var rt = (RectTransform)transform;
            float w = rt.rect.width > 1f ? rt.rect.width : 1080f;
            Vector2 from = new Vector2(dir * w, 0f);
            Vector2 to = Vector2.zero;
            const float dur = 0.18f;
            float t = 0f;
            rt.anchoredPosition = from;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                rt.anchoredPosition = Vector2.LerpUnclamped(from, to, k);
                yield return null;
            }
            rt.anchoredPosition = to;
        }

        private void Jump(List<string> siblings, int index, int dir)
        {
            Nav.Replace<RecordDetailScreen>(new RecordDetailArgs
            {
                RecordId = siblings[index], Siblings = siblings, Index = index, SlideFrom = dir,
            });
        }

        private async void Load(Transform scroll, string id)
        {
            var rec = await Services.Records.GetAsync(id);
            if (rec == null)
            {
                UiKit.Text("E", scroll, "记录不存在", 36, TextAlignmentOptions.Center, Design.OnSurfaceVariant);
                return;
            }

            BuildHero(scroll, rec);

            // Slide records can replay their grid (practice without affecting the record).
            if (rec.Mode == "slide" && rec.Slide != null)
            {
                var replay = UiKit.Button("Replay", scroll, "查看本局网格", out var rl, Design.SurfaceHigh, 32);
                rl.color = Design.Ink;
                UiKit.Pref(replay.gameObject, minH: 96);
                replay.onClick.AddListener(() => Nav.Push<SlidePuzzleScreen>(new SlideStartArgs { Replay = rec.Slide }));
            }

            Design.SectionHead(scroll, "题目回顾");
            var sib = new List<string>();
            foreach (var it in rec.Items) sib.Add(it.PoemId);
            bool showChosen = rec.Mode != "slide";
            for (int i = 0; i < rec.Items.Count; i++)
                ReviewRow.Build(scroll, Nav, rec.Items[i], sib, i, showChosen);
        }

        private static void BuildHero(Transform parent, ChallengeRecord rec)
        {
            var card = UiKit.Panel("Hero", parent, Design.SurfaceLow);
            UiKit.Pref(card, minH: 360);
            Design.Corners(card, Design.Alpha(Design.PrimaryContainer, 0.4f), arm: 30, thick: 2, inset: 14);

            Design.CardText("Cap", card.transform, "本局表现", 26, Design.OnSurfaceVariant, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1), new Vector2(0, -36), new Vector2(400, 32));
            Design.CardText("Pct", card.transform, $"{rec.AccuracyPercent}%", 80, Design.PrimaryContainer, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1), new Vector2(0, -72), new Vector2(400, 96));
            Design.CardText("Grade", card.transform, Grade(rec.AccuracyPercent), 30, Design.Primary, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1), new Vector2(0, -178), new Vector2(460, 40));

            var div = UiKit.Panel("Div", card.transform, Design.Alpha(Design.Outline, 0.16f));
            div.GetComponent<Image>().raycastTarget = false;
            var drt = UiKit.Rect(div);
            drt.anchorMin = new Vector2(0, 1); drt.anchorMax = new Vector2(1, 1); drt.pivot = new Vector2(0.5f, 1);
            drt.sizeDelta = new Vector2(-64, 2); drt.anchoredPosition = new Vector2(0, -232);

            string greenHex = ColorUtility.ToHtmlStringRGB(UiKit.Good);
            string redHex = ColorUtility.ToHtmlStringRGB(UiKit.Bad);
            string primHex = ColorUtility.ToHtmlStringRGB(Design.Primary);
            StatCol(card.transform, 0, "正确", rec.CorrectCount.ToString(), $"#{greenHex}");
            StatCol(card.transform, 1, "错误", (rec.Total - rec.CorrectCount).ToString(), $"#{redHex}");
            StatCol(card.transform, 2, "用时", UiFormat.Duration(rec.DurationSeconds), $"#{primHex}");

            VRule(card.transform, 1f / 3f);
            VRule(card.transform, 2f / 3f);
        }

        // Three stat columns, each stretched across a third of the card so the text stays centered.
        private static void StatCol(Transform card, int col, string caption, string value, string colorHex)
        {
            StatText(card, col, caption, 22, Design.OnSurfaceVariant, -262, 28);
            StatText(card, col, $"<color={colorHex}>{value}</color>", 42, Design.Ink, -296, 56);
        }

        private static void StatText(Transform card, int col, string text, float size, Color color, float yTop, float h)
        {
            var t = UiKit.Text("S", card, text, size, TextAlignmentOptions.Center, color);
            var rt = UiKit.Rect(t.gameObject);
            rt.anchorMin = new Vector2(col / 3f, 1); rt.anchorMax = new Vector2((col + 1) / 3f, 1);
            rt.pivot = new Vector2(0.5f, 1); rt.sizeDelta = new Vector2(0, h); rt.anchoredPosition = new Vector2(0, yTop);
        }

        private static void VRule(Transform card, float x)
        {
            var r = UiKit.Panel("VRule", card, Design.Alpha(Design.Outline, 0.16f));
            r.GetComponent<Image>().raycastTarget = false;
            var rt = UiKit.Rect(r);
            rt.anchorMin = rt.anchorMax = new Vector2(x, 1); rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(2, 80); rt.anchoredPosition = new Vector2(0, -258);
        }

        private static string Grade(int acc) =>
            acc >= 90 ? "文思敏捷 · 妙笔生花" : acc >= 75 ? "渐入佳境 · 出类拔萃"
            : acc >= 60 ? "勤学不辍 · 渐有所成" : "温故知新 · 勤能补拙";
    }
}
