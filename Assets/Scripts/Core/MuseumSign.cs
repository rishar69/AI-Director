using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The lettering on one of the museum's signs.
///
/// The plate, frame and post are ordinary scene geometry made by the
/// map builder; this only draws the words on the front. The words are
/// a world-space uGUI Text because the project ships no font files:
/// like the HUD, the sign asks the OS for a font when it wakes, so the
/// text objects are rebuilt on every enable and never saved.
///
/// The sign faces its own +Z. Each line is shrunk to fit the width.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class MuseumSign : MonoBehaviour
{
    [Serializable]
    public struct Line
    {
        public string text;

        /// <summary>Cap-to-descender height of the line, in metres.</summary>
        public float height;

        public Color color;

        public FontStyle style;

        public Line(string text, float height, Color color, FontStyle style = FontStyle.Normal)
        {
            this.text = text;
            this.height = height;
            this.color = color;
            this.style = style;
        }
    }


    private const string TextRootName = "Lettering";

    /// <summary>The one size every line is rasterised at.</summary>
    private const int GlyphSize = 64;

    private const float LineSpacing = 1.3f;


    [SerializeField] private bool serif = true;

    [SerializeField] private float width = 1f;

    [SerializeField] private float faceOffset = 0.03f;

    [SerializeField] private Line[] lines = new Line[0];


    private GameObject built;


    public string Title => lines != null && lines.Length > 0 ? lines[0].text : "";


    public void Configure(bool useSerif, float textWidth, float offset, Line[] text)
    {
        serif = useSerif;
        width = textWidth;
        faceOffset = offset;
        lines = text;

        Rebuild();
    }


    private void OnEnable()
    {
        Rebuild();
    }


    private void OnDisable()
    {
        Clear();
    }


    // =========================================================
    // FONTS
    // =========================================================

    private static Font serifFont, sansFont;

    private static Font SerifFont
    {
        get
        {
            if (serifFont == null)
            {
                serifFont = Font.CreateDynamicFontFromOSFont(
                    new[]
                    {
                        "Georgia", "Palatino Linotype", "Book Antiqua",
                        "Times New Roman", "Segoe UI Symbol"
                    },
                    64);
            }

            return serifFont;
        }
    }

    /// <summary>Plain workplace lettering for back of house.</summary>
    private static Font SansFont
    {
        get
        {
            if (sansFont == null)
            {
                sansFont = Font.CreateDynamicFontFromOSFont(
                    new[] { "Arial", "Segoe UI", "Helvetica", "Segoe UI Symbol" },
                    64);
            }

            return sansFont;
        }
    }


    // =========================================================
    // BUILD
    // =========================================================

    private void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);

            if (child.name == TextRootName)
            {
                Kill(child.gameObject);
            }
        }

        built = null;
    }


    private static void Kill(GameObject go)
    {
        if (Application.isPlaying)
        {
            Destroy(go);
        }
        else
        {
            DestroyImmediate(go);
        }
    }


    private void Rebuild()
    {
        Clear();

        if (lines == null || lines.Length == 0)
            return;

        Font font = serif ? SerifFont : SansFont;

        const HideFlags flags = HideFlags.DontSave | HideFlags.NotEditable;

        built = new GameObject(TextRootName);

        built.hideFlags = flags;

        Canvas canvas = built.AddComponent<Canvas>();

        canvas.renderMode = RenderMode.WorldSpace;

        RectTransform root = (RectTransform)built.transform;

        root.SetParent(transform, false);

        /*
         * A canvas reads from its -Z side; the sign faces +Z, so turn
         * the lettering round to face whoever looks at the sign. The
         * root is in metres.
         */

        root.localPosition = new Vector3(0f, 0f, faceOffset);
        root.localRotation = Quaternion.Euler(0f, 180f, 0f);
        root.localScale = Vector3.one;

        float total = 0f;

        foreach (Line line in lines)
        {
            total += line.height * LineSpacing;
        }

        root.sizeDelta = new Vector2(width, total);

        float top = total * 0.5f;

        foreach (Line line in lines)
        {
            float slot = line.height * LineSpacing;

            GameObject go = new GameObject("Line", typeof(RectTransform));

            go.hideFlags = flags;

            RectTransform rt = (RectTransform)go.transform;

            rt.SetParent(root, false);

            Text text = go.AddComponent<Text>();

            /*
             * Every line is set at one glyph size and scaled to its
             * height, so the font atlas only ever holds one size per
             * style. A line too long for the sign is scaled down.
             */

            text.font = font;
            text.text = line.text;
            text.color = line.color;
            text.fontStyle = line.style;
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = GlyphSize;
            text.resizeTextForBestFit = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;

            float metresPerPixel = line.height / GlyphSize;

            float widthPixels = width / metresPerPixel;

            float preferred = text.preferredWidth;

            if (preferred > widthPixels && preferred > 0f)
            {
                metresPerPixel *= widthPixels / preferred;
            }

            rt.sizeDelta = new Vector2(width / metresPerPixel, GlyphSize * LineSpacing);

            rt.localScale = Vector3.one * metresPerPixel;

            rt.anchoredPosition = new Vector2(0f, top - slot * 0.5f);

            top -= slot;
        }
    }
}
