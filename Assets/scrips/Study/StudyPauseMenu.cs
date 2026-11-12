using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BossTemplate
{
    internal sealed class StudyPauseMenu
    {
        private const string VolumeKey = "boss-study-master-volume";
        private static readonly Color Scrim = new Color32(17, 23, 20, 211);
        private static readonly Color Surface = new Color32(50, 66, 58, 255);
        private static readonly Color Ink = new Color32(245, 240, 232, 255);
        private static readonly Color Muted = new Color32(177, 193, 182, 255);
        private static readonly Color Accent = new Color32(250, 201, 150, 255);
        private readonly RectTransform overlay, content;
        private readonly Font displayFont;
        private readonly Text deaths, volumeLabel;
        private readonly Button resume;
        private readonly Slider volume;
        private readonly StudySlideIn slide;
        private GameObject previousSelection;

        internal static void LoadVolume() =>
            AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1));

        internal static void SaveVolume(float value)
        {
            if (PlayerPrefs.GetFloat(VolumeKey, 1) == value) return;
            PlayerPrefs.SetFloat(VolumeKey, value);
            PlayerPrefs.Save();
        }

        internal StudyPauseMenu(RectTransform parent, Font displayFont, Font bodyFont, StudySession session)
        {
            this.displayFont = displayFont;
            overlay = Rect(parent, "일시정지 메뉴", Vector2.zero, Vector2.zero);
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.gameObject.AddComponent<Image>().color = Scrim;
            content = Rect(overlay, "일시정지 항목", Vector2.zero, new Vector2(332, 384));
            content.anchorMin = content.anchorMax = new Vector2(.085f, .5f);
            content.pivot = new Vector2(0, .5f);

            Label(content, "일시정지", displayFont, 62, 144, Ink);
            var rule = Rect(content, "제목 밑줄", new Vector2(-138, 95), new Vector2(56, 3));
            var ruleImage = rule.gameObject.AddComponent<Image>();
            ruleImage.color = Accent;
            ruleImage.raycastTarget = false;

            resume = CreateButton("게임으로 돌아가기", 43, 29, Ink);
            resume.onClick.AddListener(session.TogglePause);
            var menu = CreateButton("메뉴로 돌아가기", -8, 23, Muted);
            menu.onClick.AddListener(session.ReturnToMenu);

            volumeLabel = Label(content, "음량", bodyFont, 18, -91, Ink);
            volume = CreateVolumeSlider(content, new Vector2(0, -120), Surface, Accent);
            volume.onValueChanged.AddListener(SetVolume);
            deaths = Label(content, "죽은 횟수", bodyFont, 18, -169, Muted);

            SetNavigation(resume, volume, menu);
            SetNavigation(menu, resume, volume);
            SetNavigation(volume, menu, resume);
            slide = new StudySlideIn(overlay, content, playbackSpeed: StudySlideIn.FastPlayback);
            StudyTextHover.Attach(resume);
            StudyTextHover.Attach(menu);
            overlay.gameObject.SetActive(false);
        }

        internal void Open(int count)
        {
            var available = ((RectTransform)overlay.parent).rect;
            content.localScale = Vector3.one * Mathf.Min(1, available.width / 480, available.height / 440);
            deaths.text = $"죽은 횟수  {count}회";
            volume.SetValueWithoutNotify(AudioListener.volume);
            SetVolume(AudioListener.volume);
            overlay.SetAsLastSibling();
            var events = EventSystem.current;
            previousSelection = events ? events.currentSelectedGameObject : null;
            overlay.gameObject.SetActive(true);
            slide.Restart();
            slide.Advance();
            if (events) events.SetSelectedGameObject(resume.gameObject);
        }

        internal void Advance() => slide.Advance();

        internal void Close()
        {
            SaveVolume(volume.value);
            overlay.gameObject.SetActive(false);
            slide.Finish();
            var events = EventSystem.current;
            if (events)
                events.SetSelectedGameObject(previousSelection && previousSelection.activeInHierarchy ? previousSelection : null);
        }

        private void SetVolume(float value)
        {
            AudioListener.volume = value;
            volumeLabel.text = $"음량  {Mathf.RoundToInt(value * 100)}%";
        }

        private static Slider CreateVolumeSlider(RectTransform parent, Vector2 position, Color surface, Color accent)
        {
            var area = Rect(parent, "음량 조절", position, new Vector2(332, 40));
            var track = Rect(area, "Track", Vector2.left * 10, new Vector2(312, 3));
            var trackImage = track.gameObject.AddComponent<Image>();
            trackImage.color = surface;
            RoundedUi.Apply(trackImage, 2, 0);
            var fill = Rect(track, "Fill", Vector2.zero, Vector2.zero);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.color = accent;
            RoundedUi.Apply(fillImage, 2, 0);
            var travel = Rect(area, "Handle Area", Vector2.left * 10, new Vector2(312, 20));
            var handle = Rect(travel, "Handle", Vector2.zero, new Vector2(11, 11));
            handle.localRotation = Quaternion.Euler(0, 0, 45);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = accent;
            RoundedUi.Apply(handleImage, 2, .75f);
            area.gameObject.AddComponent<Image>().color = Color.clear;
            var slider = area.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.SetValueWithoutNotify(AudioListener.volume);
            return slider;
        }

        private Button CreateButton(string title, float y, int size, Color foreground)
        {
            var rect = Rect(content, title, new Vector2(0, y), new Vector2(332, 48));
            rect.gameObject.AddComponent<Image>().color = Color.clear;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = Label(rect, title, displayFont, size, 0, Color.white);
            var colors = button.colors;
            colors.normalColor = foreground;
            colors.highlightedColor = Accent;
            colors.selectedColor = Accent;
            colors.pressedColor = Ink;
            button.colors = colors;
            return button;
        }

        private static void SetNavigation(Selectable control, Selectable above, Selectable below) =>
            control.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit, selectOnUp = above, selectOnDown = below
            };

        private static Text Label(RectTransform parent, string title, Font font, int size, float y, Color color)
        {
            var rect = Rect(parent, title, new Vector2(0, y), new Vector2(332, Mathf.Max(52, size * 1.35f)));
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontStyle = FontStyle.Bold;
            text.text = title;
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = color;
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
