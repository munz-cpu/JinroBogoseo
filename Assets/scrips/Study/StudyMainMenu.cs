using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BossTemplate
{
    internal sealed class StudyMainMenu
    {
        private static readonly Color Ink = new Color(.035f, .049f, .056f);
        private static readonly Color Paper = new Color(.91f, .95f, .94f);
        private static readonly Color Accent = new Color(.46f, .91f, .94f);
        private static readonly Color Surface = new Color(.085f, .14f, .16f);
        private static readonly Vector2[] LayerTravel =
        {
            new Vector2(6, 3), new Vector2(12, 6), new Vector2(16, 8),
            new Vector2(22, 11), new Vector2(34, 16), new Vector2(60, 32)
        };

        private readonly StudySession session;
        private readonly Font displayFont, bodyFont;
        private readonly RectTransform[] layers = new RectTransform[LayerTravel.Length];
        private readonly Texture2D shade, glow;
        private readonly RectTransform navigation, details;
        private readonly Button play, back, backToMenu;
        private readonly Text playLabel, heading, body, volumeLabel;
        private readonly ScrollRect scroll;
        private readonly Slider volume;
        private Vector2 pointer, viewport;
        private float travelScale;
        private bool showingSettings;
        private GameObject previousSelection;

        internal RectTransform Root { get; }

        internal StudyMainMenu(RectTransform parent, Font displayFont, Font bodyFont, Sprite[] background,
            Sprite golem, StudySession session)
        {
            this.session = session;
            this.displayFont = displayFont;
            this.bodyFont = bodyFont;
            Root = Rect(parent, "메인 메뉴", Vector2.one * .5f, Vector2.zero, Vector2.zero);
            Stretch(Root);
            Paint(Root, Ink);

            layers[0] = Background("하늘", background[0], -.06f, 1.06f);
            layers[1] = Background("안개", background[1], .16f, .58f);
            glow = MakeTexture(64, 64, (x, y) =>
            {
                float radius = (new Vector2(x, y) - Vector2.one * .5f).magnitude * 2;
                float alpha = 1 - Mathf.SmoothStep(0, 1, radius);
                return new Color(.21f, .53f, .60f, .82f * alpha * alpha);
            });
            layers[2] = Rect(Root, "골렘 뒤의 빛", new Vector2(.755f, .56f), Vector2.zero, Vector2.zero);
            PaintTexture(layers[2], glow);
            layers[3] = Background("먼 실루엣", background[2], -.02f, .43f);
            layers[4] = Background("가까운 실루엣", background[3], -.10f, .26f);
            layers[5] = Rect(Root, "골렘", new Vector2(.755f, .51f), Vector2.zero, Vector2.zero);
            var figure = Paint(layers[5], Color.white);
            figure.sprite = golem;
            figure.preserveAspect = true;
            shade = MakeTexture(64, 1, (x, _) =>
                new Color(Ink.r, Ink.g, Ink.b, 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.23f, .72f, x))));
            var darkening = Rect(Root, "왼쪽 음영", Vector2.zero, Vector2.zero, Vector2.zero);
            Stretch(darkening);
            PaintTexture(darkening, shade);

            navigation = Rect(Root, "메뉴 항목", new Vector2(.085f, .47f), Vector2.zero, new Vector2(380, 1));
            var accent = Rect(navigation, "제목 강조선", Vector2.one * .5f, new Vector2(28, 75), new Vector2(56, 3));
            Paint(accent, Accent);
            var title = Label(navigation, "골렘전", displayFont, 88, new Vector2(175, 137), new Vector2(350, 116), Paper, TextAnchor.MiddleLeft);
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            play = MenuButton(navigation, "게임 시작", 0, session.OpenIntro);
            playLabel = play.GetComponentInChildren<Text>();
            var settings = MenuButton(navigation, "설정", -51, () => OpenDetails("설정", "", true));
            var stats = MenuButton(navigation, "통계", -102, () => OpenDetails("통계", Stats()));
            var terms = MenuButton(navigation, "실험 약관", -153, () => OpenDetails("실험 약관", Terms()));
            SetNavigation(play, terms, settings);
            SetNavigation(settings, play, stats);
            SetNavigation(stats, settings, terms);
            SetNavigation(terms, stats, play);

            details = Rect(Root, "메뉴 상세", new Vector2(.28f, .51f), Vector2.zero, new Vector2(516, 430));
            Paint(details, Ink);
            Paint(Rect(details, "강조선", new Vector2(0, 1), new Vector2(46, -2), new Vector2(92, 4)), Accent);
            heading = Label(details, "", displayFont, 36, new Vector2(0, 151), new Vector2(428, 55), Paper, TextAnchor.MiddleLeft);
            (scroll, body) = CreateTextArea();
            volume = StudyPauseMenu.CreateVolumeSlider(details, new Vector2(0, 11), Surface, Accent);
            volumeLabel = Label(details, "", bodyFont, 23, new Vector2(0, 59), new Vector2(332, 42), Paper, TextAnchor.MiddleLeft);
            volume.onValueChanged.AddListener(value =>
            {
                AudioListener.volume = value;
                UpdateVolumeLabel(value);
            });
            back = MenuButton(details, "돌아가기", -169, CloseDetails, -54, 23);
            SetNavigation(back, volume, volume);
            SetNavigation(volume, back, back);
            details.gameObject.SetActive(false);

            backToMenu = MenuButton(parent, "메뉴로 돌아가기", 31, session.BackToTitle, 119, 19, 180);
            var backRect = (RectTransform)backToMenu.transform;
            backRect.anchorMin = backRect.anchorMax = Vector2.zero;
            backToMenu.gameObject.SetActive(false);
        }

        internal void Show()
        {
            RefreshPlayLabel();
            CloseDetails();
            if (EventSystem.current) EventSystem.current.firstSelectedGameObject = play.gameObject;
            Update();
        }

        internal void ShowBack(bool show)
        {
            backToMenu.gameObject.SetActive(show);
            if (show) backToMenu.transform.SetAsLastSibling();
        }

        internal void RefreshPlayLabel() => playLabel.text = session.HasContinuation ? "이어하기" : "게임 시작";

        internal void Update()
        {
            var screen = Root.rect;
            if (screen.width <= 0 || screen.height <= 0) return;
            Vector2 target = Vector2.zero;
            if (Mouse.current != null)
            {
                Vector2 position = Mouse.current.position.ReadValue();
                target = new Vector2(Mathf.Clamp(position.x / Mathf.Max(1, Screen.width) * 2 - 1, -1, 1),
                    Mathf.Clamp(position.y / Mathf.Max(1, Screen.height) * 2 - 1, -1, 1));
            }
            pointer = Vector2.Lerp(pointer, target, 1 - Mathf.Exp(-8 * Time.unscaledDeltaTime));
            if (viewport != screen.size)
            {
                viewport = screen.size;
                travelScale = Mathf.Min(1, Mathf.Min(screen.height / 660, screen.width / 1050));
                layers[2].sizeDelta = new Vector2(screen.width * .78f, screen.height * 1.2f);
                float figure = Mathf.Min(screen.height * .78f, screen.width * .47f);
                layers[5].sizeDelta = Vector2.one * figure;
                navigation.localScale = Vector3.one * travelScale;
                float detailScale = Mathf.Min(1, Mathf.Min(screen.height / 500, screen.width / 630));
                details.localScale = Vector3.one * detailScale;
                details.anchorMin = details.anchorMax = new Vector2(Mathf.Max(.28f, 272 * detailScale / screen.width), .51f);
            }
            for (int i = 0; i < layers.Length; i++)
                layers[i].anchoredPosition = Vector2.Scale(pointer, LayerTravel[i]) * travelScale;
        }

        internal void CloseDetails()
        {
            if (!details.gameObject.activeSelf) return;
            if (showingSettings) StudyPauseMenu.SaveVolume(volume.value);
            details.gameObject.SetActive(false);
            navigation.gameObject.SetActive(true);
            if (EventSystem.current)
                EventSystem.current.SetSelectedGameObject(previousSelection ? previousSelection : play.gameObject);
        }

        internal void Destroy()
        {
            if (showingSettings && details && details.gameObject.activeSelf) StudyPauseMenu.SaveVolume(volume.value);
            Object.Destroy(shade);
            Object.Destroy(glow);
        }

        private void OpenDetails(string title, string text, bool settings = false)
        {
            previousSelection = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            showingSettings = settings;
            heading.text = title;
            navigation.gameObject.SetActive(false);
            scroll.gameObject.SetActive(!settings);
            volume.gameObject.SetActive(settings);
            volumeLabel.gameObject.SetActive(settings);
            details.gameObject.SetActive(true);
            if (settings)
            {
                volume.SetValueWithoutNotify(AudioListener.volume);
                UpdateVolumeLabel(volume.value);
            }
            else
            {
                body.text = text;
                body.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(226, body.preferredHeight + 12));
                scroll.StopMovement();
                scroll.verticalNormalizedPosition = 1;
            }
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(back.gameObject);
        }

        private void UpdateVolumeLabel(float value) => volumeLabel.text = $"전체 음량   {Mathf.RoundToInt(value * 100)}%";

        private string Stats()
        {
            string totals = $"도전 횟수   {session.Attempts}회\n죽은 횟수   {session.Deaths}회";
            var last = session.LastAttempt;
            if (last == null) return totals + "\n\n아직 완료된 도전 기록이 없습니다.";
            string progress = last.cleared ? "클리어" : last.boss_phase > 0
                ? $"페이즈 {last.boss_phase} · {last.phase_progress_percent:0.#}%" : "기록 없음";
            return $"{totals}\n\n마지막 도전   {progress}\n플레이 타임   {last.duration_seconds:0.#}초\n입은 피해   {last.damage_taken:0.#}";
        }

        private string Terms()
        {
            var protocol = session.Protocol;
            string retention = string.IsNullOrWhiteSpace(protocol.retention) ? "미설정" : protocol.retention;
            string contact = string.IsNullOrWhiteSpace(protocol.contact) ? "미설정" : protocol.contact;
            return $"{protocol.consentNotice}\n\n보관 기간   {retention}\n문의   {contact}";
        }

        private (ScrollRect, Text) CreateTextArea()
        {
            var view = Rect(details, "본문", Vector2.one * .5f, new Vector2(0, -16), new Vector2(428, 226));
            Paint(view, Color.clear).raycastTarget = true;
            view.gameObject.AddComponent<RectMask2D>();
            var text = Label(view, "", bodyFont, 20, Vector2.zero, new Vector2(414, 226), Paper, TextAnchor.UpperLeft);
            text.lineSpacing = 1.24f;
            text.supportRichText = false;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0, 1);
            text.rectTransform.pivot = new Vector2(0, 1);
            text.rectTransform.anchoredPosition = Vector2.zero;
            var scrolling = view.gameObject.AddComponent<ScrollRect>();
            scrolling.viewport = view;
            scrolling.content = text.rectTransform;
            scrolling.horizontal = false;
            scrolling.movementType = ScrollRect.MovementType.Clamped;
            scrolling.scrollSensitivity = 32;
            return (scrolling, text);
        }

        private Button MenuButton(RectTransform parent, string title, float y, UnityEngine.Events.UnityAction action,
            float x = 155, int size = 31, float width = 310)
        {
            var rect = Rect(parent, title, Vector2.one * .5f, new Vector2(x, y), new Vector2(width, 46));
            Paint(rect, Color.clear).raycastTarget = true;
            var label = Label(rect, title, displayFont, size, Vector2.zero, rect.sizeDelta, Paper, TextAnchor.MiddleLeft);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = label;
            var colors = button.colors;
            colors.highlightedColor = Accent;
            colors.selectedColor = Accent;
            colors.pressedColor = new Color(.32f, .68f, .74f);
            colors.fadeDuration = .13f;
            button.colors = colors;
            button.onClick.AddListener(action);
            return button;
        }

        private RectTransform Background(string name, Sprite sprite, float bottom, float top)
        {
            var rect = Rect(Root, name, Vector2.one * .5f, Vector2.zero, new Vector2(100, 0));
            rect.anchorMin = new Vector2(0, bottom);
            rect.anchorMax = new Vector2(1, top);
            Paint(rect, Color.white).sprite = sprite;
            return rect;
        }

        private static void SetNavigation(Selectable control, Selectable up, Selectable down) =>
            control.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = up, selectOnDown = down };

        private static Texture2D MakeTexture(int width, int height, Func<float, float, Color> sample)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = sample((float)x / (width - 1), (float)y / Mathf.Max(1, height - 1));
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static void PaintTexture(RectTransform rect, Texture2D texture)
        {
            var image = rect.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
        }

        private static Image Paint(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text Label(RectTransform parent, string text, Font font, int size, Vector2 position,
            Vector2 bounds, Color color, TextAnchor alignment)
        {
            var label = Rect(parent, string.IsNullOrEmpty(text) ? "텍스트" : text, Vector2.one * .5f, position, bounds)
                .gameObject.AddComponent<Text>();
            label.text = text;
            label.font = font;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            return label;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static RectTransform Rect(RectTransform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }
    }
}
