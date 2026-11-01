using System.Collections;
using UnityEngine;

namespace BossTemplate
{
    public sealed class ArenaBoss : MonoBehaviour
    {
        [SerializeField, InspectorName("공격 간격")] private float attackInterval = 1.3f;
        [SerializeField, InspectorName("공격 예고 시간")] private float warningTime = .8f;
        private BossArena arena;
        private SpriteRenderer body;
        private Color baseColor;
        public FighterHealth Health { get; private set; }
        public bool Enraged => Health.Current <= Health.Maximum * .5f;
        public string Intent { get; private set; } = "전투 준비";
        public void Initialize(BossArena owner) { arena = owner; Health = GetComponent<FighterHealth>(); body = GetComponent<SpriteRenderer>(); baseColor = body.color; }

        public void ResetState()
        {
            StopAllCoroutines(); body.color = baseColor; Intent = "전투 준비";
            StartCoroutine(Fight());
        }

        private IEnumerator Fight()
        {
            yield return new WaitForSeconds(1.5f);
            int pattern = 0;
            while (!arena.Finished)
            {
                Intent = pattern % 3 == 0 ? "부채꼴 탄막" : pattern % 3 == 1 ? "지면 강타" : "돌진";
                body.color = Color.white;
                float targetX = arena.Player.transform.position.x;
                GameObject warning = null;
                if (pattern % 3 == 1)
                    warning = arena.Flash(new Vector2(targetX, arena.GroundY + 2.25f), new Vector2(2.2f, 4.5f), new Color(1, .25f, .3f, .22f), warningTime);
                if (pattern % 3 == 2)
                    warning = arena.Flash(new Vector2(0, arena.GroundY + .7f), new Vector2(21, 1.4f), new Color(1, .65f, .2f, .22f), warningTime);
                yield return new WaitForSeconds(warningTime);
                if (arena.Finished) { if (warning) warning.SetActive(false); break; }
                body.color = baseColor;
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
                    float destination = start > 0 ? -8 : 8;
                    float elapsed = 0;
                    while (elapsed < .65f && !arena.Finished)
                    {
                        elapsed += Time.deltaTime;
                        transform.position = new Vector2(Mathf.Lerp(start, destination, elapsed / .65f), arena.GroundY + body.bounds.extents.y);
                        if (Mathf.Abs(arena.Player.transform.position.x - transform.position.x) < 1.5f && Mathf.Abs(arena.Player.transform.position.y - transform.position.y) < 2.1f) arena.Player.Health.Hit(20, .8f);
                        yield return null;
                    }
                }
                pattern++;
                Intent = Enraged ? "분노 상태" : "다음 공격 준비";
                yield return new WaitForSeconds(attackInterval * (Enraged ? .55f : 1));
            }
            Intent = "전투 종료";
        }
    }
}
