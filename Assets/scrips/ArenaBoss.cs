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
        [SerializeField, InspectorName("지면 균열 타격")] private AudioClip fissureSound;
        [SerializeField, InspectorName("낙석 예고")] private AudioClip rainWarningSound;
        [SerializeField, InspectorName("낙석 발사")] private AudioClip rainLaunchSound;
        [SerializeField, InspectorName("물수제비 예고")] private AudioClip skipWarningSound;
        [SerializeField, InspectorName("물수제비 발사")] private AudioClip skipSound;
        [SerializeField, InspectorName("내려찍기 순간이동")] private AudioClip vanishSound;
        private const float FramesPerSecond = 12;
        private bool pauseRain;
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
            pauseRain = false;
            Face(arena.Player.transform.position.x - transform.position.x);
            PlayMotion(idleFrames, loop: true);
            StartCoroutine(Fight());
            StartCoroutine(RainWaves());
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
            yield return new WaitForSeconds(1f);
            int nextPhaseOnePattern = 0;
            int nextPhaseTwoPattern = 0;
            while (!arena.Finished)
            {
                int pattern = Enraged ? 5 + nextPhaseTwoPattern++ % 4 : nextPhaseOnePattern++ % 9;
                pauseRain = pattern < 5;
                switch (pattern)
                {
                    case 0: yield return GroundBarrage(); break;
                    case 1: yield return FaultLine(); break;
                    case 2: yield return Crossfire(); break;
                    case 3: yield return CeilingCollapse(); break;
                    case 4: yield return StoneNova(); break;
                    case 5: yield return Dash(); break;
                    case 6: yield return Spread(); break;
                    case 7: yield return SkippingRocks(); break;
                    case 8: yield return TeleportSlam(); break;
                }
                pauseRain = false;
                if (arena.Finished) break;
                PlayMotion(idleFrames, loop: true);
                Intent = "다음 공격 준비";
                yield return new WaitForSeconds(attackInterval * (Enraged ? .25f : .45f));
            }
            Intent = "전투 종료";
            if (!Health.Dead) PlayMotion(idleFrames, loop: true);
        }

        private Vector2 PlayerPosition => arena.Player.transform.position;
        private float StandingY => arena.GroundY + body.bounds.extents.y;
        internal float ProjectileHitRadius => body.bounds.extents.x * 1.35f;
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
            float y = arena.GroundY + .95f;
            float launchX = transform.position.x + direction * body.bounds.extents.x;
            arena.Flash(new Vector2((launchX + destination) * .5f, y),
                new Vector2(Mathf.Abs(destination - launchX), .24f), new Color(1, .65f, .2f, .45f), warningTime);
            yield return new WaitForSeconds(warningTime);
            if (arena.Finished) yield break;

            Intent = "돌진";
            arena.PlayEffect(dashSound);
            PlayMotion(idleFrames, speed: 2, loop: true);
            arena.SpawnRock(new Vector2(launchX, y), Vector2.right * direction * 28, 20);
            while (!arena.Finished && !Mathf.Approximately(transform.position.x, destination))
            {
                Vector2 next = new Vector2(Mathf.MoveTowards(transform.position.x, destination, Time.deltaTime * 27), StandingY);
                transform.position = next;
                if (next.x == destination)
                    arena.ShakeImpact(next, Vector2.right * direction * .23f);
                yield return null;
            }
        }

        private IEnumerator GroundBarrage()
        {
            float direction = DirectionToPlayer;
            float start = transform.position.x + direction * body.bounds.extents.x;
            float edge = direction < 0 ? arena.GroundBounds.min.x : arena.GroundBounds.max.x;
            float y = arena.GroundY + .95f;
            Warn("저공 돌탄 예고", spreadWarningSound, new Vector2(-.18f, -.12f), 12, warningTime);
            arena.Flash(new Vector2((start + edge) * .5f, y), new Vector2(Mathf.Abs(edge - start), .2f),
                new Color(1, .65f, .2f, .55f), warningTime);
            yield return new WaitForSeconds(warningTime);

            for (int shot = 0; shot < 3 && !arena.Finished; shot++)
            {
                Intent = "저공 돌탄";
                PlayMotion(shootFrames);
                arena.PlayEffect(spreadSound);
                arena.SpawnRock(new Vector2(start, y), Vector2.right * direction * 15, 14);
                yield return new WaitForSeconds(.24f);
            }
        }

        private IEnumerator FaultLine()
        {
            const int columns = 5;
            const float spacing = 2.5f;
            const float interval = .18f;
            float left = arena.GroundBounds.min.x + 1.25f;
            float right = arena.GroundBounds.max.x - 1.25f;
            float start = Mathf.Clamp(PlayerPosition.x - 2 * spacing, left, right - (columns - 1) * spacing);
            float direction = DirectionToPlayer;
            if (direction < 0) start += (columns - 1) * spacing;
            Warn("지면 균열 예고", slamWarningSound, new Vector2(0, .18f), 14, warningTime);
            for (int i = 0; i < columns; i++)
            {
                float x = start + i * spacing * direction;
                arena.Flash(new Vector2(x, arena.GroundY + .14f), new Vector2(2.2f, .28f),
                    new Color(1, .34f, .12f, .65f), warningTime + i * interval);
            }
            yield return new WaitForSeconds(warningTime);
            if (arena.Finished) yield break;

            Intent = "지면 균열";
            PlayMotion(slamFrames);
            for (int i = 0; i < columns && !arena.Finished; i++)
            {
                float x = start + i * spacing * direction;
                var impact = new Vector2(x, arena.GroundY + .25f);
                arena.PlayEffect(fissureSound);
                arena.ShowRockLandingDust(impact, 1.8f, playSound: false);
                arena.SpawnRock(impact, Vector2.up * 7, 15, RockFlight.Burst);
                arena.ShakeImpact(impact, Vector2.down * .11f);
                yield return new WaitForSeconds(interval);
            }
        }

        private IEnumerator Crossfire()
        {
            float center = Mathf.Clamp(PlayerPosition.x, arena.GroundBounds.min.x + 7, arena.GroundBounds.max.x - 7);
            Warn("양옆 협공 예고", dashWarningSound, new Vector2(0, -.15f), -15, warningTime);
            for (int wave = 0; wave < 2 && !arena.Finished; wave++)
            {
                float lead = wave == 0 ? warningTime : .62f;
                float y = arena.GroundY + (wave == 0 ? .95f : 2.9f);
                var left = new Vector2(center - 7, y);
                var right = new Vector2(center + 7, y);
                var laneColor = new Color(1, .74f, .2f, .48f);
                arena.Flash((left + right) * .5f, new Vector2(14, .16f), laneColor, lead);
                arena.Flash(left, Vector2.one * 1.15f, laneColor, lead);
                arena.Flash(right, Vector2.one * 1.15f, laneColor, lead);
                yield return new WaitForSeconds(lead);
                if (arena.Finished) yield break;

                Intent = wave == 0 ? "협공 · 하단" : "협공 · 상단";
                PlayMotion(shootFrames);
                arena.PlayEffect(skipSound);
                arena.SpawnRock(left, Vector2.right * 12, 13);
                arena.SpawnRock(right, Vector2.left * 12, 13);
                yield return new WaitForSeconds(.5f);
            }
        }

        private IEnumerator CeilingCollapse()
        {
            const float spacing = 2.9f;
            const float height = 13;
            const float speed = 12;
            float center = Mathf.Clamp(PlayerPosition.x, arena.GroundBounds.min.x + 7, arena.GroundBounds.max.x - 7);
            int safeLane = DirectionToPlayer < 0 ? -1 : 1;
            Warn("천장 붕괴 예고", rainWarningSound, new Vector2(0, .24f), -20, warningTime);
            for (int lane = -2; lane <= 2; lane++)
            {
                if (lane == safeLane) continue;
                float x = center + lane * spacing;
                arena.Flash(new Vector2(x, arena.GroundY + .16f), new Vector2(1.95f, .32f),
                    new Color(.8f, .4f, 1, .7f), warningTime + height / speed);
            }
            yield return new WaitForSeconds(warningTime);
            if (arena.Finished) yield break;

            Intent = "천장 붕괴";
            PlayMotion(shootFrames);
            arena.PlayEffect(rainLaunchSound);
            for (int lane = -2; lane <= 2; lane++)
            {
                if (lane == safeLane) continue;
                float x = center + lane * spacing;
                arena.SpawnRock(new Vector2(x, arena.GroundY + height), Vector2.down * speed, 14, RockFlight.Falling);
            }
            yield return new WaitForSeconds(height / speed);
        }

        private IEnumerator StoneNova()
        {
            Vector2 origin = transform.position;
            Vector2 gap = PlayerPosition - origin;
            float angle = gap.sqrMagnitude > .01f ? Mathf.Atan2(gap.y, gap.x) : 0;
            Warn("원형 파편 예고", spreadWarningSound, new Vector2(0, .14f), 0, warningTime);
            arena.ShowNovaPulse(origin, warningTime, charging: true);
            var escape = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            for (int marker = 1; marker <= 3; marker++)
            {
                Vector2 point = origin + escape * (marker * 1.35f);
                if (point.y > arena.GroundY && point.x > arena.GroundBounds.min.x && point.x < arena.GroundBounds.max.x)
                    arena.Flash(point, Vector2.one * .22f, new Color(.45f, 1, .8f, .85f), warningTime);
            }
            yield return new WaitForSeconds(warningTime);
            if (arena.Finished) yield break;

            Intent = "원형 파편";
            PlayMotion(shootFrames);
            arena.PlayEffect(spreadSound);
            arena.ShowNovaPulse(origin, .28f, charging: false);
            for (int spoke = 2; spoke <= 10; spoke++)
            {
                float heading = angle + spoke * Mathf.PI / 6;
                var direction = new Vector2(Mathf.Cos(heading), Mathf.Sin(heading));
                arena.SpawnRock(origin + direction * .8f, direction * 9, 12);
            }
            yield return new WaitForSeconds(.65f);
        }

        private IEnumerator RainWaves()
        {
            // Only the foreground attack can move, face, or animate the boss.
            yield return new WaitForSeconds(1.25f);
            while (!arena.Finished)
            {
                if (pauseRain)
                {
                    yield return null;
                    continue;
                }
                yield return RockRain();
                if (arena.Finished) yield break;
                yield return new WaitForSeconds(Enraged ? 1.75f : 2.9f);
            }
        }

        private IEnumerator RockRain()
        {
            const float launchLead = .35f;
            const float fallHeight = 16;
            const float fallSpeed = 11;
            arena.PlayEffect(rainWarningSound);
            yield return new WaitForSeconds(launchLead);
            if (arena.Finished || pauseRain) yield break;
            arena.PlayEffect(rainLaunchSound);
            if (!Hidden)
                arena.SpawnRock((Vector2)transform.position + Vector2.up, Vector2.up * 25, 0, RockFlight.Launch);
            int rockCount = Enraged ? 7 : 5;
            float rockInterval = Enraged ? .23f : .32f;
            float left = ClampX(float.NegativeInfinity);
            float right = ClampX(float.PositiveInfinity);
            float spacing = Mathf.Min(4, (right - left) / rockCount);
            float halfSpan = spacing * rockCount / 2;
            float center = Mathf.Clamp(PlayerPosition.x, left + halfSpan, right - halfSpan);
            var positions = new float[rockCount];
            for (int i = 0; i < rockCount; i++)
                positions[i] = center + (i - (rockCount - 1) * .5f + Random.Range(-.1f, .1f)) * spacing;

            for (int i = 0; i < rockCount && !arena.Finished && !pauseRain; i++)
            {
                int selected = Random.Range(i, rockCount);
                float x = positions[selected];
                positions[selected] = positions[i];
                arena.Flash(new Vector2(x, arena.GroundY + .1f), new Vector2(1.35f, .2f),
                    new Color(1, .45f, .2f, .65f), fallHeight / fallSpeed);
                arena.SpawnRock(new Vector2(x, arena.GroundY + fallHeight), Vector2.down * fallSpeed, 14, RockFlight.Falling);
                if (i < rockCount - 1) yield return new WaitForSeconds(rockInterval);
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
                    Vector2 direction = Quaternion.Euler(0, 0, i * 23) * aim;
                    arena.SpawnRock(origin, direction * 8, 11);
                }
                if (volley < 3) yield return new WaitForSeconds(Enraged ? .29f : .43f);
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
                if (i < 4) yield return new WaitForSeconds(Enraged ? .63f : .87f);
            }
        }

        private IEnumerator TeleportSlam()
        {
            const float hoverTime = .8f;
            const float fallSpeed = 27f;
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
            Intent = "내려찍기 예고";
            arena.Flash(new Vector2(x, arena.GroundY + 2), new Vector2(3, 4),
                new Color(1, .24f, .25f, .28f), hoverTime + (transform.position.y - floor) / fallSpeed);
            yield return new WaitForSeconds(hoverTime);
            if (!arena.Finished)
            {
                Intent = "내려찍기";
                PlayMotion(slamFrames);
                arena.SpawnRock((Vector2)transform.position + Vector2.down, Vector2.down * fallSpeed, 24, RockFlight.Crashing);
            }
            while (!arena.Finished && transform.position.y > floor)
            {
                Vector2 next = new Vector2(x, Mathf.MoveTowards(transform.position.y, floor, Time.deltaTime * fallSpeed));
                transform.position = next;
                yield return null;
            }
            transform.position = new Vector2(x, floor);
            if (arena.Finished) yield break;
            arena.PlayEffect(slamSound);
            Vector2 impact = new Vector2(x, arena.GroundY + .4f);
            arena.ShakeImpact(impact, Vector2.down * .32f);
            arena.Flash(impact, new Vector2(3.6f, .9f), new Color(1, .35f, .3f, .85f), .23f);
        }
    }
}
