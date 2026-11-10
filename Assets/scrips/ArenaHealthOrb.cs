using UnityEngine;

namespace BossTemplate
{
    public sealed class ArenaHealthOrb : MonoBehaviour
    {
        private const float HealAmount = 10;
        private const float Lifetime = 12;
        private BossArena arena;
        [SerializeField] private SpriteRenderer appearance;
        private Vector2 position;
        private float expires;

        internal void Initialize(BossArena owner) => arena = owner;

        internal void Drop(Vector2 origin, Material material)
        {
            appearance.sharedMaterial = material;
            Bounds ground = arena.GroundBounds;
            position = new Vector2(Mathf.Clamp(origin.x, ground.min.x + .5f, ground.max.x - .5f),
                Mathf.Max(origin.y, arena.GroundY + .8f));
            transform.position = position;
            expires = Time.time + Lifetime;
            gameObject.SetActive(true);
        }

        private void Update()
        {
            if (arena.Boss.Transitioning) expires += Time.deltaTime;
            if (arena.Finished || Time.time >= expires)
            {
                gameObject.SetActive(false);
                return;
            }

            float hoverY = arena.GroundY + .58f;
            position.y = Mathf.MoveTowards(position.y, hoverY, Time.deltaTime * 9);
            Vector2 display = position + Vector2.up * (Mathf.Sin(Time.time * 7) * .10f);
            transform.position = display;

            SideViewPlayer player = arena.Player;
            Vector2 distance = (Vector2)player.transform.position - display;
            Vector2 reach = player.HurtboxHalfSize + Vector2.one * .35f;
            if (player.Health.Current >= player.Health.Maximum ||
                Mathf.Abs(distance.x) > reach.x || Mathf.Abs(distance.y) > reach.y) return;

            player.Health.Restore(HealAmount);
            arena.ShowHealthOrbPickup(display);
            gameObject.SetActive(false);
        }
    }
}
