using UnityEngine;

namespace BossTemplate
{
    public sealed class FighterHealth : MonoBehaviour
    {
        [SerializeField, InspectorName("최대 체력")] private float maximum = 100;
        public float Current { get; private set; }
        public float Maximum => maximum;
        public bool Dead => Current <= 0;
        public float InvulnerableUntil { get; set; }
        public void ResetHealth(float value) { maximum = value; Current = value; InvulnerableUntil = 0; }
        public void Restore(float amount) { if (!Dead) Current = Mathf.Min(maximum, Current + Mathf.Max(0, amount)); }
        public bool Hit(float damage, float immunity = 0)
        {
            if (Dead || Time.time < InvulnerableUntil) return false;
            Current = Mathf.Max(0, Current - Mathf.Max(0, damage));
            InvulnerableUntil = Time.time + immunity;
            return true;
        }
    }
}
