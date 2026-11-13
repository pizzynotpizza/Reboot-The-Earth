using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace RebootEarth
{
    /// <summary>Small runtime uGUI builder (Screen Space Overlay, 1920x1080 reference).</summary>
    public class UIKit
    {
        public readonly Canvas canvas;
        public readonly RectTransform root;
        readonly MonoBehaviour host;

        public UIKit(MonoBehaviour host, string name = "UI")
        {
            this.host = host;
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var sc = go.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            root = (RectTransform)go.transform;
            go.AddComponent<FontBinder>();
        }

        /// <summary>Wrap a canvas that was already built (baked into the scene).</summary>
        public UIKit(MonoBehaviour host, Canvas existing)
        {
            this.host = host;
            canvas = existing;
            root = (RectTransform)existing.transform;
            var fb = existing.GetComponent<FontBinder>();
            if (fb == null) fb = existing.gameObject.AddComponent<FontBinder>();
            fb.Bind();
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size,
                                         Vector2? pivot = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        public Image Panel(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color color,
                           bool rounded = true)
        {
            var rt = Rect(parent ?? root, name, anchor, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            if (rounded) { img.sprite = Art.Rounded; img.type = Image.Type.Sliced; }
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public Text Label(Transform parent, string text, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize,
                          Color color, TextAnchor align = TextAnchor.MiddleCenter, bool outline = true)
        {
            var rt = Rect(parent ?? root, "Label", anchor, pos, size);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Art.Font;
            t.text = text;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            t.lineSpacing = 1.1f;
            if (outline)
            {
                var o = rt.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0, 0, 0, 0.65f);
                o.effectDistance = new Vector2(2, -2);
            }
            return t;
        }

        /// <summary>Horizontal bar. Returns the fill image (set fillAmount 0..1).</summary>
        public Image Bar(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color fill, Color back)
        {
            var bg = Panel(parent, "Bar", anchor, pos, size, back);
            var f = Panel(bg.transform, "Fill", new Vector2(0.5f, 0.5f), Vector2.zero, size - new Vector2(8, 8), fill, false);
            f.sprite = Art.Square;
            f.type = Image.Type.Filled;
            f.fillMethod = Image.FillMethod.Horizontal;
            f.fillOrigin = 0;
            f.fillAmount = 0f;
            return f;
        }

        /// <summary>Vertical bar (fills bottom to top).</summary>
        public Image VBar(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color fill, Color back)
        {
            var f = Bar(parent, anchor, pos, size, fill, back);
            f.fillMethod = Image.FillMethod.Vertical;
            f.fillOrigin = 0;
            return f;
        }

        /// <summary>Full screen black fade (alpha 0..1).</summary>
        public Image Fader()
        {
            var img = Panel(root, "Fader", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0, 0, 0, 1), false);
            Stretch(img.rectTransform);
            img.transform.SetAsLastSibling();
            return img;
        }

        public IEnumerator Fade(Image fader, float from, float to, float time)
        {
            fader.gameObject.SetActive(true);
            fader.transform.SetAsLastSibling();
            for (float t = 0; t < time; t += Time.deltaTime)
            {
                fader.color = new Color(0, 0, 0, Mathf.Lerp(from, to, t / time));
                yield return null;
            }
            fader.color = new Color(0, 0, 0, to);
            if (to <= 0f) fader.gameObject.SetActive(false);
        }

        /// <summary>Floating text that rises and fades at a world position.</summary>
        public void Popup(Vector3 world, string text, Color color, int size = 44)
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector2 screen = cam.WorldToScreenPoint(world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out var local);
            var t = Label(root, text, new Vector2(0.5f, 0.5f), local, new Vector2(500, 90), size, color);
            t.fontStyle = FontStyle.Bold;
            host.StartCoroutine(PopupAnim(t));
        }

        IEnumerator PopupAnim(Text t)
        {
            var rt = t.rectTransform;
            Vector2 start = rt.anchoredPosition;
            Color c = t.color;
            for (float a = 0; a < 1.1f; a += Time.deltaTime)
            {
                float k = a / 1.1f;
                rt.anchoredPosition = start + new Vector2(0, 90 * k);
                rt.localScale = Vector3.one * (k < 0.15f ? Mathf.Lerp(0.5f, 1.15f, k / 0.15f) : 1f);
                t.color = new Color(c.r, c.g, c.b, 1f - Mathf.Clamp01((k - 0.6f) / 0.4f));
                yield return null;
            }
            Object.Destroy(t.gameObject);
        }

        /// <summary>Pulse (scale) for alerts.</summary>
        public static float Pulse(float speed = 6f, float amount = 0.08f) => 1f + Mathf.Sin(Time.time * speed) * amount;
    }
}
