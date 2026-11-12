using UnityEngine;

namespace BossTemplate
{
    public sealed class BattleMusic : MonoBehaviour
    {
        [SerializeField] private AudioSource opening;
        [SerializeField] private AudioSource loop;

        private void OnEnable()
        {
            if (!opening || !opening.clip || !loop || !loop.clip) return;

            double start = AudioSettings.dspTime + 0.1;
            double openingDuration = (double)opening.clip.samples / opening.clip.frequency;
            opening.PlayScheduled(start);
            loop.PlayScheduled(start + openingDuration);
        }

        private void OnDisable()
        {
            if (opening) opening.Stop();
            if (loop) loop.Stop();
        }
    }
}
