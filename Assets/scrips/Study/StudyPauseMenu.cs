using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BossTemplate
{
    internal sealed class StudyPauseMenu
    {
        private const string VolumeKey = "boss-study-master-volume";
        private static readonly Color PanelColor = new Color(.065f, .09f, .08f);
        private static readonly Color Surface = new Color(.14f, .20f, .17f);
        private static readonly Color Accent = new Color(.77f, .91f, .81f);
        private readonly RectTransform overlay, card;
        private readonly Font font;
        private readonly Text deaths;
        private readonly Button resume;
        private readonly Slider volume;
        private GameObject previousSelection;

        internal static void LoadVolume() =>
            AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1));

        internal static void SaveVolume(float value)
        {
            if (PlayerPrefs.GetFloat(VolumeKey, 1) == value) return;
            PlayerPrefs.SetFloat(VolumeKey, value);
            PlayerPrefs.Save();
        }

        internal StudyPauseMenu(RectTransform parent, Font font, StudySession session)
        {
            this.font = font;
            overlay = Rect(parent, "일시정지 메뉴", Vector2.zero, Vector2.zero);
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .68f);
            card = Rect(overlay, "메뉴 패널", Vector2.zero, new Vector2(448, 390));
            card.gameObject.AddComponent<Image>().color = PanelColor;

            Label(card, "일시정지", 34, 147, Accent);
            var volumeLabel = Label(card, "음량", 22, 76, Color.white);
            volume = CreateVolumeSlider(card, new Vector2(0, 38), Surface, Accent);
            void UpdateVolume(float value)
            {
                AudioListener.volume = value;
                volumeLabel.text = $"음량  {Mathf.RoundToInt(value * 100)}%";
            }
            UpdateVolume(AudioListener.volume);
            volume.onValueChanged.AddListener(UpdateVolume);
            deaths = Label(card, "죽은 횟수", 22, -19, Color.white);
            var menu = CreateButton("메뉴로 돌아가기", -82, Surface, Color.white);
            menu.onClick.AddListener(session.ReturnToMenu);
            resume = CreateButton("게임으로 돌아가기", -144, Accent, PanelColor);
            resume.onClick.AddListener(session.TogglePause);
            SetNavigation(volume, resume, menu);
            SetNavigation(menu, volume, resume);
            SetNavigation(resume, menu, volume);
            overlay.gameObject.SetActive(false);
        }

        internal void Open(int count)
        {
            var available = ((RectTransform)overlay.parent).rect;
            card.localScale = Vector3.one * Mathf.Min(1, available.width / 480, available.height / 420);
            deaths.text = $"죽은 횟수  {count}회";
            volume.SetValueWithoutNotify(AudioListener.volume);
            overlay.SetAsLastSibling();
            var events = EventSystem.current;
            previousSelection = events ? events.currentSelectedGameObject : null;
            overlay.gameObject.SetActive(true);
            if (events) events.SetSelectedGameObject(resume.gameObject);
        }

        internal void Close()
        {
            SaveVolume(volume.value);
            overlay.gameObject.SetActive(false);
            var events = EventSystem.current;
            if (events)
                events.SetSelectedGameObject(previousSelection && previousSelection.activeInHierarchy ? previousSelection : null);
        }

        internal static Slider CreateVolumeSlider(RectTransform parent, Vector2 position, Color surface, Color accent)
        {
            var area = Rect(parent, "음량 조절", position, new Vector2(332, 40));
            var track = Rect(area, "Track", Vector2.zero, new Vector2(312, 8));
            track.gameObject.AddComponent<Image>().color = surface;
            var fill = Rect(track, "Fill", Vector2.zero, Vector2.zero);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.gameObject.AddComponent<Image>().color = accent;
            var travel = Rect(area, "Handle Area", Vector2.zero, new Vector2(312, 20));
            var handle = Rect(travel, "Handle", Vector2.zero, new Vector2(20, 20));
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = accent;
            area.gameObject.AddComponent<Image>().color = Color.clear;
            var slider = area.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.SetValueWithoutNotify(AudioListener.volume);
            return slider;
        }

        private Button CreateButton(string title, float y, Color background, Color foreground)
        {
            var rect = Rect(card, title, new Vector2(0, y), new Vector2(332, 48));
            var image = rect.gameObject.AddComponent<Image>();
            image.color = background;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            Label(rect, title, 22, 0, foreground);
            return button;
        }

        private static void SetNavigation(Selectable control, Selectable above, Selectable below) =>
            control.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit, selectOnUp = above, selectOnDown = below
            };

        private Text Label(RectTransform parent, string title, int size, float y, Color color)
        {
            var rect = Rect(parent, title, new Vector2(0, y), new Vector2(332, 52));
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.text = title;
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
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
