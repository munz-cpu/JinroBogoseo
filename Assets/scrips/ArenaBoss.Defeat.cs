using System.Collections;
using UnityEngine;

namespace BossTemplate
{
    public sealed partial class ArenaBoss
    {
        internal float? DefeatedAt { get; private set; }
        internal bool DefeatAnimationComplete { get; private set; }

        internal void BeginDefeat()
        {
            StopAllCoroutines();
            DefeatedAt = Time.realtimeSinceStartup;
            groundMask.enabled = true;
            Intent = "골렘이 무너진다";
            StartCoroutine(CollapseGiant());
        }

        private IEnumerator CollapseGiant()
        {
            for (int i = 0; i < battleFists.Length; i++)
                if (battleFists[i].enabled) StartCoroutine(DropDefeatedFist(i));

            SpriteRenderer giant = appearance ? appearance : body;
            Transform visual = giant.transform;
            Vector3 start = visual.localPosition;
            Quaternion rotation = visual.localRotation;
            Color color = giant.color;
            bool shattered = false;
            const float duration = .86f;
            for (float elapsed = 0; elapsed < duration; elapsed += Time.deltaTime)
            {
                float progress = elapsed / duration;
                float fall = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.18f, .80f, elapsed));
                if (appearance)
                {
                    Vector3 offset = new Vector3(Mathf.Sin(elapsed * 68) * .22f * (1 - progress), -fall * 3.8f, 0);
                    visual.localPosition = start + visual.parent.InverseTransformVector(offset);
                    visual.localRotation = rotation * Quaternion.Euler(0, 0, -10 * fall);
                }

                Color tint = Color.Lerp(color, ArenaProjectile.HitFlashColor, .55f * (1 - Mathf.Clamp01(elapsed / .15f)));
                tint.a = color.a * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.48f, duration, elapsed)));
                giant.color = tint;
                if (!shattered && elapsed >= .48f)
                {
                    arena.ShowGolemCollapse(giant.bounds);
                    shattered = true;
                }
                yield return null;
            }

            if (!shattered) arena.ShowGolemCollapse(giant.bounds);
            SetHidden(true);
            yield return new WaitForSeconds(1.6f);
            Intent = "골렘 격파";
            DefeatAnimationComplete = true;
        }

        private IEnumerator DropDefeatedFist(int index)
        {
            SpriteRenderer fist = battleFists[index];
            fist.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            float delay = .07f * index;
            if (delay > 0) yield return new WaitForSeconds(delay);

            Transform hand = fist.transform;
            Vector3 start = hand.position;
            Quaternion rotation = hand.rotation;
            float direction = index % 2 == 0 ? -1 : 1;
            float x = ClampFistX(start.x + direction * .65f, fist);
            float y = arena.GroundY + start.y - fist.bounds.min.y;
            var landing = new Vector3(x, y, start.z);
            float duration = Mathf.Lerp(.28f, .63f, Mathf.Clamp01((start.y - y) / 8));
            for (float elapsed = 0; elapsed < duration; elapsed += Time.deltaTime)
            {
                float t = elapsed / duration;
                hand.position = Vector3.Lerp(start, landing, t * t);
                hand.rotation = rotation * Quaternion.Euler(0, 0, direction * 36 * t);
                yield return null;
            }

            fist.enabled = false;
            var impact = new Vector2(x, arena.GroundY + .12f);
            arena.ShowRockLandingDust(impact, fist.bounds.size.x);
            arena.ShakeImpact(impact, Vector2.down * .13f);
        }
    }
}
