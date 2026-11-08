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
        [SerializeField, Min(0), InspectorName("대시 속도")] private float dashSpeed = 20;
        [SerializeField, Min(0), InspectorName("대시 지속 시간")] private float dashDuration = .16f;
        [Header("재사용 대기시간(초): 대시 / 사격 / 궁극기")]
        [SerializeField, InspectorName("재사용 대기시간")] private float[] cooldowns = { .65f, .1f, 4 };
        [Header("재장전")]
        [SerializeField, InspectorName("탄창 사용")] private bool useMagazine = true;
        [SerializeField, Min(1), InspectorName("탄창 크기")] private int magazineSize = 40;
        [SerializeField, Min(.1f), InspectorName("재장전 시간")] private float reloadDuration = .9f;
        [Header("체력 재생")]
        [SerializeField, Min(0), InspectorName("초당 체력 회복량")] private float healthRegenPerSecond = 4;
        [SerializeField, Min(0), InspectorName("피격 후 재생 대기 시간")] private float healthRegenDelay = 3;
        [Header("효과음")]
        [SerializeField, InspectorName("점프")] private AudioClip jumpSound;
        [SerializeField, InspectorName("더블 점프")] private AudioClip doubleJumpSound;
        [SerializeField, InspectorName("착지")] private AudioClip landingSound;
        [SerializeField, InspectorName("발판 내려가기")] private AudioClip dropThroughSound;
        [SerializeField, InspectorName("대시")] private AudioClip dashSound;
        [SerializeField, InspectorName("원거리 사격")] private AudioClip shotSound;
        [SerializeField, InspectorName("재장전")] private AudioClip reloadSound;
        [SerializeField, InspectorName("궁극기")] private AudioClip burstSound;
        [SerializeField, InspectorName("일반 탄환 스프라이트")] private Sprite bulletSprite;
        [SerializeField, InspectorName("궁극기 탄환 스프라이트")] private Sprite burstBulletSprite;
        private readonly float[] ready = new float[3];
        private BossArena arena;
        private float vertical;
        private float dashRemaining;
        private float reloadUntil;
        private int facing = 1, jumps;
        private int lastMuzzleFrame = -1;
        private SpriteRenderer body;
        private Color baseColor;
        private Vector2 halfSize;
        private Vector2 standingHalfSize;
        private bool crouching;
        [SerializeField, InspectorName("충돌 반크기 (0이면 스프라이트 기준)")] private Vector2 collisionHalfSize;
        private PlayerSpriteAnimation visuals;
        private bool grounded;
        private ArenaPlatform standingPlatform, ignoredPlatform;
        public FighterHealth Health { get; private set; }
        internal Vector2 HurtboxHalfSize => halfSize;
        public bool UsesMagazine => useMagazine;
        public int MagazineSize => Mathf.Max(1, magazineSize);
        public int RoundsRemaining { get; private set; }
        public float Cooldown(int index) => Mathf.Max(0, ready[index] - Time.time);

        internal bool OverlapsPlantedRock(float rockX)
        {
            Vector3 position = transform.position;
            return position.y - halfSize.y < arena.GroundY + ArenaProjectile.PlantedHeight
                && Mathf.Abs(position.x - rockX) < halfSize.x + ArenaProjectile.PlantedHalfWidth;
        }

        public void Initialize(BossArena owner)
        {
            arena = owner; Health = GetComponent<FighterHealth>();
            body = GetComponent<SpriteRenderer>();
            baseColor = body.color;
            halfSize = body.bounds.extents;
            if (collisionHalfSize.x > 0 && collisionHalfSize.y > 0) halfSize = collisionHalfSize;
            standingHalfSize = halfSize;
            visuals = GetComponent<PlayerSpriteAnimation>();
        }

        public void ResetState()
        {
            vertical = 0; facing = 1; jumps = 0;
            dashRemaining = 0;
            RoundsRemaining = MagazineSize;
            reloadUntil = 0;
            lastMuzzleFrame = -1;
            crouching = false; halfSize = standingHalfSize;
            grounded = true;
            standingPlatform = null; ignoredPlatform = null;
            System.Array.Clear(ready, 0, ready.Length);
            body.color = baseColor;
            body.flipX = false;
            if (visuals) visuals.ResetState();
        }

        private void Update()
        {
            body.color = Time.time < Health.InvulnerableUntil ? Color.white : baseColor;
            bool finished = arena.Finished;
            if (!finished && Health.Current < Health.Maximum)
            {
                float regenTime = Mathf.Min(Time.deltaTime, Time.time - Health.LastDamageTime - healthRegenDelay);
                if (regenTime > 0) Health.Restore(healthRegenPerSecond * regenTime);
            }
            var k = Keyboard.current;
            if (finished || k == null)
            {
                if (visuals) visuals.SetState(0, grounded, vertical, facing, Health.Dead);
                return;
            }
            float axis = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1 : 0) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1 : 0);
            bool down = k.sKey.isPressed || k.downArrowKey.isPressed;
            bool jump = k.wKey.wasPressedThisFrame || k.upArrowKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame;
            if (dashRemaining <= 0 && (k.leftShiftKey.wasPressedThisFrame || k.rightShiftKey.wasPressedThisFrame) && Use(0))
            {
                if (axis != 0) facing = axis > 0 ? 1 : -1;
                dashRemaining = dashDuration;
                arena.PlayEffect(dashSound);
            }
            bool dashing = dashRemaining > 0;
            Move(axis, jump, down && !dashing, Time.deltaTime);
            if (visuals) visuals.SetState(dashing ? facing : axis, grounded, vertical, facing, false);
            if (useMagazine && RoundsRemaining == 0 && Time.time >= reloadUntil) RoundsRemaining = MagazineSize;
            bool mouseShot = Mouse.current != null && Mouse.current.leftButton.isPressed;
            if ((k.enterKey.isPressed || mouseShot) && (!useMagazine || RoundsRemaining > 0) && Use(1)
                && Shoot(new Vector2(facing * 15, 0), 7))
            {
                arena.PlayEffect(shotSound);
                if (useMagazine && --RoundsRemaining == 0)
                {
                    reloadUntil = Time.time + Mathf.Max(.1f, reloadDuration);
                    arena.PlayEffect(reloadSound);
                }
            }
            if (k.backslashKey.wasPressedThisFrame && Use(2))
            {
                arena.PlayEffect(burstSound);
                for (int i = -2; i <= 2; i++) Shoot(new Vector2(facing * 12, i * 2), 15, burstBulletSprite);
            }
        }

        private void Move(float axis, bool jump, bool down, float deltaTime)
        {
            const float fastFallSpeed = 18f;
            if (dashRemaining <= 0 && axis != 0) facing = axis > 0 ? 1 : -1;
            bool nextCrouch = down && !jump;
            if (nextCrouch != crouching)
            {
                float height = standingHalfSize.y * (nextCrouch ? 2f / 3 : 1);
                transform.position += Vector3.up * (height - halfSize.y);
                halfSize.y = height; crouching = nextCrouch;
            }
            if (visuals) visuals.SetCrouching(crouching);
            if (crouching && grounded) axis = 0;
            if (jump && down && grounded && arena.CanDropThrough(standingPlatform))
            {
                // 현재 발판만 무시하므로 바로 아래 발판이나 바닥에는 정상 착지합니다.
                ignoredPlatform = standingPlatform;
                standingPlatform = null; grounded = false;
                vertical = -2; jumps = 1;
                arena.PlayEffect(dropThroughSound);
            }
            else if (jump && jumps < 2)
            {
                vertical = jumpSpeed; jumps++;
                arena.PlayEffect(jumps == 2 ? doubleJumpSound : jumpSound);
                if (jumps == 2) arena.DoubleJumpFlash((Vector2)transform.position - Vector2.up * halfSize.y);
            }
            if (crouching && !grounded) vertical = Mathf.Min(vertical, -fastFallSpeed);
            Vector3 previous = transform.position;
            Vector3 position = previous;
            float dashStep = Mathf.Min(dashRemaining, deltaTime);
            position.x += facing * dashSpeed * dashStep + axis * moveSpeed * (deltaTime - dashStep);
            dashRemaining -= dashStep;
            vertical -= gravity * deltaTime;
            position.y += vertical * deltaTime;
            position.x = Mathf.Clamp(position.x, arena.GroundBounds.min.x + halfSize.x, arena.GroundBounds.max.x - halfSize.x);
            float unblockedX = position.x;
            position.x = arena.BlockPlantedRockSides(previous, unblockedX, halfSize);
            if (position.x != unblockedX) dashRemaining = 0;
            bool landed = arena.TryLand(previous, position, halfSize, out float landingY, out standingPlatform, ignoredPlatform);
            if (landed)
            {
                if (!grounded) arena.PlayEffect(landingSound);
                position.y = landingY; vertical = 0; jumps = 0;
            }
            else if (grounded && vertical <= 0) jumps = Mathf.Max(jumps, 1);
            grounded = landed;
            if (ignoredPlatform && position.y - halfSize.y < ignoredPlatform.Bounds.max.y - .02f)
                ignoredPlatform = null;
            transform.position = position;
            body.flipX = facing < 0;
        }

        private bool Use(int i) { if (Time.time < ready[i]) return false; ready[i] = Time.time + cooldowns[i]; return true; }
        private bool Shoot(Vector2 velocity, float damage, Sprite sprite = null)
        {
            Vector2 muzzle = new Vector2(facing * (visuals ? 1.1f : .7f), crouching ? .32f : .62f);
            Vector2 origin = (Vector2)transform.position + muzzle;
            if (!arena.SpawnProjectile(origin, velocity, damage, false, sprite ? sprite : bulletSprite)) return false;
            if (visuals) visuals.Fire();
            if (lastMuzzleFrame != Time.frameCount)
            {
                arena.GunFlash(origin, facing);
                lastMuzzleFrame = Time.frameCount;
            }
            return true;
        }
    }
}
