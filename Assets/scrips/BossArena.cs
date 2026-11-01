using UnityEngine;
using UnityEngine.InputSystem;
namespace BossTemplate
{
    public sealed class BossArena : MonoBehaviour
    {
        [SerializeField, InspectorName("바닥")] private ArenaPlatform ground;
        [SerializeField, InspectorName("착지 가능한 발판 목록 (바닥 포함)")] private ArenaPlatform[] platforms;
        public float GroundY => ground.Bounds.max.y;
        [SerializeField, InspectorName("플레이어")] private SideViewPlayer player;
        [SerializeField, InspectorName("보스")] private ArenaBoss boss;
        [SerializeField, InspectorName("경기장 카메라")] private Camera arenaCamera;
        [SerializeField, InspectorName("탄환 목록")] private ArenaProjectile[] projectiles;
        [SerializeField, InspectorName("공격 표시 목록")] private ArenaEffect[] effects;
        private Vector3 playerStart, bossStart;
        public SideViewPlayer Player => player;
        public ArenaBoss Boss => boss;
        public bool Finished => Player.Health.Dead || Boss.Health.Dead;

        public bool TryLand(Vector2 previous, Vector2 next, Vector2 halfSize, out float centerY)
        {
            return TryLand(previous, next, halfSize, out centerY, out _);
        }

        public bool CanDropThrough(ArenaPlatform platform) => platform && platform != ground;

        public bool TryLand(Vector2 previous, Vector2 next, Vector2 halfSize, out float centerY,
            out ArenaPlatform landedOn, ArenaPlatform ignored = null)
        {
            centerY = float.NegativeInfinity;
            landedOn = null;
            bool landed = false;
            foreach (var platform in platforms)
            {
                if (platform == ignored && CanDropThrough(platform)) continue;
                if (!platform || !platform.TryLand(previous, next, halfSize, out float height)) continue;
                if (height > centerY) { centerY = height; landedOn = platform; }
                landed = true;
            }
            return landed;
        }
        private void Awake()
        {
            playerStart = player.transform.position; bossStart = boss.transform.position;
            Player.Initialize(this); Boss.Initialize(this); ResetFight();
        }
        public void ResetFight()
        {
            foreach (var item in projectiles) item.gameObject.SetActive(false);
            foreach (var item in effects) item.gameObject.SetActive(false);
            Player.transform.position = playerStart; Boss.transform.position = bossStart;
            Player.Health.ResetHealth(Player.Health.Maximum); Boss.Health.ResetHealth(Boss.Health.Maximum);
            Player.ResetState(); Boss.ResetState();
        }
        private void Update()
        {
            arenaCamera.orthographicSize = Mathf.Max(6.6f, 11.8f / Mathf.Max(.1f, arenaCamera.aspect));
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) ResetFight();
        }
        public GameObject Flash(Vector2 position, Vector2 size, Color color, float lifetime)
        {
            foreach (var effect in effects)
            {
                if (effect.gameObject.activeSelf) continue;
                effect.Show(position, size, color, lifetime); return effect.gameObject;
            }
            return null;
        }
        public void SpawnProjectile(Vector2 position, Vector2 velocity, float damage, bool hostile)
        {
            foreach (var item in projectiles)
            {
                if (item.gameObject.activeSelf) continue;
                item.transform.position = position;
                item.transform.localScale = hostile ? new Vector3(.35f,.35f,1) : new Vector3(.5f,.18f,1);
                item.GetComponent<SpriteRenderer>().color = hostile ? Color.red : Color.white;
                item.Initialize(this, hostile ? Player.Health : Boss.Health, velocity, damage, hostile ? .65f : 1.35f, hostile);
                item.gameObject.SetActive(true); return;
            }
        }
    }
}
