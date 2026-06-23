using TMPro;
using UnityEngine;

namespace PoemPoetry.UI
{
    public sealed class RecordsScreen : UIScreen
    {
        private RectTransform _content;
        private string _mode;   // null = all; "challenge" = challenge+wrongbook; "slide" = slide

        protected override void OnShow(object args)
        {
            var a = args as RecordsArgs;
            _mode = a?.Mode;
            string title = !string.IsNullOrEmpty(a?.Title) ? a.Title : "历史记录";
            var body = UiKit.ScreenRoot(gameObject, title, () => Nav.Pop());
            UiKit.VerticalGroup(body.gameObject, spacing: 12, padX: 24, padY: 12, align: TextAnchor.UpperCenter);
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
            foreach (var s in summaries)
                if (ModeMatch(s.Mode)) ids.Add(s.Id);

            int shown = 0;
            foreach (var s in summaries)
            {
                if (!ModeMatch(s.Mode)) continue;
                int idx = shown;
                shown++;
                var id = s.Id;
                var btn = UiKit.Button("Rec", _content, "", out var lbl, UiKit.Card, 30);
                UiKit.Pref(btn.gameObject, minH: 152);
                lbl.alignment = TextAlignmentOptions.Left;
                lbl.text =
                    $"<size=130%><color=#983430>{s.AccuracyPercent}%</color></size>   {UiFormat.Mode(s.Mode)}\n" +
                    $"<size=80%><color=#9A938C>{UiFormat.Date(s.CompletedAtUtc)}   {s.CorrectCount}/{s.Total}   用时 {UiFormat.Duration(s.DurationSeconds)}   连胜 {s.BestStreak}</color></size>";
                btn.onClick.AddListener(() => Nav.Push<RecordDetailScreen>(
                    new RecordDetailArgs { RecordId = id, Siblings = ids, Index = idx }));
            }
            if (shown == 0)
                UiKit.Text("Empty", _content, "暂无记录", 32, TextAlignmentOptions.Center, UiKit.Muted);
        }
    }
}
