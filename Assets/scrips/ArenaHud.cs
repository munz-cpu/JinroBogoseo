using UnityEngine;
using UnityEngine.UI;
namespace BossTemplate
{
    public sealed class ArenaHud : MonoBehaviour
    {
        [SerializeField, InspectorName("보스전 경기장")] private BossArena arena;
        [SerializeField, InspectorName("체력 및 보스 상태")] private Text status;
        [SerializeField, InspectorName("플레이어 체력바")] private Slider playerHealth;
        [SerializeField, InspectorName("보스 체력바")] private Slider bossHealth;
        [SerializeField, InspectorName("전투 결과")] private Text result;
        private void Update()
        {
            UpdateHealthBar(playerHealth, arena.Player.Health);
            UpdateHealthBar(bossHealth, arena.Boss.Health);
            status.text = arena.Boss.Intent;
            result.text = arena.Finished ? (arena.Boss.Health.Dead ? "승리! R 키로 재도전" : "패배! R 키로 재도전") : "";
        }

        private static void UpdateHealthBar(Slider slider, FighterHealth health)
        {
            slider.minValue = 0;
            slider.maxValue = health.Maximum;
            slider.SetValueWithoutNotify(health.Current);
        }
    }
}
