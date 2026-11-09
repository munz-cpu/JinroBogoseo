using UnityEngine;
using UnityEngine.UI;
namespace BossTemplate
{
    public sealed class ArenaHud : MonoBehaviour
    {
        [SerializeField, InspectorName("보스전 경기장")] private BossArena arena;
        [SerializeField, InspectorName("탄창 표시")] private Text ammo;
        [SerializeField, InspectorName("플레이어 체력바")] private Slider playerHealth;
        [SerializeField, InspectorName("전투 결과")] private Text result;
        private int displayedRounds = -2, displayedCapacity;
        private void Update()
        {
            UpdateHealthBar(playerHealth, arena.Player.Health);
            int rounds = arena.Player.UsesMagazine ? arena.Player.RoundsRemaining : -1;
            int capacity = arena.Player.MagazineSize;
            if (ammo && (rounds != displayedRounds || capacity != displayedCapacity))
            {
                ammo.text = rounds switch
                {
                    -1 => string.Empty,
                    0 => "재장전 중",
                    _ => $"탄약 {rounds} / {capacity}"
                };
                displayedRounds = rounds;
                displayedCapacity = capacity;
            }
            result.text = !arena.ResearchActive && arena.Finished
                ? (arena.Player.Health.Dead ? "패배! Enter로 재도전" : "승리! Enter로 재도전")
                : "";
        }

        private static void UpdateHealthBar(Slider slider, FighterHealth health)
        {
            slider.minValue = 0;
            slider.maxValue = health.Maximum;
            slider.SetValueWithoutNotify(health.Current);
        }
    }
}
