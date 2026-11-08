using System.Collections;
using UnityEngine;

namespace BossTemplate
{
    public sealed class ArenaBoss : MonoBehaviour
    {
        [SerializeField, InspectorName("공격 간격")] private float attackInterval = 1.3f;
        [SerializeField, InspectorName("공격 예고 시간")] private float warningTime = .8f;
        [SerializeField] private SpriteRenderer appearance;
        [SerializeField] private Sprite[] idleFrames, chargeFrames, shootFrames, slamFrames, defeatFrames;
        [Header("패턴 효과음")]
        [SerializeField, InspectorName("부채꼴 탄막 예고")] private AudioClip spreadWarningSound;
        [SerializeField, InspectorName("부채꼴 탄막 발사")] private AudioClip spreadSound;
        [SerializeField, InspectorName("지면 강타 예고")] private AudioClip slamWarningSound;
        [SerializeField, InspectorName("지면 강타 충격")] private AudioClip slamSound;
        [SerializeField, InspectorName("돌진 예고")] private AudioClip dashWarningSound;
        [SerializeField, InspectorName("돌진 시작")] private AudioClip dashSound;
        [SerializeField, InspectorName("연속 주먹 예고")] private AudioClip punchWarningSound;
        [SerializeField, InspectorName("주먹 타격")] private AudioClip punchSound;
        [SerializeField, InspectorName("낙석 예고")] private AudioClip rainWarningSound;
        [SerializeField, InspectorName("낙석 발사")] private AudioClip rainLaunchSound;
        [SerializeField, InspectorName("물수제비 예고")] private AudioClip skipWarningSound;
        [SerializeField, InspectorName("물수제비 발사")] private AudioClip skipSound;
        [SerializeField, InspectorName("내려찍기 순간이동")] private AudioClip vanishSound;
        private const float FramesPerSecond = 12;
        private const float CloseRange = 5.5f;
        private int nextStrike;
        internal bool Hidden { get; private set; }
        private BossArena arena;
        private SpriteRenderer body;
        private Color baseColor;
        private Sprite[] motion;
        private float motionStart, motionSpeed;
        private bool loopMotion;
        private Vector3 neutralPosition, warningOffset;
        private Quaternion neutralRotation;
        private float warningTilt, poseDuration;
        public FighterHealth Health { get; private set; }
        public bool Enraged => Health.Current <= Health.Maximum * .5f;
        public string Intent { get; private set; } = "전투 준비";
        public void Initialize(BossArena owner)
        {
            arena = owner;
            Health = GetComponent<FighterHealth>();
            body = GetComponent<SpriteRenderer>();
            baseColor = body.color;
            if (appearance)
            {
                neutralPosition = appearance.transform.localPosition;
                neutralRotation = appearance.transform.localRotation;
            }
        }

        public void ResetState()
        {
            StopAllCoroutines(); body.color = baseColor; Intent = "전투 준비";
            SetHidden(false);
            nextStrike = 0;
            Face(arena.Player.transform.position.x - transform.position.x);
            PlayMotion(idleFrames, loop: true);
            StartCoroutine(Fight());
        }

        private void Face(float horizontal)
        {
            if (appearance && horizontal != 0) appearance.flipX = horizontal < 0;
        }

        private void PlayMotion(Sprite[] frames, float speed = 1, bool loop = false)
        {
            if (poseDuration > 0 && appearance)
                appearance.transform.SetLocalPositionAndRotation(neutralPosition, neutralRotation);
            poseDuration = 0;
            motion = frames;
            motionStart = Time.time;
            motionSpeed = speed;
            loopMotion = loop;
        }

        private void LateUpdate()
        {
            if (!appearance || !Health) return;
            if (Health.Dead && motion != defeatFrames) PlayMotion(defeatFrames);
            if (poseDuration > 0)
            {
                float pose = Mathf.SmoothStep(0, 1, (Time.time - motionStart) / (poseDuration * .55f));
                appearance.transform.SetLocalPositionAndRotation(neutralPosition + warningOffset * pose,
                    neutralRotation * Quaternion.Euler(0, 0, warningTilt * pose));
            }
            if (motion == null || motion.Length == 0) return;
            int frame = Mathf.FloorToInt((Time.time - motionStart) * motionSpeed * FramesPerSecond);
            if (!loopMotion && frame >= motion.Length && !Health.Dead)
            {
                PlayMotion(idleFrames, loop: true);
                if (motion == null || motion.Length == 0) return;
                frame = 0;
            }
            appearance.sprite = motion[loopMotion ? frame % motion.Length : Mathf.Min(frame, motion.Length - 1)];
        }

        private IEnumerator Fight()
        {
            yield return new WaitForSeconds(1.5f);
            int nextPattern = 0;
            while (!arena.Finished)
            {
                int pattern = nextPattern++ % 6;
                bool close = Mathf.Abs(PlayerPosition.x - transform.position.x) < CloseRange;
                if ((pattern == 0 && close) || (pattern == 1 && !close)) continue;

                switch (pattern)
                {
                    case 0: yield return Dash(); break;
                    case 1: yield return PunchCombo(); break;
                    case 2: yield return RockRain(); break;
                    case 3: yield return Spread(); break;
                    case 4: yield return SkippingRocks(); break;
                    case 5: yield return TeleportSlam(); break;
                }
                if (arena.Finished) break;
                PlayMotion(idleFrames, loop: true);
                Intent = "다음 공격 준비";
                yield return new WaitForSeconds(attackInterval);
            }
            Intent = "전투 종료";
            if (!Health.Dead) PlayMotion(idleFrames, loop: true);
        }

        private Vector2 PlayerPosition => arena.Player.transform.position;
        private float StandingY => arena.GroundY + body.bounds.extents.y;
        private float DirectionToPlayer => PlayerPosition.x < transform.position.x ? -1 : 1;

        private float ClampX(float x)
        {
            Bounds bounds = arena.GroundBounds;
            return Mathf.Clamp(x, bounds.min.x + body.bounds.extents.x, bounds.max.x - body.bounds.extents.x);
        }

        private void Warn(string intent, AudioClip sound, Vector2 offset, float tilt, float duration)
        {
            Intent = intent;
            Face(DirectionToPlayer);
            PlayMotion(chargeFrames, loop: true);
            warningOffset = new Vector3(offset.x * DirectionToPlayer, offset.y, 0);
            warningTilt = tilt * DirectionToPlayer;
            poseDuration = duration;
            arena.PlayEffect(sound);
        }

        private void SetHidden(bool hidden)
        {
            Hidden = hidden;
            SpriteRenderer visibleBody = appearance ? appearance : body;
            visibleBody.enabled = !hidden;
        }

        private IEnumerator Dash()
        {
            Warn("돌진 준비", dashWarningSound, new Vector2(-.32f, -.18f), 13, warningTime);
            float direction = DirectionToPlayer;
            float retreatX = ClampX(transform.position.x - direction * 2.4f);
            while (!arena.Finished && !Mathf.Approximately(transform.position.x, retreatX))
            {
                float x = Mathf.MoveTowards(transform.position.x, retreatX, Time.deltaTime * 5);
                transform.position = new Vector2(x, StandingY);
                yield return null;
            }
            float destination = ClampX(transform.position.x + direction * arena.GroundBounds.size.x);
            float start = transform.position.x;
            arena.Flash(new Vector2((start + destination) / 2, arena.GroundY + .55f),
                new Vector2(Mathf.Abs(destination - start), 1.1f), new Color(1, .65f, .2f, .26f), warningTime);
            yield return new WaitForSeconds(warningTime);
            if (arena.Finished) yield break;

            Intent = "돌진";
            arena.PlayEffect(dashSound);
            PlayMotion(idleFrames, speed: 2, loop: true);
            int strike = ++nextStrike;
            while (!arena.Finished && !Mathf.Approximately(transform.position.x, destination))
            {
                Vector2 previous = transform.position;
                Vector2 next = new Vector2(Mathf.MoveTowards(previous.x, destination, Time.deltaTime * 27), StandingY);
                transform.position = next;
                bool hitRock = arena.StrikeRocks(previous + Vector2.down, next + Vector2.down, 1.4f, strike);
                bool hitPlayer = !float.IsPositiveInfinity(ArenaProjectile.ContactTime(previous, next, PlayerPosition, 1.65f))
                    && arena.Player.Health.Hit(20, .8f);
                if (hitRock || hitPlayer || next.x == destination)
                    arena.ShakeImpact(next, Vector2.right * direction * .23f);
                yield return null;
            }
        }

        private IEnumerator PunchCombo()
        {
            Warn("연속 주먹 준비", punchWarningSound, new Vector2(-.22f, .08f), 21, warningTime * .6f);
            yield return new WaitForSeconds(warningTime * .6f);
            for (int i = 0; i < 5 && !arena.Finished; i++)
            {
                float direction = DirectionToPlayer;
                Face(direction);
                PlayMotion(slamFrames, speed: 3);
                yield return new WaitForSeconds(.10f);
                if (arena.Finished) yield break;
                Intent = $"연속 주먹 {i + 1}/5";
                arena.PlayEffect(punchSound);
                Vector2 origin = (Vector2)transform.position + Vector2.right * direction * .7f;
                Vector2 end = origin + Vector2.right * direction * 3;
                arena.Flash((origin + end) / 2, new Vector2(3, 1.45f), new Color(.92f, .7f, .45f, .6f), .09f);
                arena.StrikeRocks(origin + Vector2.down, end + Vector2.down, .85f);
                if (!float.IsPositiveInfinity(ArenaProjectile.ContactTime(origin, end, PlayerPosition, 1))
                    && arena.Player.Health.Hit(8, .09f))
                    arena.ShakeImpact(PlayerPosition, Vector2.right * direction * .095f);
                yield return new WaitForSeconds(.09f);
            }
        }

        private IEnumerator RockRain()
        {
            const float fallHeight = 16;
            const float fallSpeed = 11;
            Warn("낙석 준비", rainWarningSound, new Vector2(0, .22f), 24, warningTime);
            yield return new WaitForSeconds(warningTime);
            if (arena.Finished) yield break;
            Intent = "낙석";
            PlayMotion(shootFrames);
            arena.PlayEffect(rainLaunchSound);
            arena.SpawnRock((Vector2)transform.position + Vector2.up, Vector2.up * 25, 0, RockFlight.Launch);
            yield return new WaitForSeconds(.65f);
            float center = PlayerPosition.x;
            for (int i = 0; i < 9 && !arena.Finished; i++)
            {
                float x = ClampX(center + Random.Range(-9f, 9f));
                arena.Flash(new Vector2(x, arena.GroundY + .1f), new Vector2(1.35f, .2f),
                    new Color(1, .45f, .2f, .65f), fallHeight / fallSpeed);
                arena.SpawnRock(new Vector2(x, arena.GroundY + fallHeight), Vector2.down * fallSpeed, 14, RockFlight.Falling);
                yield return new WaitForSeconds(.12f);
            }
        }

        private IEnumerator Spread()
        {
            Warn("부채꼴 투석 준비", spreadWarningSound, new Vector2(.30f, .025f), -11, warningTime);
            yield return new WaitForSeconds(warningTime);
            for (int volley = 0; volley < 4 && !arena.Finished; volley++)
            {
                Intent = $"부채꼴 투석 {volley + 1}/4";
                Face(DirectionToPlayer);
                PlayMotion(shootFrames, speed: 2);
                yield return new WaitForSeconds(.14f);
                if (arena.Finished) yield break;
                arena.PlayEffect(spreadSound);
                Vector2 origin = transform.position;
                Vector2 aim = (PlayerPosition - origin).normalized;
                if (aim == Vector2.zero) aim = Vector2.right * DirectionToPlayer;
                for (int i = -2; i <= 2; i++)
                {
                    Vector2 direction = Quaternion.Euler(0, 0, i * 13) * aim;
                    arena.SpawnRock(origin, direction * 8, 11);
                }
                yield return new WaitForSeconds(.26f);
            }
        }

        private IEnumerator SkippingRocks()
        {
            Warn("물수제비 준비", skipWarningSound, new Vector2(.22f, -.25f), -18, warningTime);
            yield return new WaitForSeconds(warningTime);
            float direction = DirectionToPlayer;
            Face(direction);
            for (int i = 0; i < 5 && !arena.Finished; i++)
            {
                Intent = $"물수제비 {i + 1}/5";
                PlayMotion(shootFrames, speed: 2.5f);
                yield return new WaitForSeconds(.11f);
                if (arena.Finished) yield break;
                arena.PlayEffect(skipSound);
                arena.SpawnRock(transform.position, new Vector2(direction * 4.5f, 3.5f), 12, RockFlight.Skipping);
                if (i < 4) yield return new WaitForSeconds(.74f);
            }
        }

        private IEnumerator TeleportSlam()
        {
            Warn("순간이동 준비", slamWarningSound, new Vector2(0, .35f), 0, warningTime * .5f);
            yield return new WaitForSeconds(warningTime * .5f);
            if (arena.Finished) yield break;
            Intent = "순간이동";
            arena.PlayEffect(vanishSound);
            SetHidden(true);
            yield return new WaitForSeconds(.45f);

            SetHidden(false);
            if (arena.Finished) yield break;
            float x = ClampX(PlayerPosition.x);
            float floor = StandingY;
            transform.position = new Vector2(x, Mathf.Max(floor + 5, PlayerPosition.y + 4));
            Intent = "내려찍기";
            PlayMotion(slamFrames);
            arena.Flash(new Vector2(x, arena.GroundY + 2), new Vector2(3, 4),
                new Color(1, .24f, .25f, .28f), .65f);
            yield return new WaitForSeconds(.3f);
            while (!arena.Finished && transform.position.y > floor)
            {
                Vector2 previous = transform.position;
                Vector2 next = new Vector2(x, Mathf.MoveTowards(previous.y, floor, Time.deltaTime * 27));
                transform.position = next;
                if (!float.IsPositiveInfinity(ArenaProjectile.ContactTime(previous, next, PlayerPosition, 1.6f)))
                    arena.Player.Health.Hit(24, .8f);
                yield return null;
            }
            transform.position = new Vector2(x, floor);
            if (arena.Finished) yield break;
            arena.PlayEffect(slamSound);
            Vector2 impact = new Vector2(x, arena.GroundY + .4f);
            arena.ShakeImpact(impact, Vector2.down * .32f);
            arena.Flash(impact, new Vector2(3.6f, .9f), new Color(1, .35f, .3f, .85f), .23f);
            arena.StrikeRocks(impact, impact, 1.8f);
            if (Vector2.Distance(PlayerPosition, impact) <= 1.8f) arena.Player.Health.Hit(24, .8f);
        }
    }
}
