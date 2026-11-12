using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BossTemplate
{
    internal sealed class StudyLoadingScreen
    {
        private readonly GameObject root;
        private readonly RectTransform fill;

        internal StudyLoadingScreen(Font font)
        {
            root = new GameObject("로딩 화면", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.SetActive(false);
            Object.DontDestroyOnLoad(root);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = .5f;
            var backdrop = Rect(root.transform, "배경", Vector2.zero, Vector2.zero);
            backdrop.anchorMin = Vector2.zero;
            backdrop.anchorMax = Vector2.one;
            backdrop.gameObject.AddComponent<Image>().color = new Color32(17, 23, 20, 255);

            var caption = Rect(root.transform, "로딩 안내", new Vector2(0, 21), new Vector2(320, 44))
                .gameObject.AddComponent<Text>();
            caption.font = font ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            caption.text = "불러오는 중…";
            caption.fontSize = 26;
            caption.fontStyle = FontStyle.Bold;
            caption.alignment = TextAnchor.MiddleCenter;
            caption.color = new Color32(245, 240, 232, 255);
            caption.raycastTarget = false;

            var track = Rect(root.transform, "진행률", new Vector2(0, -21), new Vector2(240, 4));
            Paint(track, new Color32(50, 66, 58, 255));
            fill = Rect(track, "진행 표시", Vector2.zero, Vector2.zero);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.up;
            Paint(fill, new Color32(250, 201, 150, 255));
        }

        internal void Show()
        {
            SetProgress(0);
            root.SetActive(true);
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        }

        internal void SetProgress(float progress) =>
            fill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1);

        internal void Hide() => root.SetActive(false);

        internal void Destroy() => Object.Destroy(root);

        private static void Paint(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
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
