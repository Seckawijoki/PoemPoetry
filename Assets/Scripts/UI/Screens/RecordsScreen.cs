using PoemPoetry.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoemPoetry.UI
{
    /// <summary>历史记录: summary stats + per-session cards (mode tag · date · score), per designs/stitch/历史记录.</summary>
    public sealed class RecordsScreen : UIScreen
    {
        private RectTransform _content;
        private string _mode;   // null = all; "challenge" = challenge+wrongbook; "slide" = slide

        protected override void OnShow(object args)
        {
            var a = args as RecordsArgs;
            _mode = a?.Mode;
            string title = !string.IsNullOrEmpty(a?.Title) ? a.Title : "历史记录";
            var body = Design.Chrome(gameObject, () => Nav.Pop(), () => Nav.Push<SettingsScreen>(), title);
            UiKit.VerticalGroup(body.gameObject, spacing: 12, padX: 28, padY: 16, align: TextAnchor.UpperCenter);
            _content = UiKit.ScrollList("List", body, out _);
            Refresh();
        }

        protected override void OnFocus() => Refresh();

        private bool ModeMatch(string recMode)
        {
            if (string.IsNullOrEmpty(_mode)) return true;
            if (_mode == "challenge") return recMode == "challenge" || recMode == "wrongbook";
            return recMode == _mode;
        }

        private async void Refresh()
        {
            if (_content == null) return;
            UiKit.ClearChildren(_content);
            var summaries = await Services.Records.GetAllAsync();

            var ids = new System.Collections.Generic.List<string>();
            int accSum = 0;
            foreach (var s in summaries)
                if (ModeMatch(s.Mode)) { ids.Add(s.Id); accSum += s.AccuracyPercent; }

            if (ids.Count == 0)
            {
                UiKit.Text("Empty", _content, "暂无记录", 32, TextAlignmentOptions.Center, Design.OnSurfaceVariant);
                return;
            }

            BuildSummary(ids.Count, accSum / ids.Count);

            int shown = 0;
            foreach (var s in summaries)
            {
                if (!ModeMatch(s.Mode)) continue;
                int idx = shown; shown++;
                var id = s.Id;
                BuildRecordCard(s, ids, idx, id);
            }
        }

        private void BuildSummary(int count, int avgAccuracy)
        {
            var row = UiKit.Panel("Summary", _content);
            UiKit.Pref(row, minH: 132);
            var hg = UiKit.HorizontalGroup(row, spacing: 16, pad: 0);
            hg.childForceExpandHeight = true;
            StatCard(row.transform, "挑战次数", count.ToString());
            StatCard(row.transform, "平均正确率", $"{avgAccuracy}%");
        }

        private static void StatCard(Transform parent, string caption, string value)
        {
            var card = UiKit.Panel("Stat", parent, Design.SurfaceContainer);
            card.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var bar = UiKit.Panel("Bar", card.transform, Design.Primary);
            bar.GetComponent<Image>().raycastTarget = false;
            var brt = UiKit.Rect(bar);
            brt.anchorMin = new Vector2(0, 0); brt.anchorMax = new Vector2(0, 1); brt.pivot = new Vector2(0, 0.5f);
            brt.sizeDelta = new Vector2(8, 0); brt.anchoredPosition = Vector2.zero;
            Design.CardText("Cap", card.transform, caption, 22, Design.OnSurfaceVariant, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1), new Vector2(8, -30), new Vector2(320, 30));
            Design.CardText("Val", card.transform, value, 44, Design.Primary, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1), new Vector2(8, -64), new Vector2(320, 56));
        }

        private void BuildRecordCard(ChallengeRecordSummary s, System.Collections.Generic.List<string> ids, int idx, string id)
        {
            var card = Design.CardButton("Rec", _content, out var btn, 196);
            Design.Corners(card, Design.Alpha(Design.Outline, 0.3f), arm: 26, thick: 1, inset: 10);

            Design.Tag("Tag", card.transform, UiFormat.Mode(s.Mode), new Vector2(0, 1), new Vector2(24, -22),
                new Vector2(160, 46), Design.PrimaryContainer, 24);
            Design.CardText("Date", card.transform, UiFormat.Date(s.CompletedAtUtc), 24, Design.OnSurfaceVariant,
                TextAlignmentOptions.Left, new Vector2(0, 1), new Vector2(28, -82), new Vector2(540, 40));

            Design.CardText("Pct", card.transform, $"{s.AccuracyPercent}%", 56, Design.Primary,
                TextAlignmentOptions.Right, new Vector2(1, 1), new Vector2(-28, -20), new Vector2(280, 70));
            Design.CardText("PctCap", card.transform, "正确率", 20, Design.OnSurfaceVariant,
                TextAlignmentOptions.Right, new Vector2(1, 1), new Vector2(-28, -96), new Vector2(280, 28));

            var div = UiKit.Panel("Div", card.transform, Design.Alpha(Design.Outline, 0.12f));
            div.GetComponent<Image>().raycastTarget = false;
            var drt = UiKit.Rect(div);
            drt.anchorMin = new Vector2(0, 1); drt.anchorMax = new Vector2(1, 1); drt.pivot = new Vector2(0.5f, 1);
            drt.sizeDelta = new Vector2(-48, 2); drt.anchoredPosition = new Vector2(0, -126);

            Design.CardText("Foot", card.transform,
                $"用时 {UiFormat.Duration(s.DurationSeconds)}　·　{s.CorrectCount}/{s.Total}　·　连胜 {s.BestStreak}",
                24, Design.OnSurfaceVariant, TextAlignmentOptions.Left,
                new Vector2(0, 0), new Vector2(28, 28), new Vector2(620, 40));
            Design.CardText("Chev", card.transform, "▸", 30, Design.Alpha(Design.Outline, 0.6f),
                TextAlignmentOptions.Right, new Vector2(1, 0), new Vector2(-28, 28), new Vector2(60, 40));

            btn.onClick.AddListener(() => Nav.Push<RecordDetailScreen>(
                new RecordDetailArgs { RecordId = id, Siblings = ids, Index = idx }));
        }
    }
}
