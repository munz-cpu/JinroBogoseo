using System;
using UnityEngine;

namespace BossTemplate
{
    internal enum StudyAudioChannel { Master, Music, Effects }

    internal static class StudyAudioVolume
    {
        private static readonly string[] Keys =
        {
            "boss-study-master-volume", "boss-study-music-volume", "boss-study-effects-volume"
        };

        internal static event Action ChannelsChanged;

        internal static float Get(StudyAudioChannel channel) =>
            Mathf.Clamp01(PlayerPrefs.GetFloat(Keys[(int)channel], 1));

        internal static void Set(StudyAudioChannel channel, float value)
        {
            value = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(Keys[(int)channel], value);
            if (channel == StudyAudioChannel.Master) LoadMaster();
            else ChannelsChanged?.Invoke();
        }

        internal static void LoadMaster() => AudioListener.volume = Get(StudyAudioChannel.Master);
    }
}
