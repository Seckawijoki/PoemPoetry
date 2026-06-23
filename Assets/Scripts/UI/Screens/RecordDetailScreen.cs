using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PoemPoetry.UI
{
    public sealed class RecordDetailScreen : UIScreen
    {
        protected override void OnShow(object args)
        {
            var a = args as RecordDetailArgs;
            var id = a?.RecordId;
            var body = UiKit.ScreenRoot(gameObject, "记录详情", () => Nav.Pop());
            UiKit.VerticalGroup(body.gameObject, spacing: 12, padX: 24, padY: 12, align: TextAnchor.UpperCenter);

            // Prev/next jump across the records list.
            if (a != null && a.Siblings != null && a.Siblings.Count > 1)
            {
                int n = a.Siblings.Count;
                var navRow = UiKit.Panel("Nav", body);
                UiKit.Pref(navRow, minH: 78).flexibleHeight = 0f;
                var hg = UiKit.HorizontalGroup(navRow, spacing: 12);
                hg.childForceExpandHeight = false;
                var prev = UiKit.Button("Prev", navRow.transform, "上一组", out _, UiKit.CardAlt, 30);
                UiKit.Pref(prev.gameObject, minH: 78);
                prev.onClick.AddListener(() => Jump(a.Siblings, (a.Index - 1 + n) % n));
                var pos = UiKit.Text("Pos", navRow.transform, $"{a.Index + 1}/{n}", 28, TextAlignmentOptions.Center, UiKit.Muted);
                pos.raycastTarget = false;
                var next = UiKit.Button("Next", navRow.transform, "下一组", out _, UiKit.CardAlt, 30);
                UiKit.Pref(next.gameObject, minH: 78);
                next.onClick.AddListener(() => Jump(a.Siblings, (a.Index + 1) % n));
            }

            Load(body, id);
        }

        private void Jump(List<string> siblings, int index)
        {
            Nav.Replace<RecordDetailScreen>(new RecordDetailArgs
            {
                RecordId = siblings[index], Siblings = siblings, Index = index,
            });
        }

        private async void Load(RectTransform body, string id)
        {
            var rec = await Services.Records.GetAsync(id);
            if (rec == null)
            {
                UiKit.Text("E", body, "记录不存在", 36, TextAlignmentOptions.Center, UiKit.Muted);
                return;
            }
            var head = UiKit.Text("H", body,
                $"正确率 {rec.AccuracyPercent}%    {rec.CorrectCount}/{rec.Total}    用时 {UiFormat.Duration(rec.DurationSeconds)}",
                34, TextAlignmentOptions.Center, UiKit.Accent);
            UiKit.MinHeight(head.gameObject, 70);

            // Slide records can replay their grid (practice without affecting the record).
            if (rec.Mode == "slide" && rec.Slide != null)
            {
                var replay = UiKit.Button("Replay", body, "查看本局网格", out _, UiKit.CardAlt, 32);
                UiKit.Pref(replay.gameObject, minH: 92);
                replay.onClick.AddListener(() => Nav.Push<SlidePuzzleScreen>(new SlideStartArgs { Replay = rec.Slide }));
            }

            var content = UiKit.ScrollList("Items", body, out _);
            var sib = new List<string>();
            foreach (var it in rec.Items) sib.Add(it.PoemId);
            for (int i = 0; i < rec.Items.Count; i++)
                ReviewRow.Build(content, Nav, rec.Items[i], sib, i);
        }
    }
}
