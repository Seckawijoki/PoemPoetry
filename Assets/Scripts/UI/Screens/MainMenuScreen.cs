using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace PoemPoetry.UI
{
    /// <summary>Home: two primary modes (答题 / 滑动找诗) plus secondary entries.</summary>
    public sealed class MainMenuScreen : UIScreen
    {
        private TextMeshProUGUI _wrongNav;

        protected override void BuildUI()
        {
            var body = UiKit.ScreenRoot(gameObject, "唐诗宋词", null);
            UiKit.VerticalGroup(body.gameObject, spacing: 22, padX: 40, padY: 24, align: TextAnchor.UpperCenter);

            UiKit.Text("Hint", body, "中国古典诗词 · 填空与找诗", 32, TextAlignmentOptions.Center, UiKit.Muted)
                .gameObject.AddComponent<UnityEngine.UI.LayoutElement>().minHeight = 70;

            var quiz = UiKit.Button("Quiz", body, "答题", out var quizLbl, UiKit.Accent, 56);
            quizLbl.color = Color.white;
            UiKit.Pref(quiz.gameObject, minH: 220);
            quiz.onClick.AddListener(() => Nav.Push<QuizConfigScreen>());

            var slide = UiKit.Button("Slide", body, "滑动找诗", out var slideLbl, UiKit.Accent, 56);
            slideLbl.color = Color.white;
            UiKit.Pref(slide.gameObject, minH: 220);
            slide.onClick.AddListener(() => Nav.Push<SlideConfigScreen>());

            UiKit.Panel("Gap", body); // small spacer via flexible
            UiKit.Flexible(UiKit.Panel("Flex", body), 1f);

            AddNav(body, "历史记录", () => Nav.Push<RecordsScreen>());
            AddNav(body, "收藏夹", () => Nav.Push<FavoritesScreen>());
            _wrongNav = AddNav(body, "错题本", () => Nav.Push<WrongBookScreen>());
            AddNav(body, "设置", () => Nav.Push<SettingsScreen>());
        }

        protected override void OnShow(object args) => RefreshBadge();
        protected override void OnFocus() => RefreshBadge();

        private TextMeshProUGUI AddNav(Transform body, string label, UnityAction action)
        {
            var b = UiKit.Button(label, body, label, out var lbl, UiKit.Card, 34);
            UiKit.Pref(b.gameObject, minH: 86);
            b.onClick.AddListener(action);
            return lbl;
        }

        private async void RefreshBadge()
        {
            if (_wrongNav == null || Services == null) return;
            int due = await Services.WrongBook.GetDueCountAsync();
            _wrongNav.text = due > 0 ? $"错题本 ({due} 待复习)" : "错题本";
        }
    }
}
