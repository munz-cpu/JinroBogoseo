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
        private const float FramesPerSecond = 12;
        private BossArena arena;
        private SpriteRenderer body;
        private Color baseColor;
        private Sprite[] motion;
        private float motionStart, motionSpeed;
        private bool loopMotion;
        public FighterHealth Health { get; private set; }
        public bool Enraged => Health.Current <= Health.Maximum * .5f;
        public string Intent { get; private set; } = "전투 준비";
        public void Initialize(BossArena owner)
        {
            arena = owner;
            Health = GetComponent<FighterHealth>();
            body = GetComponent<SpriteRenderer>();
            baseColor = body.color;
        }

        public void ResetState()
        {
            StopAllCoroutines(); body.color = baseColor; Intent = "전투 준비";
            Face(arena.Player.transform.position.x - transform.position.x);
            PlayMotion(idleFrames, loop: true);
            StartCoroutine(Fight());
        }

        private void Face(float horizontal)
        {
            if (appearance && horizontal != 0) appearance.flipX = horizontal < 0;
        }

        private void PlayMotion(Sprite[] frames, float speed = 1, bool loop = false)
        {
            motion = frames;
            motionStart = Time.time;
            motionSpeed = speed;
            loopMotion = loop;
        }

        private void LateUpdate()
        {
            if (!appearance || !Health) return;
            if (Health.Dead && motion != defeatFrames) PlayMotion(defeatFrames);
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
            yield return new WaitForSeconds(1.5f);
            int pattern = 0;
            while (!arena.Finished)
            {
                Intent = pattern % 3 == 0 ? "부채꼴 탄막" : pattern % 3 == 1 ? "지면 강타" : "돌진";
                if (!appearance) body.color = Color.white;
                float targetX = arena.Player.transform.position.x;
                Face(targetX - transform.position.x);
                PlayMotion(chargeFrames, loop: true);
                GameObject warning = null;
                if (pattern % 3 == 1)
                    warning = arena.Flash(new Vector2(targetX, arena.GroundY + 2.25f), new Vector2(2.2f, 4.5f), new Color(1, .25f, .3f, .22f), warningTime);
                if (pattern % 3 == 2)
                    warning = arena.Flash(new Vector2(arena.GroundBounds.center.x, arena.GroundY + .7f), new Vector2(arena.GroundBounds.size.x, 1.4f), new Color(1, .65f, .2f, .22f), warningTime);
                Sprite[] attackMotion = pattern % 3 == 0 ? shootFrames : pattern % 3 == 1 ? slamFrames : null;
                float windup = attackMotion != null && attackMotion.Length > 0
                    ? Mathf.Min(warningTime, (pattern % 3 == 0 ? 3 : 5) / FramesPerSecond) : 0;
                if (windup > 0)
                {
                    yield return new WaitForSeconds(warningTime - windup);
                    if (!arena.Finished) PlayMotion(attackMotion);
                }
                yield return new WaitForSeconds(windup > 0 ? windup : warningTime);
                if (arena.Finished) { if (warning) warning.SetActive(false); break; }
                body.color = baseColor;
                Face(arena.Player.transform.position.x - transform.position.x);
                if (pattern % 3 == 0)
                {
                    Vector2 aim = ((Vector2)arena.Player.transform.position - (Vector2)transform.position).normalized;
                    int count = Enraged ? 7 : 5;
                    for (int i = 0; i < count; i++)
                    {
                        Vector2 direction = Quaternion.Euler(0, 0, (i - (count - 1) * .5f) * 13) * aim;
                        arena.SpawnProjectile(transform.position, direction * (Enraged ? 7 : 5), 12, true);
                    }
                }
                else if (pattern % 3 == 1)
                {
                    arena.Flash(new Vector2(targetX, arena.GroundY + 2.25f), new Vector2(2.2f, 4.5f), new Color(1, .35f, .3f, .85f), .3f);
                    if (Mathf.Abs(arena.Player.transform.position.x - targetX) < 1.5f && arena.Player.transform.position.y < arena.GroundY + 5.2f) arena.Player.Health.Hit(22, .8f);
                }
                else
                {
                    float start = transform.position.x;
                    float destination = start > arena.GroundBounds.center.x
                        ? arena.GroundBounds.min.x + body.bounds.extents.x + 1
                        : arena.GroundBounds.max.x - body.bounds.extents.x - 1;
                    float elapsed = 0;
                    float duration = Mathf.Max(.65f, Mathf.Abs(destination - start) / 25);
                    Face(destination - start);
                    PlayMotion(idleFrames, speed: 2, loop: true);
                    while (elapsed < duration && !arena.Finished)
                    {
                        elapsed += Time.deltaTime;
                        transform.position = new Vector2(Mathf.Lerp(start, destination, elapsed / duration), arena.GroundY + body.bounds.extents.y);
                        if (Mathf.Abs(arena.Player.transform.position.x - transform.position.x) < 1.5f && Mathf.Abs(arena.Player.transform.position.y - transform.position.y) < 2.1f) arena.Player.Health.Hit(20, .8f);
                        yield return null;
                    }
                    PlayMotion(idleFrames, loop: true);
                }
                pattern++;
                Intent = Enraged ? "분노 상태" : "다음 공격 준비";
                yield return new WaitForSeconds(attackInterval * (Enraged ? .55f : 1));
            }
            Intent = "전투 종료";
            if (!Health.Dead) PlayMotion(idleFrames, loop: true);
        }
    }
}
