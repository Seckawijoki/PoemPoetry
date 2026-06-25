using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoemPoetry.UI
{
    /// <summary>
    /// Shared "Ink &amp; Parchment" design tokens and reusable widget builders, matching the Stitch
    /// mockups in <c>designs/stitch</c>. Layered on top of <see cref="UiKit"/> (which stays the
    /// low-level uGUI/TMP factory) so screens can be re-skinned without per-file color literals.
    /// </summary>
    public static class Design
    {
        private static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        // Palette (designs/stitch/*/DESIGN.md).
        public static readonly Color Paper = UiKit.Paper;            // surface  #fef9ea
        public static readonly Color Ink = UiKit.Ink;               // on-surface #1d1c13
        public static readonly Color Primary = Hex("#761519");          // cinnabar seal
        public static readonly Color PrimaryContainer = Hex("#962d2d"); // filled CTA / selected
        public static readonly Color SecondaryFixed = Hex("#e8e2d0");   // tiles / secondary fill
        public static readonly Color Secondary = Hex("#625e50");        // captions
        public static readonly Color OnSurfaceVariant = Hex("#574240"); // sub text
        public static readonly Color SurfaceLow = Hex("#f8f3e4");
        public static readonly Color SurfaceContainer = Hex("#f3eedf");
        public static readonly Color SurfaceHigh = Hex("#ede8d9");
        public static readonly Color SurfaceHighest = Hex("#e7e2d4");
        public static readonly Color CardWhite = Hex("#ffffff");
        public static readonly Color Outline = Hex("#8a716f");

        public const float HeaderH = 120f;
        public const string Brand = "墨香诗韵";

        public static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>
        /// Top app bar (optional back · centered brand · optional settings) over a Paper canvas.
        /// Returns the body RectTransform filling everything below the header.
        /// </summary>
        public static RectTransform Chrome(GameObject screen, Action onBack, Action onSettings, string brand = Brand)
        {
            var bg = screen.GetComponent<Image>() ?? screen.AddComponent<Image>();
            bg.color = Paper;

            float safeTop = UiKit.SafeTopInset(screen);

            var header = UiKit.Panel("Header", screen.transform, Paper);
            var hrt = UiKit.Rect(header);
            hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1); hrt.pivot = new Vector2(0.5f, 1);
            hrt.sizeDelta = new Vector2(0, HeaderH + safeTop); hrt.anchoredPosition = Vector2.zero;

            var line = UiKit.Panel("HeaderBorder", header.transform, Alpha(Outline, 0.20f));
            var lr = UiKit.Rect(line);
            lr.anchorMin = new Vector2(0, 0); lr.anchorMax = new Vector2(1, 0); lr.pivot = new Vector2(0.5f, 0);
            lr.sizeDelta = new Vector2(0, 2); lr.anchoredPosition = Vector2.zero;

            float iconY = -(safeTop + (HeaderH - 84f) / 2f);
            if (onBack != null)
                IconButton(screen.transform, "Back", "←", 50, Primary, new Vector2(0, 1), new Vector2(20, iconY), onBack);
            if (onSettings != null)
                IconButton(screen.transform, "Settings", "设置", 30, Primary, new Vector2(1, 1), new Vector2(-24, iconY), onSettings);

            var title = UiKit.Text("Brand", screen.transform, brand, 44, TextAlignmentOptions.Center, Primary);
            title.characterSpacing = 10f;
            UiKit.AnchorTop(title.gameObject, height: 80, topOffset: safeTop + (HeaderH - 80f) / 2f, sideMargin: 220);

            var body = UiKit.Panel("Body", screen.transform);
            var brt = UiKit.Rect(body);
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero; brt.offsetMax = new Vector2(0, -(HeaderH + safeTop));
            return brt;
        }

        /// <summary>Borderless glyph/text button anchored to a corner of <paramref name="parent"/>.</summary>
        public static Button IconButton(Transform parent, string name, string glyph, float size, Color color,
            Vector2 corner, Vector2 pos, Action onClick, float w = 96f, float h = 84f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0, 0, 0, 0); // transparent hit area
            var rt = UiKit.Rect(go);
            rt.anchorMin = rt.anchorMax = corner; rt.pivot = corner;
            rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = pos;
            var lbl = UiKit.Text("L", go.transform, glyph, size, TextAlignmentOptions.Center, color);
            UiKit.StretchFull(lbl.gameObject);
            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(() => onClick());
            return btn;
        }

        /// <summary>Window-lattice corner motif (L-brackets) at the four corners of a panel.</summary>
        public static void Corners(GameObject card, Color? color = null, float arm = 46f, float thick = 3f, float inset = 18f)
        {
            var col = color ?? Alpha(Outline, 0.40f);
            Vector2[] corners = { new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 0), new Vector2(1, 0) };
            foreach (var a in corners)
            {
                float sx = a.x == 0 ? 1f : -1f;
                float sy = a.y == 1 ? -1f : 1f;
                var pos = new Vector2(sx * inset, sy * inset);
                var hh = UiKit.Rect(FreeCorner("CornerH", card.transform, col));
                hh.anchorMin = hh.anchorMax = hh.pivot = a; hh.sizeDelta = new Vector2(arm, thick); hh.anchoredPosition = pos;
                var vv = UiKit.Rect(FreeCorner("CornerV", card.transform, col));
                vv.anchorMin = vv.anchorMax = vv.pivot = a; vv.sizeDelta = new Vector2(thick, arm); vv.anchoredPosition = pos;
            }
        }

        // Corner arm that is anchor-positioned and excluded from any parent layout group.
        private static GameObject FreeCorner(string name, Transform parent, Color col)
        {
            var go = UiKit.Panel(name, parent, col);
            go.GetComponent<Image>().raycastTarget = false;
            var le = go.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            return go;
        }

        /// <summary>
        /// A framed parchment card (subtle lattice corners) whose own VerticalLayoutGroup +
        /// ContentSizeFitter size it to its children. Returns the card transform to populate.
        /// </summary>
        public static Transform Card(Transform parent, Color? bg = null, int pad = 30, int spacing = 26)
        {
            // No ContentSizeFitter: the card lives inside the scroll content's VerticalLayoutGroup,
            // and its own VerticalLayoutGroup already reports a preferred height to that parent.
            var go = UiKit.Panel("Card", parent, bg ?? SurfaceLow);
            var vg = UiKit.VerticalGroup(go, spacing, pad, pad, TextAnchor.UpperCenter);
            vg.childForceExpandHeight = false;
            Corners(go);
            return go.transform;
        }

        /// <summary>Section header: a short cinnabar bar + title, as used on the config pages.</summary>
        public static void SectionHead(Transform parent, string text)
        {
            var row = UiKit.Panel("Sec", parent);
            UiKit.Pref(row, minH: 70);
            var h = UiKit.HorizontalGroup(row, spacing: 16, pad: 0, align: TextAnchor.MiddleLeft);
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;

            var bar = UiKit.Panel("Bar", row.transform, Primary);
            var ble = UiKit.Pref(bar, minW: 8, minH: 40); ble.flexibleWidth = 0f;
            var t = UiKit.Text("T", row.transform, text, 34, TextAlignmentOptions.Left, Ink);
            var tle = UiKit.Pref(t.gameObject, minH: 44); tle.flexibleWidth = 1f;
        }

        /// <summary>Solid cinnabar CTA button (sharp corners, white serif text).</summary>
        public static Button PrimaryButton(string name, Transform parent, string label, out TextMeshProUGUI lbl, float size = 40f)
        {
            var b = UiKit.Button(name, parent, label, out lbl, PrimaryContainer, size);
            lbl.color = Color.white;
            var colors = b.colors;
            colors.highlightedColor = Color.white;
            colors.pressedColor = Alpha(Primary, 1f);
            b.colors = colors;
            return b;
        }

        /// <summary>Selection chip styling: filled cinnabar when on, parchment with ink text when off.</summary>
        public static void SetChip(Button b, TextMeshProUGUI lbl, bool on)
        {
            if (b != null && b.image != null) b.image.color = on ? PrimaryContainer : SurfaceHigh;
            if (lbl != null) lbl.color = on ? Color.white : Ink;
        }

        /// <summary>A tappable parchment list card (fixed height); add anchored children with <see cref="CardText"/>.</summary>
        public static GameObject CardButton(string name, Transform parent, out Button btn, float minH, Color? bg = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = bg ?? SurfaceContainer;
            btn = go.GetComponent<Button>();
            UiKit.Pref(go, minH: minH);
            return go;
        }

        /// <summary>Anchored text inside a fixed-size card. corner = anchor/pivot (e.g. (0,1) top-left).</summary>
        public static TextMeshProUGUI CardText(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions align, Vector2 corner, Vector2 pos, Vector2 sizeDelta, bool wrap = false)
        {
            var t = UiKit.Text(name, parent, text, size, align, color, wrap);
            var rt = UiKit.Rect(t.gameObject);
            rt.anchorMin = rt.anchorMax = corner; rt.pivot = corner;
            rt.anchoredPosition = pos; rt.sizeDelta = sizeDelta;
            return t;
        }

        /// <summary>A small filled cinnabar tag (label chip), anchored. Returns the tag GameObject.</summary>
        public static GameObject Tag(string name, Transform parent, string text, Vector2 corner, Vector2 pos,
            Vector2 size, Color? bg = null, float fontSize = 22f)
        {
            var go = UiKit.Panel(name, parent, bg ?? PrimaryContainer);
            go.GetComponent<Image>().raycastTarget = false;
            var rt = UiKit.Rect(go);
            rt.anchorMin = rt.anchorMax = corner; rt.pivot = corner;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var lbl = UiKit.Text("L", go.transform, text, fontSize, TextAlignmentOptions.Center, Color.white);
            UiKit.StretchFull(lbl.gameObject, 6);
            return go;
        }

        /// <summary>Thin horizontal divider line that ignores layout (anchor it yourself if needed).</summary>
        public static GameObject HLine(Transform parent, float alpha = 0.14f)
        {
            var ln = UiKit.Panel("HLine", parent, Alpha(Outline, alpha));
            ln.GetComponent<Image>().raycastTarget = false;
            return ln;
        }
    }
}
