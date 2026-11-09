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
        [SerializeField, InspectorName("플레이어")] private SideViewPlayer player;
        [SerializeField, InspectorName("보스")] private ArenaBoss boss;
        [SerializeField, InspectorName("경기장 카메라")] private Camera arenaCamera;
        [SerializeField, InspectorName("탄환 목록")] private ArenaProjectile[] projectiles;
        [SerializeField, InspectorName("공격 표시 목록")] private ArenaEffect[] effects;
        [SerializeField, InspectorName("섬광 메시")] private Mesh sparkMesh;
        [SerializeField, InspectorName("점프 링 메시")] private Mesh jumpRingMesh;
        [Header("낙석")]
        [SerializeField, InspectorName("돌 스프라이트")] private Sprite rockSprite;
        [SerializeField, InspectorName("돌 외형 세트")] private RockAppearance[] fallingRocks;
        [SerializeField, Range(0, 1), InspectorName("바닥에 박힐 확률")] private float rockPlantChance = .45f;
        [SerializeField, Min(.1f), InspectorName("폭발까지 걸리는 시간")] private float rockFuse = 4;
        [SerializeField, InspectorName("돌 폭발 효과음")] private AudioClip rockExplosionSound;
        [SerializeField, InspectorName("낙석 착지 효과음")] private AudioClip rockLandingSound;
        [SerializeField, Range(0, 2), InspectorName("충돌 화면 흔들림")] private float shakeStrength = 1;
        private Vector3 playerStart, bossStart;
        private float phaseOneHealth;
        private AudioSource effectsAudio;
        private Mesh rockDustMesh;
        private ArenaHealthOrb healthOrb;
        private CinemachineBrain cameraBrain;
        private CinemachineVirtualCameraBase shakeCamera;
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
        public bool Finished => (Study && !Study.InCombat) || CombatEnded;
        internal bool CombatEnded => Player.Health.Dead || (Boss.Phase == 2 && Boss.Health.Dead);
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
            rockDustMesh = CreateRockDustMesh();
            if (arenaCamera) cameraBrain = arenaCamera.GetComponent<CinemachineBrain>();
            playerStart = player.transform.position; bossStart = boss.transform.position;
            Player.Initialize(this); Boss.Initialize(this);
            phaseOneHealth = Boss.Health.Maximum;
            Player.Health.InitializeAudio(this);
            Boss.Health.InitializeAudio(this);
            ResetFight();
        }

        internal void AttachStudy(StudySession study)
        {
            Study = study;
            EndStudyFight();
        }

        private void OnDestroy() => Destroy(rockDustMesh);

        internal void ShakeImpact(Vector2 position, Vector2 impulse, bool allowOffscreen = false)
        {
            if (shakeStrength <= 0 || !cameraBrain
                || !(cameraBrain.ActiveVirtualCamera is CinemachineVirtualCameraBase activeCamera)) return;

            if (!allowOffscreen)
            {
                Vector3 viewport = arenaCamera.WorldToViewportPoint(position);
                if (viewport.x < -.05f || viewport.x > 1.05f || viewport.y < -.05f || viewport.y > 1.05f) return;
            }
            if (shakeCamera != activeCamera)
            {
                if (!activeCamera.TryGetComponent(out CinemachineImpulseListener listener))
                {
                    listener = activeCamera.gameObject.AddComponent<CinemachineImpulseListener>();
                    listener.ChannelMask = impactImpulse.ImpulseChannel;
                    listener.Gain = 1;
                    listener.ApplyAfter = CinemachineCore.Stage.Noise;
                    listener.SignalCombinationMode = CinemachineImpulseListener.SignalCombinationModes.UseLargest;
                    listener.ReactionSettings = default;
                }
                shakeCamera = activeCamera;
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
            Player.transform.position = playerStart; Boss.transform.position = bossStart;
            Player.Health.ResetHealth(Player.Health.Maximum);
            Boss.Health.ResetHealth(phaseOneHealth * (Study ? Study.BossHealthFactor : 1));
            Player.ResetState(); Boss.ResetState();
        }

        internal void EndStudyFight()
        {
            Boss.StopAllCoroutines();
            ClearAttacks();
        }

        private void ClearAttacks()
        {
            if (effectsAudio) effectsAudio.Stop();
            if (healthOrb) healthOrb.gameObject.SetActive(false);
            foreach (var item in projectiles) item.gameObject.SetActive(false);
            foreach (var item in effects) item.gameObject.SetActive(false);
        }

        internal void DropHealthOrb(Vector2 position, Material material)
        {
            if (!healthOrb)
            {
                var pickup = new GameObject("회복 구슬");
                pickup.transform.SetParent(transform, true);
                healthOrb = pickup.AddComponent<ArenaHealthOrb>();
                healthOrb.Initialize(this, material);
            }
            healthOrb.Drop(position);
        }

        internal void ShowHealthOrbPickup(Vector2 position)
        {
            Burst(jumpRingMesh, position, Vector2.one * 1.2f, new Color(.35f, 1, .45f), .32f,
                Vector2.up * .6f, 2);
        }

#if UNITY_EDITOR
        public void SkipCurrentPhase()
        {
            if (Study || Finished || Boss.Transitioning) return;
            Boss.Health.InvulnerableUntil = 0;
            Boss.Health.Hit(Boss.Health.Current);
            AdvancePhaseIfDefeated();
        }
#endif

        internal void PlayEffect(AudioClip clip)
        {
            if (!clip) return;
            if (!effectsAudio)
            {
                effectsAudio = gameObject.AddComponent<AudioSource>();
                effectsAudio.playOnAwake = false;
                effectsAudio.spatialBlend = 0;
            }
            effectsAudio.PlayOneShot(clip);
        }
        private void Update()
        {
            AdvancePhaseIfDefeated();
            if (!Study && Finished && Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame) ResetFight();
        }

        private void AdvancePhaseIfDefeated()
        {
            if (Finished || Boss.Phase != 1 || Boss.Transitioning || !Boss.Health.Dead) return;
            ClearAttacks();
            Boss.BeginPhaseTwo();
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
        internal void ShowRockBreak(Vector2 position, Vector2 size)
        {
            PlayEffect(rockExplosionSound);
            Burst(rockDustMesh, position, size, new Color(.79f, .68f, .53f), .42f,
                Vector2.up * .85f, 2.6f);
        }

        internal void ShowRockLandingDust(Vector2 position, float width, bool playSound = true)
        {
            if (playSound) PlayEffect(rockLandingSound);
            Burst(rockDustMesh, position, new Vector2(width, .55f), new Color(.79f, .68f, .53f, .8f),
                .3f, Vector2.up * .65f, 2f);
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

        private static Mesh CreateRockDustMesh()
        {
            // One pooled mesh draws the whole burst, leaving room for attack warnings.
            const int particleCount = 20;
            var vertices = new Vector3[particleCount * 4];
            var triangles = new int[particleCount * 6];
            for (int i = 0; i < particleCount; i++)
            {
                float angle = i * 2.4f;
                float distance = Mathf.Sqrt((i + .5f) / particleCount) * .45f;
                var center = new Vector3(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance, 0);
                float halfSize = .015f + (i % 4) * .008f;
                int v = i * 4;
                vertices[v] = center + new Vector3(-halfSize, -halfSize, 0);
                vertices[v + 1] = center + new Vector3(-halfSize, halfSize, 0);
                vertices[v + 2] = center + new Vector3(halfSize, halfSize, 0);
                vertices[v + 3] = center + new Vector3(halfSize, -halfSize, 0);
                int t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;
            }
            return new Mesh { name = "Rock Dust", vertices = vertices, triangles = triangles };
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
            Sprite shell = flight is RockFlight.Falling or RockFlight.Mine ? appearance.shell : null;
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

