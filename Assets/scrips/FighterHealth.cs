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
        private const float HitFlashDuration = .10f;
        private float hitFlashUntil;
        private Material[] normalMaterials;
        private bool showingHitFlash;
        public float Current { get; private set; }
        public float Maximum => maximum;
        public bool Dead => Current <= 0;
        internal float LastDamageTime { get; private set; } = float.NegativeInfinity;
        public float InvulnerableUntil { get; set; }
#if UNITY_EDITOR
        public bool DebugInvincible { get; set; }
#endif
        internal void InitializeAudio(BossArena owner) => arena = owner;
        private void Awake()
        {
            if (!hitFlashMaterial || hitRenderers == null || hitRenderers.Length == 0) return;
            normalMaterials = new Material[hitRenderers.Length];
            for (int i = 0; i < hitRenderers.Length; i++)
                if (hitRenderers[i]) normalMaterials[i] = hitRenderers[i].sharedMaterial;
        }

        private void LateUpdate() => SetHitFlash(Time.time < hitFlashUntil);
        private void OnDisable() => SetHitFlash(false);

        private void SetHitFlash(bool visible)
        {
            if (normalMaterials == null || showingHitFlash == visible) return;
            showingHitFlash = visible;
            for (int i = 0; i < hitRenderers.Length; i++)
                if (hitRenderers[i]) hitRenderers[i].sharedMaterial = visible ? hitFlashMaterial : normalMaterials[i];
        }
        public void ResetHealth(float value)
        {
            maximum = value;
            Current = value;
            LastDamageTime = float.NegativeInfinity;
            InvulnerableUntil = hitFlashUntil = 0;
            SetHitFlash(false);
        }
        public void Restore(float amount) { if (!Dead) Current = Mathf.Min(maximum, Current + Mathf.Max(0, amount)); }
        public bool Hit(float damage, float immunity = 0, bool flash = true)
        {
#if UNITY_EDITOR
            if (DebugInvincible) return false;
#endif
            if (Dead || Time.time < InvulnerableUntil) return false;
            float remaining = Mathf.Max(0, Current - Mathf.Max(0, damage));
            bool lostHealth = remaining < Current;
            Current = remaining;
            InvulnerableUntil = Time.time + immunity;
            if (lostHealth)
            {
                LastDamageTime = Time.time;
                if (flash) hitFlashUntil = Time.time + HitFlashDuration;
                if (arena) arena.PlayEffect(Dead && defeatSound ? defeatSound : hitSound);
            }
            return true;
        }
    }
}
