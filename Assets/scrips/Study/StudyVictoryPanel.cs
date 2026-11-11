using UnityEngine;
using UnityEngine.UI;

namespace BossTemplate
{
    internal sealed class StudyVictoryPanel
    {
        private static readonly Color Accent = new Color32(250, 201, 150, 255);
        private static readonly Color Ink = new Color32(245, 240, 232, 255);
        private static readonly Vector2 Size = new Vector2(356, 310);

        internal RectTransform Root { get; }
        internal RectTransform Content { get; }
        internal Text Title { get; }
        internal StudyRatingButton[] Ratings { get; } = new StudyRatingButton[5];
        internal Button Skip { get; }

        internal StudyVictoryPanel(RectTransform parent, Font font, Sprite star, Texture shade)
        {
            Root = Rect(parent, "승리 및 별점", Vector2.zero, Vector2.zero);
            Root.anchorMin = Vector2.zero;
            Root.anchorMax = Vector2.one;
            var backdrop = Root.gameObject.AddComponent<RawImage>();
            backdrop.texture = shade;
            backdrop.color = shade ? Color.white : new Color(.055f, .07f, .06f, .78f);

            Content = Rect(Root, "평가 항목", Vector2.zero, Size);
            Content.anchorMin = Content.anchorMax = new Vector2(.085f, .46f);
            Content.pivot = new Vector2(0, .5f);
            Title = LeftLabel(Content, "보스전 승리", font, 57, 109);
            Image(Content, "제목 강조선", new Vector2(-150, 66), new Vector2(56, 3), Accent);
            LeftLabel(Content, "보스전에서 느낀 성취감", font, 20, 29);

            var row = Rect(Content, "별점 선택", new Vector2(0, -30), new Vector2(Size.x, 62));

            for (int i = 0; i < Ratings.Length; i++)
            {
                var hitArea = Image(row, $"{i + 1}점", new Vector2(-150 + i * 61, 0), new Vector2(56, 62), Color.clear);
                hitArea.raycastTarget = true;
                var icon = Image(hitArea.rectTransform, "별", new Vector2(0, 6), new Vector2(35, 35), Color.white);
                icon.sprite = star;
                icon.preserveAspect = true;
                var number = Label(hitArea.rectTransform, (i + 1).ToString(), font, 13, -19, Ink);
                var button = hitArea.gameObject.AddComponent<StudyRatingButton>();
                button.targetGraphic = number;
                ApplyMenuColors(button);
                button.BindStar(icon);
                Ratings[i] = button;
            }

            var skip = Image(Content, "별점 건너뛰기", new Vector2(-73, -94), new Vector2(210, 44), Color.clear);
            skip.raycastTarget = true;
            Skip = skip.gameObject.AddComponent<Button>();
            Skip.targetGraphic = LeftLabel(skip.rectTransform, "별점 건너뛰기", font, 21, 0);
            ApplyMenuColors(Skip);
        }

        internal void Fit()
        {
            var available = ((RectTransform)Root.parent).rect;
            Content.localScale = Vector3.one * Mathf.Min(1, available.width / 520, available.height / 410);
            Root.SetAsLastSibling();
        }

        private static void ApplyMenuColors(Selectable selectable)
        {
            ColorBlock colors = selectable.colors;
            colors.highlightedColor = colors.selectedColor = Accent;
            colors.pressedColor = new Color(.73f, .53f, .37f);
            colors.fadeDuration = .13f;
            selectable.colors = colors;
        }

        private static Image Image(RectTransform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var image = Rect(parent, name, position, size).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text Label(RectTransform parent, string title, Font font, int size, float y, Color color)
        {
            var text = Rect(parent, title, new Vector2(0, y), new Vector2(parent.sizeDelta.x, size + 10))
                .gameObject.AddComponent<Text>();
            text.font = font;
            text.text = title;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            return text;
        }

        private static Text LeftLabel(RectTransform parent, string title, Font font, int size, float y)
        {
            var text = Label(parent, title, font, size, y, Ink);
            text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(42, size * 1.4f));
            text.alignment = TextAnchor.MiddleLeft;
            return text;
        }

        private static RectTransform Rect(RectTransform parent, string name, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }
    }
}
