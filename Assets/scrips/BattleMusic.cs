using UnityEngine;

namespace BossTemplate
{
    public sealed class BattleMusic : MonoBehaviour
    {
        [SerializeField] private AudioSource opening;
        [SerializeField] private AudioSource loop;
        private float openingVolume, loopVolume;

        private void Awake()
        {
            if (opening) openingVolume = opening.volume;
            if (loop) loopVolume = loop.volume;
        }

        private void OnEnable()
        {
            if (!opening || !opening.clip || !loop || !loop.clip) return;

            StudyAudioVolume.ChannelsChanged += ApplyVolume;
            ApplyVolume();
            double start = AudioSettings.dspTime + 0.1;
            double openingDuration = (double)opening.clip.samples / opening.clip.frequency;
            opening.PlayScheduled(start);
            loop.PlayScheduled(start + openingDuration);
        }

        private void OnDisable()
        {
            StudyAudioVolume.ChannelsChanged -= ApplyVolume;
            if (opening) opening.Stop();
            if (loop) loop.Stop();
        }

        private void ApplyVolume()
        {
            float value = StudyAudioVolume.Get(StudyAudioChannel.Music);
            opening.volume = openingVolume * value;
            loop.volume = loopVolume * value;
        }
    }
}
