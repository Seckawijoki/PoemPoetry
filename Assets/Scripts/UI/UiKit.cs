using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PoemPoetry.UI
{
    /// <summary>
    /// Tiny code-driven uGUI/TMP factory so the whole app can be built at runtime with no
    /// prefabs or hand-wired serialized fields. Uses only stable Unity 2022 APIs.
    /// </summary>
    public static class UiKit
    {
        // Palette (ink-and-paper, fits classical poetry).
        public static readonly Color Paper = new Color(0.97f, 0.95f, 0.90f);
        public static readonly Color Ink = new Color(0.13f, 0.12f, 0.11f);
        public static readonly Color Accent = new Color(0.60f, 0.20f, 0.18f);   // 朱砂红
        public static readonly Color Card = new Color(1f, 1f, 1f, 0.92f);
        public static readonly Color CardAlt = new Color(0.93f, 0.90f, 0.83f);
        public static readonly Color Good = new Color(0.20f, 0.55f, 0.30f);
        public static readonly Color Bad = new Color(0.75f, 0.25f, 0.22f);
        public static readonly Color Muted = new Color(0.45f, 0.43f, 0.40f);

        public static (Canvas canvas, RectTransform root) CreateCanvas(string name)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return (canvas, go.GetComponent<RectTransform>());
        }

        public static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Object.DontDestroyOnLoad(go);
        }

        public static RectTransform Rect(GameObject go) =>
            go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();

        public static void StretchFull(GameObject go, float pad = 0f)
        {
            var rt = Rect(go);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
        }

        public static GameObject Panel(string name, Transform parent, Color? color = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            if (color.HasValue)
            {
                var img = go.AddComponent<Image>();
                img.color = color.Value;
            }
            return go;
        }

        public static TextMeshProUGUI Text(string name, Transform parent, string text, float size,
            TextAlignmentOptions align = TextAlignmentOptions.Center, Color? color = null, bool wrap = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color ?? Ink;
            // Default to no wrap + overflow so text never collapses/truncates when a layout
            // group computes a degenerate width; opt into wrap for long paragraphs.
            t.enableWordWrapping = wrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(string name, Transform parent, string label, out TextMeshProUGUI labelText,
            Color? bg = null, float labelSize = 40f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = bg ?? CardAlt;
            var btn = go.GetComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.85f, 0.82f, 0.75f);
            btn.colors = colors;

            labelText = Text(name + "Label", go.transform, label, labelSize, TextAlignmentOptions.Center, Ink);
            StretchFull(labelText.gameObject, 8f);
            return btn;
        }

        public static VerticalLayoutGroup VerticalGroup(GameObject go, int spacing = 16,
            int padX = 24, int padY = 24, TextAnchor align = TextAnchor.UpperCenter)
        {
            var v = go.GetComponent<VerticalLayoutGroup>() ?? go.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padX, padX, padY, padY);
            v.childAlignment = align;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HorizontalGroup(GameObject go, int spacing = 16,
            int pad = 0, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var h = go.GetComponent<HorizontalLayoutGroup>() ?? go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(pad, pad, pad, pad);
            h.childAlignment = align;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            return h;
        }

        /// <summary>Visually toggle a multi-select chip button (selected = accent fill, white text).</summary>
        public static void SetChipSelected(Button b, TextMeshProUGUI lbl, bool on)
        {
            if (b != null && b.image != null) b.image.color = on ? Accent : Card;
            if (lbl != null) lbl.color = on ? Color.white : Ink;
        }

        public static LayoutElement MinHeight(GameObject go, float height)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            return le;
        }

        public static LayoutElement Flexible(GameObject go, float flexH = 1f)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.flexibleHeight = flexH;
            return le;
        }

        /// <summary>
        /// Builds a vertical scrolling list and returns the content transform to add rows to.
        /// </summary>
        public static RectTransform ScrollList(string name, Transform parent, out ScrollRect scroll)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            root.transform.SetParent(parent, false);
            root.GetComponent<Image>().color = new Color(0, 0, 0, 0.02f);
            Flexible(root, 1f); // fill the remaining space inside a vertical layout group
            scroll = root.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30f;

            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(root.transform, false);
            StretchFull(viewport);
            scroll.viewport = viewport.GetComponent<RectTransform>();

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(0, 0);
            VerticalGroup(content, spacing: 12, padX: 12, padY: 12, align: TextAnchor.UpperCenter);
            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = crt;
            return crt;
        }

        public static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                Object.Destroy(t.GetChild(i).gameObject);
        }

        /// <summary>Anchor a full-width element to the top of its parent at a fixed height/offset.</summary>
        public static void AnchorTop(GameObject go, float height, float topOffset, float sideMargin)
        {
            var rt = Rect(go);
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(-2 * sideMargin, height);
            rt.anchoredPosition = new Vector2(0, -topOffset);
        }

        public static LayoutElement Pref(GameObject go, float minW = -1, float minH = -1, float flexH = -1)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (minW >= 0) { le.minWidth = minW; le.preferredWidth = minW; }
            if (minH >= 0) { le.minHeight = minH; le.preferredHeight = minH; }
            if (flexH >= 0) le.flexibleHeight = flexH;
            return le;
        }

        /// <summary>
        /// Standard screen chrome: Paper background, centered title, a flexible body panel, and
        /// an optional top-left Back button. Returns the body transform to populate.
        /// </summary>
        /// <summary>
        /// Top inset (in canvas reference units) needed to clear a notch / status bar, derived
        /// from <see cref="Screen.safeArea"/>. 0 on devices/editor without a top cutout.
        /// </summary>
        public static float SafeTopInset(GameObject reference)
        {
            var sa = Screen.safeArea;
            float topPx = Screen.height - sa.yMax;
            if (topPx <= 0.5f) return 0f;
            var canvas = reference != null ? reference.GetComponentInParent<Canvas>() : null;
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            return topPx / scale;
        }

        public static RectTransform ScreenRoot(GameObject screenGo, string title, System.Action onBack)
        {
            var bg = screenGo.GetComponent<Image>() ?? screenGo.AddComponent<Image>();
            bg.color = Paper;

            // Push the header below the notch / status bar on cutout phones.
            float safeTop = SafeTopInset(screenGo);

            if (onBack != null)
            {
                var bgo = new GameObject("Back", typeof(RectTransform), typeof(Image), typeof(Button));
                bgo.transform.SetParent(screenGo.transform, false);
                var rt = Rect(bgo);
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0, 1);
                rt.anchoredPosition = new Vector2(24, -(24 + safeTop));
                rt.sizeDelta = new Vector2(160, 76);
                bgo.GetComponent<Image>().color = CardAlt;
                var lbl = Text("L", bgo.transform, "返回", 34, TextAlignmentOptions.Center, Ink);
                StretchFull(lbl.gameObject, 6);
                bgo.GetComponent<Button>().onClick.AddListener(() => onBack());
            }

            // Title anchored at top, inset on both sides to clear the back button, and
            // auto-sizing so long titles shrink instead of sliding under the button.
            var titleText = Text("Title", screenGo.transform, title, 52, TextAlignmentOptions.Center, Accent);
            titleText.enableAutoSizing = true;
            titleText.fontSizeMin = 26;
            titleText.fontSizeMax = 52;
            AnchorTop(titleText.gameObject, height: 96, topOffset: 26 + safeTop, sideMargin: 200);

            // Body fills everything below the title band.
            var body = Panel("Body", screenGo.transform);
            var brt = Rect(body);
            brt.anchorMin = new Vector2(0, 0);
            brt.anchorMax = new Vector2(1, 1);
            brt.offsetMin = new Vector2(0, 0);
            brt.offsetMax = new Vector2(0, -(140 + safeTop));
            return brt;
        }
    }
}
