using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PoemPoetry.UI
{
    /// <summary>
    /// Home (主界面): a welcome caption, three calligraphic mode cards (问答 / 找诗 / 填空) and a 2×2
    /// quick-access grid (记录 / 收藏 / 错题 / 设置), styled per designs/stitch/主界面.
    /// </summary>
    public sealed class MainMenuScreen : UIScreen
    {
        private TextMeshProUGUI _wrongNav;

        protected override void BuildUI()
        {
            // Home has no center brand title (removed per request); just the settings action.
            var body = Design.Chrome(gameObject, null, () => Nav.Push<SettingsScreen>(), "");
            UiKit.VerticalGroup(body.gameObject, spacing: 0, padX: 40, padY: 24, align: TextAnchor.UpperCenter);
            var scroll = UiKit.ScrollList("Home", body, out _);

            ModeCard(scroll, "诗词问答", "Answer Questions", "开始挑战", Design.SurfaceLow, () => Nav.Push<QuizConfigScreen>());
            ModeCard(scroll, "划线寻踪", "Slide to Find Poetry", "步入词林", Design.SurfaceHigh, () => Nav.Push<SlideConfigScreen>());
            ModeCard(scroll, "墨意填空", "Fill in the Blanks", "落笔成章", Design.SurfaceHighest, () => Nav.Push<WordClozeConfigScreen>());
            Spacer(scroll, 16);

            BuildQuickGrid(scroll);
        }

        protected override void OnShow(object args) => RefreshBadge();
        protected override void OnFocus() => RefreshBadge();

        private static void Spacer(Transform parent, float h) => UiKit.MinHeight(UiKit.Panel("Sp", parent), h);

        /// <summary>A full-width mode card: vertical cinnabar name | English subtitle + CTA.</summary>
        private void ModeCard(Transform parent, string nameZh, string en, string cta, Color bg, UnityAction action)
        {
            var go = new GameObject("Mode", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = bg;
            UiKit.Pref(go, minH: 230);
            var hg = UiKit.HorizontalGroup(go, spacing: 0, pad: 0, align: TextAnchor.MiddleLeft);
            hg.padding = new RectOffset(30, 30, 20, 20);
            hg.childForceExpandWidth = false; hg.childForceExpandHeight = true;

            // Vertical-stacked cinnabar name (one char per line), auto-sized so it never overflows.
            var col = UiKit.Panel("V", go.transform);
            var colLe = UiKit.Pref(col, minW: 96); colLe.flexibleWidth = 0f;
            var vt = UiKit.Text("VT", col.transform, string.Join("\n", nameZh.ToCharArray()), 36,
                TextAlignmentOptions.Center, Design.Primary);
            vt.fontStyle = FontStyles.Bold;
            vt.lineSpacing = -8f;                 // pull the stacked chars together (was overflowing)
            vt.enableWordWrapping = true;
            vt.enableAutoSizing = true; vt.fontSizeMin = 22; vt.fontSizeMax = 38;
            UiKit.StretchFull(vt.gameObject, 4);

            var div = UiKit.Panel("Div", go.transform, Design.Alpha(Design.Primary, 0.15f));
            div.GetComponent<Image>().raycastTarget = false;
            var dle = UiKit.Pref(div, minW: 2); dle.flexibleWidth = 0f;

            var info = UiKit.Panel("Info", go.transform);
            var ile = info.AddComponent<LayoutElement>(); ile.flexibleWidth = 1f;
            var ig = UiKit.VerticalGroup(info, spacing: 10, padX: 26, padY: 0, align: TextAnchor.MiddleLeft);
            ig.childForceExpandHeight = false; ig.childForceExpandWidth = true; ig.childControlHeight = true;
            var sub = UiKit.Text("Sub", info.transform, en, 26, TextAlignmentOptions.Left, Design.OnSurfaceVariant);
            UiKit.Pref(sub.gameObject, minH: 36);
            var ctaText = UiKit.Text("CTA", info.transform, cta + "  →", 34, TextAlignmentOptions.Left, Design.Primary);
            ctaText.fontStyle = FontStyles.Bold;
            UiKit.Pref(ctaText.gameObject, minH: 44);

            go.GetComponent<Button>().onClick.AddListener(action);
        }

        // Even 2×2 grid (uniform cells) of quick-access entries.
        private void BuildQuickGrid(Transform parent)
        {
            var grid = new GameObject("Quick", typeof(RectTransform), typeof(GridLayoutGroup));
            grid.transform.SetParent(parent, false);
            var glg = grid.GetComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(460, 132);
            glg.spacing = new Vector2(16, 16);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 2;
            glg.childAlignment = TextAnchor.UpperCenter;

            QuickButton(grid.transform, "历练记录", "HISTORY", () => Nav.Push<RecordsScreen>(), out _);
            QuickButton(grid.transform, "雅集收藏", "FAVORITES", () => Nav.Push<FavoritesScreen>(), out _);
            QuickButton(grid.transform, "纠偏补缺", "MISTAKES", () => Nav.Push<WrongBookScreen>(), out _wrongNav);
            QuickButton(grid.transform, "笔墨设置", "SETTINGS", () => Nav.Push<SettingsScreen>(), out _);
        }

        private void QuickButton(Transform parent, string zh, string en, UnityAction action, out TextMeshProUGUI zhLbl)
        {
            var go = new GameObject("Q", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Design.SurfaceContainer;
            var vg = UiKit.VerticalGroup(go, spacing: 4, padX: 24, padY: 0, align: TextAnchor.MiddleLeft);
            vg.childForceExpandHeight = false; vg.childControlHeight = true;
            zhLbl = UiKit.Text("Zh", go.transform, zh, 32, TextAlignmentOptions.Left, Design.Ink);
            zhLbl.fontStyle = FontStyles.Bold;
            UiKit.Pref(zhLbl.gameObject, minH: 40);
            var enLbl = UiKit.Text("En", go.transform, en, 18, TextAlignmentOptions.Left, Design.Alpha(Design.Secondary, 0.7f));
            enLbl.characterSpacing = 3f;
            UiKit.Pref(enLbl.gameObject, minH: 24);
            go.GetComponent<Button>().onClick.AddListener(action);
        }

        private async void RefreshBadge()
        {
            if (_wrongNav == null || Services == null) return;
            int due = await Services.WrongBook.GetDueCountAsync();
            _wrongNav.text = due > 0 ? $"纠偏补缺 · {due}" : "纠偏补缺";
        }
    }
}
