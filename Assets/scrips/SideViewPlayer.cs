using UnityEngine;
using UnityEngine.InputSystem;

namespace BossTemplate
{
    public sealed class SideViewPlayer : MonoBehaviour
    {
        [Header("이동 설정")]
        [SerializeField, InspectorName("이동 속도")] private float moveSpeed = 6;
        [SerializeField, InspectorName("점프 속도")] private float jumpSpeed = 12;
        [SerializeField, InspectorName("중력")] private float gravity = 30;
        [Header("스킬 단축키")]
        [SerializeField, InspectorName("근접 공격 키")] private Key attackKey = Key.Z;
        [SerializeField, InspectorName("원거리 공격 키")] private Key shotKey = Key.X;
        [SerializeField, InspectorName("탄막 키")] private Key burstKey = Key.C;
        [Header("WASD 조작용 공격 키")]
        [SerializeField, InspectorName("근접 공격 키 (WASD)")] private Key alternateAttackKey = Key.LeftShift;
        [SerializeField, InspectorName("원거리 공격 키 (WASD)")] private Key alternateShotKey = Key.Enter;
        [SerializeField, InspectorName("탄막 키 (WASD)")] private Key alternateBurstKey = Key.Backslash;
        [Header("재사용 대기시간(초): 근접 공격 / 원거리 공격 / 탄막")]
        [SerializeField, InspectorName("재사용 대기시간")] private float[] cooldowns = { .28f, .2f, 4 };
        private readonly float[] ready = new float[3];
        private BossArena arena;
        private float vertical;
        private int facing = 1, jumps;
        private SpriteRenderer body;
        private Color baseColor;
        private Vector2 halfSize;
        private bool grounded;
        private ArenaPlatform standingPlatform, ignoredPlatform;
        public FighterHealth Health { get; private set; }
        public float Cooldown(int index) => Mathf.Max(0, ready[index] - Time.time);

        public void Initialize(BossArena owner)
        {
            arena = owner; Health = GetComponent<FighterHealth>();
            body = GetComponent<SpriteRenderer>();
            baseColor = body.color;
            halfSize = body.bounds.extents;
        }

        public void ResetState()
        {
            vertical = 0; facing = 1; jumps = 0;
            grounded = true;
            standingPlatform = null; ignoredPlatform = null;
            System.Array.Clear(ready, 0, ready.Length);
            body.color = baseColor;
        }

        private void Update()
        {
            var k = Keyboard.current;
            if (arena.Finished || k == null) return;
            float axis = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1 : 0) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1 : 0);
            bool down = k.sKey.isPressed || k.downArrowKey.isPressed;
            bool jump = k.wKey.wasPressedThisFrame || k.upArrowKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame;
            Move(axis, jump, down, Time.deltaTime);
            if ((k[attackKey].isPressed || k[alternateAttackKey].isPressed) && Use(0))
            {
                Vector2 center = (Vector2)transform.position + Vector2.right * facing * 1.2f;
                arena.Flash(center, new Vector2(2.4f, 1.6f), new Color(.5f, 1, 1, .65f), .1f);
                if (Mathf.Abs(arena.Boss.transform.position.x - center.x) < 2.1f && Mathf.Abs(arena.Boss.transform.position.y - center.y) < 2) arena.Boss.Health.Hit(12);
            }
            if ((k[shotKey].isPressed || k[alternateShotKey].isPressed) && Use(1)) Shoot(new Vector2(facing * 15, 0), 7);
            if ((k[burstKey].wasPressedThisFrame || k[alternateBurstKey].wasPressedThisFrame) && Use(2))
                for (int i = -2; i <= 2; i++) Shoot(new Vector2(facing * 12, i * 2), 15);
        }

        private void Move(float axis, bool jump, bool down, float deltaTime)
        {
            if (axis != 0) facing = axis > 0 ? 1 : -1;
            if (jump && down && grounded && arena.CanDropThrough(standingPlatform))
            {
                // 현재 발판만 무시하므로 바로 아래 발판이나 바닥에는 정상 착지합니다.
                ignoredPlatform = standingPlatform;
                standingPlatform = null; grounded = false;
                vertical = -2; jumps = 1;
            }
            else if (jump && jumps < 2)
            { vertical = jumpSpeed; jumps++; }
            if (down && !grounded) vertical -= gravity * deltaTime;
            Vector3 previous = transform.position;
            Vector3 position = previous;
            position.x += axis * moveSpeed * deltaTime;
            vertical -= gravity * deltaTime;
            position.y += vertical * deltaTime;
            position.x = Mathf.Clamp(position.x, -10, 10);
            bool landed = arena.TryLand(previous, position, halfSize, out float landingY, out standingPlatform, ignoredPlatform);
            if (landed) { position.y = landingY; vertical = 0; jumps = 0; }
            else if (grounded && vertical <= 0) jumps = Mathf.Max(jumps, 1);
            grounded = landed;
            if (ignoredPlatform && position.y - halfSize.y < ignoredPlatform.Bounds.max.y - .02f)
                ignoredPlatform = null;
            transform.position = position;
            body.color = Time.time < Health.InvulnerableUntil ? Color.white : baseColor;
        }

        private bool Use(int i) { if (Time.time < ready[i]) return false; ready[i] = Time.time + cooldowns[i]; return true; }
        private void Shoot(Vector2 velocity, float damage) => arena.SpawnProjectile((Vector2)transform.position + Vector2.right * facing * .7f, velocity, damage, false);
    }
}
