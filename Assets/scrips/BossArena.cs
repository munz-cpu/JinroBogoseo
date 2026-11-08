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
        [SerializeField, InspectorName("낙석 외형 세트")] private RockAppearance[] fallingRocks;
        [SerializeField, Range(0, 1), InspectorName("바닥에 박힐 확률")] private float rockPlantChance = .45f;
        [SerializeField, Min(.1f), InspectorName("폭발까지 걸리는 시간")] private float rockFuse = 4;
        [SerializeField, InspectorName("돌 폭발")] private AudioClip rockExplosionSound;
        [SerializeField, Range(0, 2), InspectorName("충돌 화면 흔들림")] private float shakeStrength = 1;
        private Vector3 playerStart, bossStart;
        private AudioSource effectsAudio;
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
        public bool Finished => Player.Health.Dead || Boss.Health.Dead;
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
            if (arenaCamera) cameraBrain = arenaCamera.GetComponent<CinemachineBrain>();
            playerStart = player.transform.position; bossStart = boss.transform.position;
            Player.Initialize(this); Boss.Initialize(this);
            Player.Health.InitializeAudio(this);
            Boss.Health.InitializeAudio(this);
            ResetFight();
        }

        internal void ShakeImpact(Vector2 position, Vector2 impulse)
        {
            if (shakeStrength <= 0 || !cameraBrain
                || !(cameraBrain.ActiveVirtualCamera is CinemachineVirtualCameraBase activeCamera)) return;

            Vector3 viewport = arenaCamera.WorldToViewportPoint(position);
            if (viewport.x < -.05f || viewport.x > 1.05f || viewport.y < -.05f || viewport.y > 1.05f) return;
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
            if (effectsAudio) effectsAudio.Stop();
            foreach (var item in projectiles) item.gameObject.SetActive(false);
            foreach (var item in effects) item.gameObject.SetActive(false);
            Player.transform.position = playerStart; Boss.transform.position = bossStart;
            Player.Health.ResetHealth(Player.Health.Maximum); Boss.Health.ResetHealth(Boss.Health.Maximum);
            Player.ResetState(); Boss.ResetState();
        }

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
            if (Finished && Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame) ResetFight();
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
        public bool SpawnProjectile(Vector2 position, Vector2 velocity, float damage, bool hostile)
            => SpawnProjectile(position, velocity, damage, hostile, null, null, RockFlight.Straight);

        internal void SpawnRock(Vector2 position, Vector2 velocity, float damage, RockFlight flight = RockFlight.Straight)
        {
            RockAppearance appearance = default;
            if (flight == RockFlight.Falling && fallingRocks != null && fallingRocks.Length > 0)
                appearance = fallingRocks[Random.Range(0, fallingRocks.Length)];
            SpawnProjectile(position, velocity, damage, true, appearance.solid ? appearance.solid : rockSprite,
                appearance.solid ? appearance.shell : null, flight);
        }

        private bool SpawnProjectile(Vector2 position, Vector2 velocity, float damage, bool hostile,
            Sprite sprite, Sprite shell, RockFlight flight)
        {
            foreach (var item in projectiles)
            {
                if (item.gameObject.activeSelf) continue;
                item.transform.position = position;
                item.Initialize(this, velocity, damage, hostile, sprite, shell, flight);
                item.gameObject.SetActive(true);
                return true;
            }
            return false;
        }

        internal ArenaProjectile FirstRockOnPath(Vector2 from, Vector2 to, bool skipping, out float time)
        {
            time = float.PositiveInfinity;
            ArenaProjectile nearest = null;
            foreach (var item in projectiles)
            {
                if (!item.Planted) continue;
                float contact = ArenaProjectile.ContactTime(from, to, item.transform.position, .8f);
                if (contact >= time || skipping && contact == 0) continue;
                nearest = item;
                time = contact;
            }
            return nearest;
        }

        internal bool StrikeRocks(Vector2 from, Vector2 to, float radius, int strike = 0)
        {
            bool hit = false;
            foreach (var item in projectiles)
                if (item.Planted && !float.IsPositiveInfinity(
                    ArenaProjectile.ContactTime(from, to, item.transform.position, radius))) hit |= item.Strike(strike);
            return hit;
        }

        internal void ExplodeRock(Vector2 center, float radius)
        {
            ShakeImpact(center, Vector2.down * .25f);
            PlayEffect(rockExplosionSound);
            Flash(center, Vector2.one * (radius * 2), new Color(1, .28f, .12f, .65f), .22f);
            Burst(sparkMesh, center, Vector2.one, new Color(1, .55f, .15f), .25f, Vector2.zero, radius * 2);
            if (Vector2.Distance(center, Player.transform.position) <= radius) Player.Health.Hit(20, .65f);
        }
    }
}

