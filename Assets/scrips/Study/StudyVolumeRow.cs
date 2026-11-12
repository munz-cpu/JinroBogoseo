using UnityEngine;
using UnityEngine.UI;

namespace BossTemplate
{
    internal sealed class StudyVolumeRow
    {
        private readonly Text label;
        private readonly string title;
        private readonly StudyAudioChannel channel;

        internal Slider Slider { get; }

        internal StudyVolumeRow(Text label, Slider slider, string title, StudyAudioChannel channel)
        {
            this.label = label;
            this.title = title;
            this.channel = channel;
            Slider = slider;
            slider.gameObject.name = title + " 조절";
            slider.onValueChanged.AddListener(ChangeVolume);
            Refresh();
        }

        internal void Refresh()
        {
            float value = StudyAudioVolume.Get(channel);
            Slider.SetValueWithoutNotify(value);
            UpdateLabel(value);
        }

        internal void SetVisible(bool visible)
        {
            label.gameObject.SetActive(visible);
            Slider.gameObject.SetActive(visible);
        }

        private void ChangeVolume(float value)
        {
            StudyAudioVolume.Set(channel, value);
            UpdateLabel(value);
        }

        private void UpdateLabel(float value) => label.text = $"{title}  {Mathf.RoundToInt(value * 100)}%";

        internal static void SetNavigation(Selectable control, Selectable above, Selectable below) =>
            control.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit, selectOnUp = above, selectOnDown = below
            };
    }
}
