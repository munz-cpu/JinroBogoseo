using UnityEngine;
using UnityEngine.UI;

namespace BossTemplate
{
    internal sealed class StudyVictoryPanel
    {
        private static readonly Color Gold = new Color(.97f, .76f, .40f);
        private static readonly Color Cream = new Color(1, .94f, .80f);
        private static readonly Color Surface = new Color(.20f, .15f, .12f);
        private static readonly Vector2 Size = new Vector2(414, 226);

        internal RectTransform Root { get; }
        internal Text Title { get; }
        internal StudyRatingButton[] Ratings { get; } = new StudyRatingButton[5];
        internal Button Skip { get; }

        internal StudyVictoryPanel(RectTransform parent, Font font, Sprite star)
        {
            Root = Rect(parent, "승리 및 별점", Vector2.zero, Size);
            Root.anchorMin = Root.anchorMax = new Vector2(.5f, .30f);
            Image(Root, "그림자", new Vector2(0, -6), Size, new Color(0, 0, 0, .44f));
            Image(Root, "테두리", Vector2.zero, Size, Gold);
            Image(Root, "카드", Vector2.zero, Size - Vector2.one * 4, new Color(.095f, .067f, .052f));

            Title = Label(Root, "보스전 승리", font, 36, 71, Gold);
            Title.fontStyle = FontStyle.Bold;
            Label(Root, "보스전에서 느낀 성취감", font, 18, 33, Cream);

            for (int i = 0; i < Ratings.Length; i++)
            {
                var tile = Image(Root, $"{i + 1}점", new Vector2((i - 2) * 64, -17), new Vector2(57, 62), Surface);
                tile.raycastTarget = true;
                var icon = Image(tile.rectTransform, "별", new Vector2(0, 6), new Vector2(31, 31), Color.white);
                icon.sprite = star;
                icon.preserveAspect = true;
                Label(tile.rectTransform, (i + 1).ToString(), font, 13, -19, Cream);
                var button = tile.gameObject.AddComponent<StudyRatingButton>();
                button.targetGraphic = tile;
                button.BindStar(icon);
                Ratings[i] = button;
            }

            var skip = Image(Root, "별점 건너뛰기", new Vector2(0, -78), new Vector2(158, 37), Surface);
            skip.raycastTarget = true;
            Label(skip.rectTransform, "별점 건너뛰기", font, 16, 0, Cream);
            Skip = skip.gameObject.AddComponent<Button>();
            Skip.targetGraphic = skip;
        }

        internal void Fit()
        {
            var available = ((RectTransform)Root.parent).rect;
            Root.localScale = Vector3.one * Mathf.Min(1, available.width / 450, available.height / 420);
            Root.SetAsLastSibling();
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
