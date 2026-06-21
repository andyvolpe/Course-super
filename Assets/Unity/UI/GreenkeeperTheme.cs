using UnityEngine;

namespace Greenkeeper.Unity.UI
{
    /// <summary>
    /// A small IMGUI design system for a calm, championship aesthetic (Augusta / R&A register):
    /// deep pine panels, parchment-cream text, soft gold accents, sage for good and muted clay for
    /// bad. Builds its GUIStyles + 1px textures once (regenerated if the font size changes). Nature-
    /// toned and low-saturation so it reads as prestigious and calming over the 3D course.
    /// </summary>
    public sealed class GreenkeeperTheme
    {
        public static readonly GreenkeeperTheme I = new GreenkeeperTheme();

        // ---- palette ----
        public readonly Color Pine = Hex("16352A", 0.94f);     // panel body (translucent)
        public readonly Color PineDeep = Hex("0E241C", 0.97f); // dark accents / text on gold
        public readonly Color Header = Hex("1E4636", 0.98f);   // section header bar
        public readonly Color Cream = Hex("F1ECDD", 1f);       // body text
        public readonly Color Dimmed = Hex("F1ECDD", 0.6f);
        public readonly Color Gold = Hex("C8A14B", 1f);        // accent
        public readonly Color Sage = Hex("9CC196", 1f);        // positive
        public readonly Color Clay = Hex("CC7A5C", 1f);        // negative (muted, not harsh red)

        public GUIStyle Panel, Title, Section, Body, Dim, GoldText, Button, Toggle, BarBack;

        private Texture2D _panel, _header, _title, _btn, _btnHover, _btnOn, _barBack, _white;
        private GUIStyle _barLabel;
        private int _font = -1;

        public void Ensure(int font)
        {
            if (_font == font && Panel != null) return;
            _font = font;

            _panel = Tex(Pine);
            _header = Tex(Header);
            _title = Tex(Gold);
            _btn = Tex(Hex("2A5440", 0.96f));
            _btnHover = Tex(Hex("3A6F4C", 1f));
            _btnOn = Tex(Gold);
            _barBack = Tex(Hex("0E241C", 0.85f));
            _white = Tex(Color.white);

            Panel = new GUIStyle { padding = new RectOffset(12, 12, 10, 12), border = new RectOffset(2, 2, 2, 2) };
            Panel.normal.background = _panel;

            Body = new GUIStyle { fontSize = font, richText = true, wordWrap = true, padding = new RectOffset(2, 2, 1, 1) };
            Body.normal.textColor = Cream;

            Dim = new GUIStyle(Body); Dim.normal.textColor = Dimmed;

            GoldText = new GUIStyle(Body) { fontStyle = FontStyle.Bold };
            GoldText.normal.textColor = Gold;

            Section = new GUIStyle { fontSize = font + 1, fontStyle = FontStyle.Bold, richText = true,
                                     padding = new RectOffset(10, 10, 5, 5), margin = new RectOffset(0, 0, 4, 4) };
            Section.normal.background = _header;
            Section.normal.textColor = Gold;

            Title = new GUIStyle(Section) { fontSize = font + 6 };
            Title.normal.background = _title;
            Title.normal.textColor = PineDeep;

            Button = new GUIStyle { fontSize = font, richText = true, alignment = TextAnchor.MiddleCenter,
                                    padding = new RectOffset(8, 8, 6, 6), margin = new RectOffset(3, 3, 3, 3),
                                    border = new RectOffset(2, 2, 2, 2) };
            Button.normal.background = _btn; Button.normal.textColor = Cream;
            Button.hover.background = _btnHover; Button.hover.textColor = Cream;
            Button.active.background = _btnOn; Button.active.textColor = PineDeep;

            Toggle = new GUIStyle(Button);
            Toggle.onNormal.background = _btnOn; Toggle.onNormal.textColor = PineDeep;
            Toggle.onHover.background = _btnOn; Toggle.onHover.textColor = PineDeep;

            BarBack = new GUIStyle(); BarBack.normal.background = _barBack;
            _barLabel = new GUIStyle(Body) { alignment = TextAnchor.MiddleCenter };
        }

        /// <summary>Draws a labelled progress bar with a nature-toned fill (sage → gold → clay).</summary>
        public void Bar(Rect r, float frac01, string overlay)
        {
            frac01 = Mathf.Clamp01(frac01);
            GUI.DrawTexture(r, _barBack);
            Color prev = GUI.color;
            GUI.color = frac01 >= 1f ? Clay : Color.Lerp(Sage, Gold, frac01);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * frac01, r.height), _white); // tinted via GUI.color (no per-frame alloc)
            GUI.color = prev;
            if (!string.IsNullOrEmpty(overlay)) GUI.Label(r, overlay, _barLabel);
        }

        private static Color Hex(string h, float a)
        {
            ColorUtility.TryParseHtmlString("#" + h, out var c);
            c.a = a;
            return c;
        }

        private static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }
    }
}
