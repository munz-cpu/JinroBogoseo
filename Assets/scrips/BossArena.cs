using UnityEngine;
using UnityEngine.InputSystem;
namespace BossTemplate
{
    public sealed class BossArena : MonoBehaviour
    {
        [SerializeField, InspectorName("바닥")] private ArenaPlatform ground;
        [SerializeField, InspectorName("착지 가능한 발판 목록 (바닥 포함)")] private ArenaPlatform[] platforms;
        public float GroundY => ground.Bounds.max.y;
        public Bounds GroundBounds => ground.Bounds;
        [SerializeField, InspectorName("플레이어")] private SideViewPlayer player;
        [SerializeField, InspectorName("보스")] private ArenaBoss boss;
        [SerializeField, InspectorName("경기장 카메라")] private Camera arenaCamera;
        [SerializeField, InspectorName("탄환 목록")] private ArenaProjectile[] projectiles;
        [SerializeField, InspectorName("공격 표시 목록")] private ArenaEffect[] effects;
        [SerializeField, InspectorName("섬광 메시")] private Mesh sparkMesh;
        [SerializeField, InspectorName("점프 링 메시")] private Mesh jumpRingMesh;
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
            Player.Initialize(this); Boss.Initialize(this);
            ResetFight();
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
        private void Burst(Mesh mesh, Vector2 position, Vector2 size, Color color, float lifetime,
            Vector2 velocity, float endScale, float angle = 0)
        {
            if (!mesh) return;
            foreach (var effect in effects)
            {
                if (effect.gameObject.activeSelf) continue;
                effect.ShowAnimated(mesh, position, size, color, lifetime, velocity, endScale, angle);
                return;
            }
        }
        public void GunFlash(Vector2 muzzle, int facing)
        {
            Burst(sparkMesh, muzzle + Vector2.right * facing * .14f, new Vector2(.65f, .35f),
                new Color(1, .78f, .25f), .09f, Vector2.right * facing * 1.5f, .15f);
            Burst(sparkMesh, muzzle - Vector2.right * facing * .3f, new Vector2(.12f, .07f),
                new Color(1, .7f, .25f), .22f, new Vector2(-facing * 2, 1.4f), .25f, 35);
        }
        public void DoubleJumpFlash(Vector2 feet)
        {
            Burst(jumpRingMesh, feet, new Vector2(.8f, .25f), new Color(.4f, .85f, 1, .9f),
                .3f, Vector2.down * .4f, 2.8f);
            for (int i = -1; i <= 1; i++)
                Burst(sparkMesh, feet + new Vector2(i * .2f, -.1f), new Vector2(.12f, .28f),
                    new Color(.7f, .95f, 1), .25f, new Vector2(i * 2, -2), .1f, i * 25);
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

