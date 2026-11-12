using System.Collections;
using UnityEngine;

namespace BossTemplate
{
    public sealed partial class ArenaBoss : MonoBehaviour
    {
        [SerializeField, InspectorName("공격 간격")] private float attackInterval = .95f;
        [SerializeField, InspectorName("공격 예고 시간")] private float warningTime = .65f;
        [SerializeField] private SpriteRenderer appearance;
        [SerializeField] private Sprite[] idleFrames, chargeFrames, shootFrames, slamFrames, defeatFrames;
        [SerializeField, InspectorName("2페이즈 거대 골렘")] private Sprite giantSprite;
        [SerializeField, InspectorName("2페이즈 주먹")] private Sprite fistSprite;
        [SerializeField, Min(1), InspectorName("2페이즈 최대 체력")] private float phaseTwoHealth = 2700;
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
        [SerializeField, InspectorName("주먹 충돌")] private AudioClip fistImpactSound;
        [SerializeField, InspectorName("주먹 내려찍기 착지")] private AudioClip fistSlamSound;
        [SerializeField, InspectorName("2페이즈 골렘 등장")] private AudioClip giantRiseSound;
        private const float FramesPerSecond = 12;
        private const float FallingRockHoverHeight = 7.8f;
        private const float HealingMineChance = .3f;
        private const float FistHeight = 4.2f;
        private const int OriginalFistCount = 2;
        private const int TotalFistCount = 4;
        private const float LegacyEnrageHealthRatio = .30f;
        private const float DefaultEnrageHealthRatio = .50f;
        private const float DefaultPhaseTwoHealthMultiplier = 4f / 3f;
        private const float EnrageRevealDuration = .55f;
        internal const int GiantSortingOrder = ArenaProjectile.PlantedBodySortingOrder - 1;
        private bool pauseRain;
        internal bool Hidden { get; private set; }
        private BossArena arena;
        private SpriteRenderer body;
        private Color baseColor;
        private Sprite[] motion;
        private float motionStart, motionSpeed;
        private bool loopMotion;
        private Vector3 neutralPosition, neutralScale, warningOffset;
        private Quaternion neutralRotation;
        private int neutralSortingOrder;
        private SpriteMaskInteraction neutralMaskInteraction;
        private SpriteMask groundMask;
        private Color neutralColor;
        private float warningTilt, poseDuration;
        private float giantFollowVelocity;
        [SerializeField, InspectorName("골렘 주먹 렌더러")] private SpriteRenderer[] fists;
        private readonly SpriteRenderer[] battleFists = new SpriteRenderer[TotalFistCount];
        private readonly int[] fistSortingOrders = new int[OriginalFistCount];
        private float fistRevealFloor;
        private readonly float[] reinforcementEntryX = new float[TotalFistCount - OriginalFistCount];
        private readonly float[] fistFlashUntil = new float[TotalFistCount];
        private readonly bool[] fistBusy = new bool[TotalFistCount];
        private bool holdBothFists;
        private readonly float[] fistWindupUntil = new float[TotalFistCount];
        private float fistOrbitStart, enragedAt;
        private bool enraged;
        private static readonly Color FistWarningColor = new Color(1, .52f, .23f);
        private static readonly Color FistReleaseColor = new Color(1, .86f, .53f);
        internal float PhaseTwoMaximum => phaseTwoHealth * (arena.Study
            ? arena.Study.BossHealthFactor * Mathf.Max(1, arena.Study.Protocol.phaseTwoHealthMultiplier)
            : DefaultPhaseTwoHealthMultiplier);
        private float EnrageHealthRatio => arena.Study ? arena.Study.Protocol.fourHandHealthThreshold : DefaultEnrageHealthRatio;
        public FighterHealth Health { get; private set; }
        public int Phase { get; private set; } = 1;
        public bool Transitioning { get; private set; }
        internal bool CanBeHit => Phase == 1 && !Hidden && !Transitioning && !Health.Dead;
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
                neutralScale = appearance.transform.localScale;
                neutralSortingOrder = appearance.sortingOrder;
                neutralMaskInteraction = appearance.maskInteraction;
                neutralColor = appearance.color;
            }
            groundMask = arena.CreateAboveGroundMask();
            for (int i = 0; i < OriginalFistCount; i++)
            {
                SpriteRenderer fist = fists[i];
                fistSortingOrders[i] = fist.sortingOrder;
                if (fistSprite) fist.sprite = fistSprite;
                else if (!fist.sprite) fist.sprite = body.sprite;
                fist.transform.localScale = Vector3.one * (FistHeight / fist.sprite.bounds.size.y);
                fist.sharedMaterial = appearance ? appearance.sharedMaterial : body.sharedMaterial;
                battleFists[i] = fist;
                var reinforcement = Instantiate(fist, fist.transform.parent);
                reinforcement.name = i == 0 ? "왼쪽 추가 주먹" : "오른쪽 추가 주먹";
                reinforcement.transform.localScale *= .86f;
                reinforcement.enabled = false;
                battleFists[i + OriginalFistCount] = reinforcement;
            }
        }

        public void ResetState()
        {
            StopAllCoroutines(); body.color = baseColor; Intent = "전투 준비";
            Phase = 1;
            Transitioning = false;
            DefeatedAt = null;
            DefeatAnimationComplete = false;
            groundMask.enabled = false;
            giantFollowVelocity = 0;
            giantBurrowing = false;
            hockeyHitstopUntil = 0;
            holdBothFists = false;
            enraged = false;
            for (int i = 0; i < battleFists.Length; i++)
            {
                fistBusy[i] = false;
                fistWindupUntil[i] = fistFlashUntil[i] = 0;
                battleFists[i].enabled = false;
                battleFists[i].transform.rotation = Quaternion.identity;
                battleFists[i].sortingOrder = fistSortingOrders[i % OriginalFistCount];
                battleFists[i].maskInteraction = SpriteMaskInteraction.None;
            }
            if (appearance)
            {
                appearance.transform.SetLocalPositionAndRotation(neutralPosition, neutralRotation);
                appearance.transform.localScale = neutralScale;
                appearance.sortingOrder = neutralSortingOrder;
                appearance.maskInteraction = neutralMaskInteraction;
                appearance.color = neutralColor;
            }
            SetHidden(false);
            pauseRain = false;
            Face(arena.Player.transform.position.x - transform.position.x);
            PlayMotion(idleFrames, loop: true);
            StartCoroutine(FightPhaseOne());
            StartCoroutine(RainWaves());
        }

        internal void BeginPhaseTwo()
        {
            StopAllCoroutines();
            Transitioning = true;
            StartCoroutine(RevealGiant());
        }

        private IEnumerator RevealGiant()
        {
            Intent = "골렘이 뛰어내린다";
            SetHidden(false);
            PlayMotion(idleFrames, speed: 2, loop: true);
            if (appearance) appearance.sortingOrder = -1;
            Vector3 start = transform.position;
            float direction = -DirectionToPlayer;
            Face(direction);
            float exitX = ClampX(start.x + direction * 3.5f);
            for (float elapsed = 0; elapsed < .85f; elapsed += Time.deltaTime)
            {
                float t = elapsed / .85f;
                transform.position = new Vector3(Mathf.Lerp(start.x, exitX, t),
                    Mathf.Lerp(start.y, arena.GroundY - 6, t * t) + 8 * t * (1 - t), start.z);
                yield return null;
            }

            SetHidden(true);
            yield return new WaitForSeconds(.3f);

            Intent = "플랫폼 뒤에서 거대한 골렘이 올라온다";
            float giantX = GiantTargetX;
            transform.position = new Vector3(giantX, StandingY, start.z);
            fistRevealFloor = arena.GroundVisualBottom;
            if (appearance && (giantSprite || appearance.sprite))
            {
                PlayMotion(null);
                Sprite sprite = giantSprite ? giantSprite : appearance.sprite;
                appearance.sprite = sprite;
                appearance.flipX = false;
                // Keep the giant behind both layers of planted mines.
                int behindPlatforms = arena.BehindPlatformSortingOrder;
                appearance.sortingOrder = Mathf.Min(GiantSortingOrder, behindPlatforms);
                groundMask.enabled = true;
                appearance.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                for (int i = 0; i < OriginalFistCount; i++)
                {
                    fists[i].sortingOrder = behindPlatforms;
                    // Sharing the giant's SpriteMask can draw its texture on the fists.
                    fists[i].maskInteraction = SpriteMaskInteraction.None;
                }
                appearance.color = neutralColor * new Color(.8f, .86f, .94f, 1);

                float scale = 15f / sprite.bounds.size.y;
                Vector3 parentScale = appearance.transform.parent.lossyScale;
                appearance.transform.localScale = new Vector3(scale / parentScale.x, scale / parentScale.y, 1);
                Vector3 center = new Vector3(giantX, arena.GroundY + 1.1f, start.z);
                Vector3 raised = appearance.transform.parent.InverseTransformPoint(center - sprite.bounds.center * scale);
                const float riseHeight = 11f;
                Vector3 lowered = raised + appearance.transform.parent.InverseTransformVector(Vector3.down * riseHeight);
                appearance.transform.localPosition = lowered;
                ShowOriginalFists(-riseHeight);
                SetHidden(false);
                arena.PlayEffect(giantRiseSound);
                const float riseDuration = 3f;
                const float rumbleInterval = .2f;
                var rumbleOrigin = new Vector2(giantX, arena.GroundY + 1);
                float nextRumble = 0;
                for (float elapsed = 0; elapsed < riseDuration; elapsed += Time.deltaTime)
                {
                    float progress = elapsed / riseDuration;
                    float rise = Mathf.SmoothStep(0, 1, progress);
                    appearance.transform.localPosition = Vector3.Lerp(lowered, raised, rise);
                    PositionRisingFists(Mathf.Lerp(-riseHeight, 0, rise));
                    if (elapsed >= nextRumble)
                    {
                        float strength = Mathf.Lerp(.35f, .09f, progress);
                        arena.ShakeImpact(rumbleOrigin, Vector2.down * strength, allowOffscreen: true);
                        nextRumble = elapsed + rumbleInterval;
                    }
                    yield return null;
                }
                appearance.transform.localPosition = raised;
                PositionRisingFists(0);
            }
            else SetHidden(false);

            yield return new WaitForSeconds(.45f);
            Phase = 2;
            Transitioning = false;
            Health.ResetHealth(PhaseTwoMaximum);
            for (int i = 0; i < OriginalFistCount; i++)
                fists[i].sortingOrder = fistSortingOrders[i];
            Intent = "2페이즈 시작";
            if (!arena.Finished)
            {
                fistOrbitStart = Time.time;
                if (!fists[0].enabled) ShowOriginalFists(0);
                StartCoroutine(FightWithFists());
            }
        }

        private void ShowOriginalFists(float verticalOffset)
        {
            for (int i = 0; i < OriginalFistCount; i++)
                fists[i].flipX = i == 1;
            PositionRisingFists(verticalOffset);
        }

        private void PositionRisingFists(float verticalOffset)
        {
            for (int i = 0; i < OriginalFistCount; i++)
            {
                SpriteRenderer fist = fists[i];
                fist.transform.position = FistOrbitPosition(i, atOrbitStart: true) + Vector2.up * verticalOffset;
                // Reveal only when the foreground terrain can cover the part of the sprite below ground.
                float alpha = Mathf.SmoothStep(0, 1, (fist.bounds.min.y - fistRevealFloor) / .65f);
                fist.enabled = alpha > 0;
                fist.color = new Color(1, 1, 1, alpha);
            }
        }

        private Vector2 FistOrbitPosition(int index, bool atOrbitStart = false)
        {
            bool reinforcement = index >= OriginalFistCount;
            float sideOffset = reinforcement ? 5.5f : 9f;
            float horizontalRadius = reinforcement ? 1.1f : 1.6f;
            const float verticalRadius = 1.2f;
            const float orbitPeriod = 5f;
            Bounds bounds = arena.GroundBounds;
            float clearance = Mathf.Min(bounds.extents.x, sideOffset + horizontalRadius + FistHitBounds(battleFists[index]).extents.x);
            float centerX = Mathf.Clamp(transform.position.x, bounds.min.x + clearance, bounds.max.x - clearance);
            centerX += index % 2 == 0 ? -sideOffset : sideOffset;
            float centerY = arena.GroundY + FistHeight * .5f + .35f + verticalRadius + (reinforcement ? 3.1f : 0);
            float elapsed = atOrbitStart ? 0 : Time.time - fistOrbitStart;
            float angle = elapsed * (2 * Mathf.PI / orbitPeriod) + (1 - index) * Mathf.PI
                + (reinforcement ? Mathf.PI * .5f : 0);
            return new Vector2(centerX + Mathf.Cos(angle) * horizontalRadius, centerY + Mathf.Sin(angle) * verticalRadius);
        }

        internal float FistContactTime(Vector2 from, Vector2 to, out int hitIndex)
        {
            hitIndex = -1;
            float first = float.PositiveInfinity;
            for (int i = 0; i < battleFists.Length; i++)
            {
                if (!battleFists[i].enabled) continue;
                float time = ArenaProjectile.BoundsContactTime(FistHitBounds(battleFists[i]), from, to);
                if (time >= first) continue;
                first = time;
                hitIndex = i;
            }
            return first;
        }

        private static Bounds FistHitBounds(SpriteRenderer fist)
        {
            // Keep the same combat footprint while the sprite tilts along a diagonal.
            Bounds local = fist.localBounds;
            Vector3 scale = fist.transform.lossyScale;
            return new Bounds(fist.transform.position + Vector3.Scale(local.center, scale),
                Vector3.Scale(local.size, scale));
        }

        internal void HitFist(int index, float damage)
        {
            if (Health.Hit(damage, flash: false)) fistFlashUntil[index] = Time.time + .1f;
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
            if (!Health || arena.Frozen) return;
            if (Phase == 2)
            {
                if (Health.Dead && !arena.Player.Health.Dead) return;
                if (giantBurrowing && !arena.Finished) return;
                if (!arena.Finished && Time.deltaTime > 0)
                {
                    if (!enraged)
                    {
                        float ratio = EnrageHealthRatio;
                        bool ready = ratio > 0
                            ? Health.Current <= Health.Maximum * ratio
                            : Health.Current < Health.Maximum * LegacyEnrageHealthRatio;
                        if (ready) Enrage();
                    }
                    Vector3 position = transform.position;
                    position.x = Mathf.SmoothDamp(position.x, PlayerPosition.x, ref giantFollowVelocity, 1.25f, 2.2f);
                    transform.position = position;
                }
                for (int i = 0; i < battleFists.Length; i++)
                {
                    SpriteRenderer fist = battleFists[i];
                    if (arena.Finished) fist.enabled = false;
                    if (!fist.enabled) continue;
                    bool reinforcement = i >= OriginalFistCount;
                    if (!fistBusy[i] && (!holdBothFists || reinforcement))
                    {
                        Vector2 orbit = FistOrbitPosition(i);
                        float revealProgress = (Time.time - enragedAt) / EnrageRevealDuration;
                        if (reinforcement && revealProgress < 1)
                        {
                            float leftEdge = arena.ScreenEdgeX(right: false);
                            float rightEdge = arena.ScreenEdgeX(right: true);
                            float inset = Mathf.Min(fist.bounds.extents.x, (rightEdge - leftEdge) * .5f);
                            float visibleX = Mathf.Clamp(orbit.x, leftEdge + inset, rightEdge - inset);
                            float settle = Mathf.SmoothStep(0, 1, (revealProgress - .65f) / .35f);
                            float entry = Mathf.SmoothStep(0, 1, revealProgress);
                            orbit.x = Mathf.Lerp(reinforcementEntryX[i - OriginalFistCount],
                                Mathf.Lerp(visibleX, orbit.x, settle), entry);
                        }
                        fist.transform.position = orbit;
                    }
                    float windupRemaining = fistWindupUntil[i] - Time.time;
                    Color color = windupRemaining > 0
                        ? Color.Lerp(FistReleaseColor, FistWarningColor, windupRemaining / .22f)
                        : reinforcement ? FistReleaseColor : Color.white;
                    if (Time.time < fistFlashUntil[i])
                        color = Color.Lerp(color, ArenaProjectile.HitFlashColor, windupRemaining > 0 ? .4f : 1f);
                    fist.color = color;
                }
                return;
            }
            if (!appearance) return;
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

        private void Enrage()
        {
            enraged = true;
            enragedAt = Time.time;
            if (appearance) appearance.color *= new Color(1, .68f, .51f);
            for (int i = OriginalFistCount; i < battleFists.Length; i++)
            {
                SpriteRenderer fist = battleFists[i];
                bool right = i % 2 != 0;
                fist.flipX = right;
                Bounds bounds = fist.bounds;
                float innerEdge = right ? bounds.min.x : bounds.max.x;
                Vector2 start = FistOrbitPosition(i);
                start.x = arena.ScreenEdgeX(right) + (fist.transform.position.x - innerEdge) + (right ? .5f : -.5f);
                reinforcementEntryX[i - OriginalFistCount] = start.x;
                fist.transform.position = start;
                fist.enabled = true;
            }
            var impact = new Vector2(transform.position.x, arena.GroundY);
            arena.ShowRockLandingDust(impact, 6, playSound: false);
            arena.ShakeImpact(impact, Vector2.down * .45f, allowOffscreen: true);
            arena.PlayEffect(fistSlamSound);
        }

        private IEnumerator FightPhaseOne()
        {
            yield return new WaitForSeconds(1f);
            int nextPattern = 0;
            BossPattern? previous = null;
            while (!arena.Finished)
            {
                BossPattern pattern = arena.Study
                    ? arena.Study.Pick(StudySession.PhaseOne, previous)
                    : StudySession.PhaseOne[nextPattern++ % StudySession.PhaseOne.Length];
                previous = pattern;
                arena.Study?.Executed(pattern);
                pauseRain = (int)pattern < (int)BossPattern.dash;
                switch (pattern)
                {
                    case BossPattern.ground_barrage: yield return GroundBarrage(); break;
                    case BossPattern.fault_line: yield return FaultLine(); break;
                    case BossPattern.crossfire: yield return Crossfire(); break;
                    case BossPattern.ceiling_collapse: yield return CeilingCollapse(); break;
                    case BossPattern.stone_nova: yield return StoneNova(); break;
                    case BossPattern.dash: yield return Dash(); break;
                    case BossPattern.spread: yield return Spread(); break;
                    case BossPattern.skipping_rocks: yield return SkippingRocks(); break;
                    case BossPattern.teleport_slam: yield return TeleportSlam(); break;
                }
                pauseRain = false;
                if (arena.Finished) break;
                PlayMotion(idleFrames, loop: true);
                Intent = "다음 공격 준비";
                yield return new WaitForSeconds(attackInterval * .45f * arena.Parameters(pattern).interval);
            }
            Intent = "전투 종료";
            if (!Health.Dead) PlayMotion(idleFrames, loop: true);
        }

        private Vector2 PlayerPosition => arena.Player.transform.position;
        private float GiantTargetX => Mathf.Clamp(PlayerPosition.x, arena.GroundBounds.min.x + 5, arena.GroundBounds.max.x - 5);
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
            arena.SpawnRock(BossPattern.dash, new Vector2(launchX, y), Vector2.right * direction * 28, 20);
            while (!arena.Finished && !Mathf.Approximately(transform.position.x, destination))
            {
                Vector2 next = new Vector2(Mathf.MoveTowards(transform.position.x, destination, Time.deltaTime * 27 * arena.Parameters(BossPattern.dash).speed), StandingY);
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
                arena.SpawnRock(BossPattern.ground_barrage, new Vector2(start, y), Vector2.right * direction * 15, 14);
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
                arena.SpawnRock(BossPattern.fault_line, impact, Vector2.up * 7, 15, RockFlight.Burst);
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
                arena.SpawnRock(BossPattern.crossfire, left, Vector2.right * 12, 13);
                arena.SpawnRock(BossPattern.crossfire, right, Vector2.left * 12, 13);
                yield return new WaitForSeconds(.5f);
            }
        }

        private IEnumerator CeilingCollapse()
        {
            const float spacing = 2.9f;
            const float speed = 8.5f;
            float center = Mathf.Clamp(PlayerPosition.x, arena.GroundBounds.min.x + 7, arena.GroundBounds.max.x - 7);
            int safeLane = DirectionToPlayer < 0 ? -1 : 1;
            int healingLane = Random.Range(-2, 2);
            if (healingLane >= safeLane) healingLane++;
            bool hasHealingMine = Random.value < HealingMineChance;
            Warn("천장 붕괴 예고", rainWarningSound, new Vector2(0, .24f), -20, warningTime);
            for (int lane = -2; lane <= 2; lane++)
            {
                if (lane == safeLane) continue;
                float x = center + lane * spacing;
                arena.SpawnRock(BossPattern.ceiling_collapse, new Vector2(x, arena.GroundY + FallingRockHoverHeight),
                    Vector2.down * speed, 14, RockFlight.Falling, holdTime: warningTime,
                    healingMine: hasHealingMine && lane == healingLane);
            }
            yield return new WaitForSeconds(warningTime);
            if (arena.Finished) yield break;

            Intent = "천장 붕괴";
            PlayMotion(shootFrames);
            arena.PlayEffect(rainLaunchSound);
            yield return new WaitForSeconds(FallingRockHoverHeight / speed);
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
                arena.SpawnRock(BossPattern.stone_nova, origin + direction * .8f, direction * 9, 12);
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
                arena.Study?.Executed(BossPattern.rock_rain);
                yield return RockRain();
                if (arena.Finished) yield break;
                var rain = arena.Parameters(BossPattern.rock_rain);
                yield return new WaitForSeconds(RockRainRepeatDelay(rain));
            }
        }

        internal static float RockRainRepeatDelay(PatternParameters rain) =>
            2.2f * rain.interval / rain.selection_weight;

        private IEnumerator RockRain()
        {
            const float launchLead = .35f;
            const float fallSpeed = 11;
            const float hoverTime = .75f;
            arena.PlayEffect(rainWarningSound);
            yield return new WaitForSeconds(launchLead);
            if (arena.Finished || pauseRain) yield break;
            arena.PlayEffect(rainLaunchSound);
            if (!Hidden)
                arena.SpawnRock(BossPattern.rock_rain, (Vector2)transform.position + Vector2.up, Vector2.up * 25, 0, RockFlight.Launch);
            const int rockCount = 7;
            const float rockInterval = .25f;
            int healingRock = Random.value < HealingMineChance ? Random.Range(0, rockCount) : -1;
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
                arena.SpawnRock(BossPattern.rock_rain, new Vector2(x, arena.GroundY + FallingRockHoverHeight),
                    Vector2.down * fallSpeed, 14, RockFlight.Falling, holdTime: hoverTime, healingMine: i == healingRock);
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
                    arena.SpawnRock(BossPattern.spread, origin, direction * 8, 11);
                }
                if (volley < 3) yield return new WaitForSeconds(.43f);
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
                arena.SpawnRock(BossPattern.skipping_rocks, transform.position, new Vector2(direction * 4.5f, 3.5f), 12, RockFlight.Skipping);
                if (i < 4) yield return new WaitForSeconds(.87f);
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
            arena.SpawnRock(BossPattern.teleport_slam, (Vector2)transform.position + Vector2.down, Vector2.down * fallSpeed,
                24, RockFlight.Crashing, holdTime: hoverTime);
            yield return new WaitForSeconds(hoverTime);
            if (!arena.Finished)
            {
                Intent = "내려찍기";
                PlayMotion(slamFrames);
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
