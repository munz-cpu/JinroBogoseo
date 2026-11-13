using UnityEngine;

namespace BossTemplate
{
    public sealed class FighterHealth : MonoBehaviour
    {
        [SerializeField, InspectorName("최대 체력")] private float maximum = 100;
        [SerializeField] private Material hitFlashMaterial;
        [SerializeField] private SpriteRenderer[] hitRenderers;
        [Header("효과음")]
        [SerializeField, InspectorName("피격")] private AudioClip hitSound;
        [SerializeField, InspectorName("쓰러짐")] private AudioClip defeatSound;
        private BossArena arena;
        internal const float HitFlashDuration = .16f;
        private static readonly int FlashOpacity = Shader.PropertyToID("_FlashOpacity");
        private float hitFlashUntil;
        private float dashImmuneUntil;
        private SpriteRenderer[] flashOverlays;
        private MaterialPropertyBlock flashProperties;
        public float Current { get; private set; }
        public float Maximum => maximum;
        public bool Dead => Current <= 0;
        internal float LastDamageTime { get; private set; } = float.NegativeInfinity;
        public float InvulnerableUntil { get; set; }
#if UNITY_EDITOR
        public bool DebugInvincible { get; set; }
#endif
        internal void InitializeAudio(BossArena owner) => arena = owner;
        internal void BeginDashImmunity(float duration) => dashImmuneUntil = Time.time + duration;

        private void Awake()
        {
            if (!hitFlashMaterial || hitRenderers == null || hitRenderers.Length == 0) return;
            flashProperties = new MaterialPropertyBlock();
            flashOverlays = new SpriteRenderer[hitRenderers.Length];
            for (int i = 0; i < hitRenderers.Length; i++)
            {
                if (!hitRenderers[i]) continue;
                var overlay = new GameObject("Hit Flash").AddComponent<SpriteRenderer>();
                overlay.transform.SetParent(hitRenderers[i].transform, false);
                overlay.sharedMaterial = hitFlashMaterial;
                overlay.enabled = false;
                flashOverlays[i] = overlay;
            }
        }

        private void LateUpdate()
        {
            if (hitFlashUntil <= 0) return;
            float remaining = hitFlashUntil - Time.time;
            if (remaining <= 0)
            {
                ClearHitFlash();
                return;
            }
            DrawHitFlash(Mathf.Clamp01(remaining / HitFlashDuration));
        }

        private void OnDisable()
        {
            dashImmuneUntil = 0;
            ClearHitFlash();
        }

        private void DrawHitFlash(float opacity)
        {
            if (flashOverlays == null) return;
            for (int i = 0; i < flashOverlays.Length; i++)
            {
                SpriteRenderer overlay = flashOverlays[i];
                SpriteRenderer source = hitRenderers[i];
                if (!overlay || !source) continue;
                overlay.enabled = source.enabled && source.sprite;
                if (!overlay.enabled) continue;
                overlay.sprite = source.sprite;
                overlay.flipX = source.flipX;
                overlay.flipY = source.flipY;
                overlay.sortingLayerID = source.sortingLayerID;
                overlay.sortingOrder = source.sortingOrder + 1;
                overlay.maskInteraction = source.maskInteraction;
                flashProperties.SetFloat(FlashOpacity, opacity * source.color.a);
                overlay.SetPropertyBlock(flashProperties);
            }
        }

        private void ClearHitFlash()
        {
            hitFlashUntil = 0;
            if (flashOverlays == null) return;
            foreach (var overlay in flashOverlays)
                if (overlay) overlay.enabled = false;
        }

        public void ResetHealth(float value)
        {
            maximum = value;
            Current = value;
            LastDamageTime = float.NegativeInfinity;
            InvulnerableUntil = dashImmuneUntil = 0;
            ClearHitFlash();
        }
        public void Restore(float amount) { if (!Dead) Current = Mathf.Min(maximum, Current + Mathf.Max(0, amount)); }
        public bool Hit(float damage, float immunity = 0, bool flash = true) =>
            Hit(damage, immunity, flash, null);

        internal bool Hit(float damage, float immunity, bool flash, BossPattern? pattern)
        {
#if UNITY_EDITOR
            if (DebugInvincible) return false;
#endif
            if (Dead || Time.time < InvulnerableUntil || Time.time < dashImmuneUntil) return false;
            float remaining = Mathf.Max(0, Current - Mathf.Max(0, damage));
            float lostHealth = Current - remaining;
            Current = remaining;
            InvulnerableUntil = Time.time + immunity;
            if (lostHealth > 0)
            {
                if (arena) arena.Study?.RecordDamage(this, lostHealth, pattern);
                LastDamageTime = Time.time;
                if (flash) hitFlashUntil = Time.time + HitFlashDuration;
                if (arena)
                    arena.PlayEffect(Dead && defeatSound ? defeatSound : hitSound,
                        stopCurrent: Dead && this == arena.Player.Health);
            }
            return true;
        }
    }
}
