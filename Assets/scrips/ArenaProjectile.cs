using UnityEngine;

namespace BossTemplate
{
    public sealed class ArenaProjectile : MonoBehaviour
    {
        private BossArena arena;
        private FighterHealth target;
        private Vector2 velocity;
        private float damage, radius, expires;
        private bool hostile;

        public void Initialize(BossArena owner, FighterHealth victim, Vector2 speed, float amount, float hitRadius, bool enemy)
        {
            arena = owner; target = victim; velocity = speed; damage = amount;
            radius = hitRadius; hostile = enemy; expires = Time.time + 5;
        }

        private void Update()
        {
            if (arena.Finished || Time.time > expires) { gameObject.SetActive(false); return; }
            Vector2 from = transform.position;
            Vector2 to = from + velocity * Time.deltaTime;
            transform.position = to;
            if (!target || target.Dead) return;
            Vector2 segment = to - from;
            float t = segment.sqrMagnitude < .00001f ? 0 : Mathf.Clamp01(Vector2.Dot((Vector2)target.transform.position - from, segment) / segment.sqrMagnitude);
            if (Vector2.Distance(from + segment * t, target.transform.position) < radius)
            {
                target.Hit(damage, hostile ? .65f : 0);
                gameObject.SetActive(false);
            }
        }
    }
}
