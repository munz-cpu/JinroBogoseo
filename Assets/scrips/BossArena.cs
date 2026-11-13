using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;
namespace BossTemplate
{
    public sealed class BossArena : MonoBehaviour
    {
        [System.Serializable]
        private struct RockAppearance
        {
            public Sprite solid;
            public Sprite shell;
        }

        [SerializeField, InspectorName("바닥")] private ArenaPlatform ground;
        [SerializeField, InspectorName("착지 가능한 발판 목록 (바닥 포함)")] private ArenaPlatform[] platforms;
        public float GroundY => ground.Bounds.max.y;
        public Bounds GroundBounds => ground.Bounds;
        internal float GroundVisualBottom
        {
            get
            {
                float bottom = GroundY;
                foreach (var renderer in ground.GetComponentsInChildren<SpriteRenderer>())
                    if (renderer.enabled && renderer.color.a > 0)
                        bottom = Mathf.Min(bottom, renderer.bounds.min.y);
                return bottom;
            }
        }
        [SerializeField, InspectorName("플레이어")] private SideViewPlayer player;
        [SerializeField, InspectorName("보스")] private ArenaBoss boss;
        [SerializeField, InspectorName("경기장 카메라")] private Camera arenaCamera;
        [SerializeField, InspectorName("탄환 목록")] private ArenaProjectile[] projectiles;
        [SerializeField, InspectorName("공격 표시 목록")] private ArenaEffect[] effects;
        [SerializeField, InspectorName("섬광 메시")] private Mesh sparkMesh;
        [SerializeField, InspectorName("점프 링 메시")] private Mesh jumpRingMesh;
        [SerializeField, InspectorName("낙석 먼지 메시")] private Mesh rockDustMesh;
        [SerializeField, InspectorName("회복 구슬")] private ArenaHealthOrb healthOrb;
        [SerializeField, InspectorName("전투 효과음 소스")] private AudioSource effectsAudio;
        private float effectsVolume;
        [Header("낙석")]
        [SerializeField, InspectorName("돌 스프라이트")] private Sprite rockSprite;
        [SerializeField, InspectorName("돌 외형 세트")] private RockAppearance[] fallingRocks;
        [SerializeField, Range(0, 1), InspectorName("바닥에 박힐 확률")] private float rockPlantChance = .45f;
        [SerializeField, Min(.1f), InspectorName("폭발까지 걸리는 시간")] private float rockFuse = 4;
        [SerializeField, InspectorName("돌 폭발 효과음")] private AudioClip rockExplosionSound;
        [SerializeField, InspectorName("돌 파괴 효과음")] private AudioClip rockBreakSound;
        [SerializeField, InspectorName("낙석 착지 효과음")] private AudioClip rockLandingSound;
        [SerializeField, Range(0, 2), InspectorName("충돌 화면 흔들림")] private float shakeStrength = 1;
        private Vector3 playerStart, bossStart;
        private float phaseOneHealth;
        private CinemachineBrain cameraBrain;
        private CinemachineCamera defeatCamera;
        private Transform previousCameraTarget;
        private readonly CinemachineImpulseDefinition impactImpulse = new CinemachineImpulseDefinition
        {
            ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform,
            ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump,
            ImpulseDuration = .18f
        };
        public SideViewPlayer Player => player;
        public ArenaBoss Boss => boss;
        internal StudySession Study { get; private set; }
        public bool ResearchActive => Study != null;
        internal bool Frozen => Study && Study.Frozen;
        internal bool EnhancedPhaseTwoEffects => !Study || Study.Protocol.UsesDashAssist;
        public bool Finished => (Study && !Study.InCombat) || CombatEnded;
        internal bool CombatEnded => Player.Health.Dead || (Boss.Phase == 2 && Boss.Health.Dead);
        internal bool ResultsReady => Player.Health.Dead || Boss.DefeatAnimationComplete;
        internal int BehindPlatformSortingOrder
        {
            get
            {
                int lowest = ground.Surface.sortingOrder;
                foreach (var platform in platforms)
                    if (platform) lowest = Mathf.Min(lowest, platform.Surface.sortingOrder);
                return lowest - 1;
            }
        }
        internal float StudyBossHealthRemaining
        {
            get
            {
                float phaseTwo = Boss.PhaseTwoMaximum;
                float total = phaseOneHealth * (Study ? Study.BossHealthFactor : 1) + phaseTwo;
                return Mathf.Clamp01((Boss.Health.Current + (Boss.Phase == 1 ? phaseTwo : 0)) / total);
            }
        }

        internal PatternParameters Parameters(BossPattern pattern) =>
            Study ? Study.Parameters(pattern) : AdaptiveDifficulty.Neutral;

        internal float RockPlantChance => rockPlantChance;
        internal float RockFuse => Mathf.Max(.1f, rockFuse);

        public bool TryLand(Vector2 previous, Vector2 next, Vector2 halfSize, out float centerY)
        {
            return TryLand(previous, next, halfSize, out centerY, out _);
        }

        public bool CanDropThrough(ArenaPlatform platform) => platform && platform != ground;

        internal float BlockPlantedRockSides(Vector2 previous, float nextX, Vector2 halfSize)
        {
            if (nextX == previous.x || previous.y - halfSize.y >= GroundY + ArenaProjectile.PlantedHeight - .001f)
                return nextX;

            float clearance = halfSize.x + ArenaProjectile.PlantedHalfWidth;
            foreach (var item in projectiles)
            {
                if (!item.Planted) continue;
                float rockX = item.transform.position.x;
                float left = rockX - clearance;
                float right = rockX + clearance;
                if (previous.x <= left + .001f && nextX > left) nextX = Mathf.Max(previous.x, left);
                else if (previous.x >= right - .001f && nextX < right) nextX = Mathf.Min(previous.x, right);
            }
            return nextX;
        }

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

            float rockTop = GroundY + ArenaProjectile.PlantedHeight;
            if (next.y < previous.y && previous.y - halfSize.y >= rockTop - .01f
                && next.y - halfSize.y <= rockTop && rockTop + halfSize.y > centerY)
            {
                float clearance = halfSize.x + ArenaProjectile.PlantedHalfWidth;
                foreach (var item in projectiles)
                {
                    if (!item.Planted || Mathf.Abs(next.x - item.transform.position.x) >= clearance - .001f) continue;
                    centerY = rockTop + halfSize.y;
                    landedOn = null;
                    return true;
                }
            }
            return landed;
        }
        private void Awake()
        {
            if (effectsAudio) effectsVolume = effectsAudio.volume;
            if (arenaCamera) cameraBrain = arenaCamera.GetComponent<CinemachineBrain>();
            playerStart = player.transform.position; bossStart = boss.transform.position;
            Player.Initialize(this); Boss.Initialize(this);
            phaseOneHealth = Boss.Health.Maximum;
            Player.Health.InitializeAudio(this);
            Boss.Health.InitializeAudio(this);
            healthOrb.Initialize(this);
            ResetFight();
        }

        private void OnEnable()
        {
            StudyAudioVolume.ChannelsChanged += ApplyEffectsVolume;
            ApplyEffectsVolume();
        }

        private void OnDisable() => StudyAudioVolume.ChannelsChanged -= ApplyEffectsVolume;

        private void ApplyEffectsVolume()
        {
            if (effectsAudio) effectsAudio.volume = effectsVolume * StudyAudioVolume.Get(StudyAudioChannel.Effects);
        }

        internal void AttachStudy(StudySession study)
        {
            Study = study;
            EndStudyFight();
        }

        internal float ScreenEdgeX(bool right)
        {
            if (!arenaCamera) return right ? GroundBounds.max.x : GroundBounds.min.x;
            return arenaCamera.ViewportToWorldPoint(new Vector3(right ? 1 : 0, .5f, arenaCamera.nearClipPlane)).x;
        }

        internal void ShakeImpact(Vector2 position, Vector2 impulse, bool allowOffscreen = false)
        {
            if (shakeStrength <= 0 || !cameraBrain
                || cameraBrain.ActiveVirtualCamera == null) return;

            if (!allowOffscreen)
            {
                Vector3 viewport = arenaCamera.WorldToViewportPoint(position);
                if (viewport.x < -.05f || viewport.x > 1.05f || viewport.y < -.05f || viewport.y > 1.05f) return;
            }
            impactImpulse.CreateEvent(position, impulse * shakeStrength);
        }
        public void ResetFight()
        {
            if (!Study) ResetStudyFight();
        }

        internal void ResetStudyFight()
        {
            ClearAttacks();
            if (defeatCamera) defeatCamera.Follow = previousCameraTarget;
            defeatCamera = null;
            previousCameraTarget = null;
            if (healthOrb) healthOrb.gameObject.SetActive(false);
            Player.transform.position = playerStart; Boss.transform.position = bossStart;
            Player.Health.ResetHealth(Player.Health.Maximum);
            Boss.Health.ResetHealth(phaseOneHealth * (Study ? Study.BossHealthFactor : 1));
            Player.ResetState(); Boss.ResetState();
        }

        internal void EndStudyFight(bool stopAudio = true)
        {
            Boss.StopAllCoroutines();
            ClearAttacks(stopAudio);
            if (healthOrb) healthOrb.gameObject.SetActive(false);
        }

        private void ClearAttacks(bool stopAudio = true)
        {
            if (stopAudio && effectsAudio) effectsAudio.Stop();
            foreach (var item in projectiles) item.gameObject.SetActive(false);
            foreach (var item in effects) item.gameObject.SetActive(false);
        }

        internal void DropHealthOrb(Vector2 position, Material material)
        {
            healthOrb.Drop(position, material);
        }

        internal void ShowHealthOrbPickup(Vector2 position)
        {
            Burst(jumpRingMesh, position, Vector2.one * 1.2f, new Color(.35f, 1, .45f), .32f,
                Vector2.up * .6f, 2);
        }

#if UNITY_EDITOR
        public void SkipCurrentPhase()
        {
            if (Frozen || Finished || Boss.Transitioning) return;
            Boss.Health.InvulnerableUntil = 0;
            Boss.Health.Hit(Boss.Health.Current);
            AdvancePhaseIfDefeated();
        }
#endif

        internal void PlayEffect(AudioClip clip, bool stopCurrent = false)
        {
            if (!effectsAudio) return;
            if (stopCurrent) effectsAudio.Stop();
            if (clip) effectsAudio.PlayOneShot(clip);
        }
        private void Update()
        {
            AdvancePhaseIfDefeated();
            var keyboard = Keyboard.current;
            if (!Study && ResultsReady && keyboard != null
                && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
                ResetFight();
        }

        private void AdvancePhaseIfDefeated()
        {
            if (Frozen || (Study && !Study.InCombat) || Player.Health.Dead || Boss.Transitioning
                || !Boss.Health.Dead || Boss.DefeatedAt.HasValue) return;
            ClearAttacks(stopAudio: Boss.Phase == 1);
            if (Boss.Phase == 1) Boss.BeginPhaseTwo();
            else
            {
                if (cameraBrain && cameraBrain.ActiveVirtualCamera is CinemachineCamera camera)
                {
                    defeatCamera = camera;
                    previousCameraTarget = camera.Follow;
                    camera.Follow = Boss.transform;
                }
                Boss.BeginDefeat();
            }
        }

        internal SpriteMask CreateAboveGroundMask()
        {
            var mask = new GameObject("지면 위 렌더링 마스크").AddComponent<SpriteMask>();
            mask.transform.SetParent(transform, false);
            mask.sprite = ground.Surface.sprite;
            const float height = 50f;
            Bounds floor = GroundBounds;
            Vector3 parentScale = transform.lossyScale;
            Vector3 spriteSize = mask.sprite.bounds.size;
            mask.transform.position = new Vector3(floor.center.x, GroundY + height * .5f, floor.center.z);
            mask.transform.localScale = new Vector3((floor.size.x + 40) / (spriteSize.x * parentScale.x),
                height / (spriteSize.y * parentScale.y), 1);
            mask.enabled = false;
            return mask;
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
        internal void ShowNovaPulse(Vector2 position, float lifetime, bool charging)
        {
            var color = charging ? new Color(.65f, .42f, 1) : new Color(.9f, .7f, 1);
            Burst(jumpRingMesh, position, Vector2.one * (charging ? 4 : 1.4f), color,
                lifetime, Vector2.zero, charging ? .35f : 3.6f);
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

        internal void ShowProtectedDash(Vector2 position, int facing, float duration)
        {
            Burst(jumpRingMesh, position, new Vector2(1, 1.4f), new Color(.38f, .9f, 1, .9f),
                duration, Vector2.right * (facing * 2f), 1.85f);
        }

        internal void ShowFistRush(Vector2 position, Vector2 direction, float width)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            Burst(sparkMesh, position - direction * width * .35f, new Vector2(width * 1.5f, width * .55f),
                new Color(1, .64f, .26f, .72f), .19f, direction * 5f, 1.45f, angle);
        }

        internal void ShowPhaseTwoImpact(Vector2 position, Vector2 direction, float width, float shake, bool groundImpact = false)
        {
            if (shake > 0) ShakeImpact(position, direction * shake);
            var color = new Color(1, .75f, .39f, .85f);
            Burst(jumpRingMesh, position, groundImpact ? new Vector2(width, width * .22f) : Vector2.one * width,
                color, .24f, Vector2.zero, 2.3f);
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            Vector2 tangent = Vector2.Perpendicular(direction);
            for (int i = -1; i <= 1; i += 2)
            {
                Vector2 drift = groundImpact ? new Vector2(i * 3.8f, 2.3f) : direction * 2.5f + tangent * (i * 3f);
                Burst(sparkMesh, position + tangent * (i * width * .23f), new Vector2(width * .6f, width * .21f),
                    color, .17f, drift, 1.3f, angle + i * 28);
            }
        }

        internal void ShowRockBreak(Vector2 position, Vector2 size)
        {
            PlayEffect(rockBreakSound ? rockBreakSound : rockExplosionSound);
            Burst(rockDustMesh, position, size, new Color(.79f, .68f, .53f), .42f,
                Vector2.up * .85f, 2.6f);
        }

        internal void ShowRockLandingDust(Vector2 position, float width, bool playSound = true)
        {
            if (playSound) PlayEffect(rockLandingSound);
            Burst(rockDustMesh, position, new Vector2(width, .55f), new Color(.79f, .68f, .53f, .8f),
                .3f, Vector2.up * .65f, 2f);
        }

        internal void ShowGolemCollapse(Bounds giant)
        {
            var impact = new Vector2(giant.center.x, GroundY + .2f);
            PlayEffect(rockExplosionSound);
            ShakeImpact(impact, Vector2.down * .48f, allowOffscreen: true);

            float spread = Mathf.Min(giant.extents.x, 5f);
            for (int i = -1; i <= 1; i++)
            {
                Vector2 position = impact + new Vector2(i * spread * .74f, .22f);
                Burst(rockDustMesh, position, new Vector2(3.4f, 1.3f), new Color(.67f, .63f, .57f, .78f),
                    1.55f, new Vector2(i * 1.2f, 1.5f), 2.6f, i * 9);
            }
            Burst(rockDustMesh, impact + Vector2.up * 2.4f, new Vector2(4.2f, 2.2f), new Color(.53f, .50f, .47f, .74f),
                1.26f, Vector2.up * 2.25f, 2.2f);

            if (!rockSprite) return;
            int chunk = 0;
            foreach (var effect in effects)
            {
                if (effect.gameObject.activeSelf) continue;
                float side = (chunk - 3.5f) / 3.5f;
                var position = impact + new Vector2(side * spread * .8f, Random.Range(1.1f, 5.6f));
                var launch = new Vector2(side * Random.Range(2.2f, 6.2f), Random.Range(3.2f, 8.4f));
                effect.ShowDebris(rockSprite, position, Random.Range(.55f, 1.5f), new Color(.77f, .71f, .64f),
                    Random.Range(1.3f, 1.85f), launch, side * Random.Range(120f, 310f), GroundY);
                if (++chunk == 8) break;
            }
        }

        internal Rect GetAirHockeyCourt(float paddleHalfWidth)
        {
            Bounds floor = GroundBounds;
            Vector3 center = floor.center + Vector3.up * 5;
            float halfHeight = 5.5f;
            float halfWidth = floor.extents.x;
            if (arenaCamera)
            {
                var activeCamera = cameraBrain ? cameraBrain.ActiveVirtualCamera : null;
                center = activeCamera == null ? arenaCamera.transform.position : activeCamera.State.RawPosition;
                halfHeight = arenaCamera.orthographicSize;
                halfWidth = halfHeight * arenaCamera.aspect;
            }
            float inset = Mathf.Min(paddleHalfWidth * 1.5f, halfWidth * .65f);
            float bottom = Mathf.Max(GroundY + .95f, center.y - halfHeight + .25f);
            float top = Mathf.Max(bottom + 2, center.y + halfHeight - .25f);
            return Rect.MinMaxRect(center.x - halfWidth + inset, bottom, center.x + halfWidth - inset, top);
        }

        internal void ShowHockeyImpact(Vector2 position, Vector2 rebound, AudioClip sound, bool paddle)
        {
            if (paddle) PlayEffect(sound);
            ShakeImpact(position, rebound * (paddle ? .3f : .13f), allowOffscreen: true);
            Flash(position, Vector2.one * (paddle ? .75f : .45f),
                paddle ? new Color(1, .76f, .4f, .76f) : new Color(.52f, .87f, 1, .72f), .08f);
            Burst(rockDustMesh, position, new Vector2(1.15f, .7f), new Color(.87f, .72f, .51f), .23f,
                rebound * 2.5f, 2.4f);
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
        public bool SpawnProjectile(Vector2 position, Vector2 velocity, float damage, bool hostile, Sprite sprite = null)
            => SpawnProjectile(position, velocity, damage, hostile, sprite, null, RockFlight.Straight) != null;

        internal ArenaProjectile SpawnRock(BossPattern pattern, Vector2 position, Vector2 velocity, float damage,
            RockFlight flight = RockFlight.Straight, float holdTime = 0, bool healingMine = false)
        {
            RockAppearance appearance = default;
            if (fallingRocks != null && fallingRocks.Length > 0)
                appearance = fallingRocks[Random.Range(0, fallingRocks.Length)];
            Sprite shell = flight is RockFlight.Falling or RockFlight.Mine || pattern == BossPattern.fist_rolling_mines
                ? appearance.shell : null;
            ArenaProjectile rock = SpawnProjectile(position, velocity, damage, true, appearance.solid ? appearance.solid : rockSprite,
                shell, flight, true, holdTime, pattern);
            if (rock && healingMine) rock.MarkHealingMine();
            return rock;
        }

        private ArenaProjectile SpawnProjectile(Vector2 position, Vector2 velocity, float damage, bool hostile,
            Sprite sprite, Sprite shell, RockFlight flight, bool rock = false, float holdTime = 0, BossPattern? pattern = null)
        {
            foreach (var item in projectiles)
            {
                if (item.gameObject.activeSelf) continue;
                item.transform.position = position;
                item.Initialize(this, velocity, damage, hostile, sprite, shell, flight, rock, holdTime, pattern);
                item.gameObject.SetActive(true);
                return item;
            }
            return null;
        }

        internal ArenaProjectile FirstRockOnPath(Vector2 from, Vector2 to, bool hostile, bool skipping, out float time)
        {
            time = float.PositiveInfinity;
            ArenaProjectile nearest = null;
            foreach (var item in projectiles)
            {
                if (hostile && !item.Planted) continue;
                float contact = hostile
                    ? ArenaProjectile.ContactTime(from, to, item.transform.position, ArenaProjectile.PlantedHitRadius)
                    : item.BulletContactTime(from, to);
                if (contact >= time || skipping && contact == 0) continue;
                nearest = item;
                time = contact;
            }
            return nearest;
        }

        internal void ExplodeRock(BossPattern pattern, Vector2 center, float radius, bool launchUpward)
        {
            ShakeImpact(center, Vector2.down * .25f);
            Flash(center, Vector2.one * (radius * 2), new Color(1, .28f, .12f, .42f), .12f);
            ShowRockBreak(center + Vector2.up * .3f, new Vector2(radius * 1.35f, radius));
            var origin = new Vector2(center.x, GroundY + .9f);
            float startAngle = launchUpward ? Mathf.PI / 4 : 0;
            float angleStep = launchUpward ? Mathf.PI / 8 : Mathf.PI / 4;
            float speed = launchUpward ? 11 : 6;
            for (int shard = 0; shard < 5; shard++)
            {
                float angle = startAngle + shard * angleStep;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                SpawnRock(pattern, origin + direction * .25f, direction * speed, 20, RockFlight.Burst);
            }
        }
    }
}

