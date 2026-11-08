using UnityEngine;

namespace BossTemplate
{
    public sealed class ArenaHealthOrb : MonoBehaviour
    {
        private const float HealAmount = 10;
        private const float Lifetime = 12;
        private BossArena arena;
        private Sprite sprite;
        private Texture2D texture;
        private Vector2 position;
        private float expires;

        internal void Initialize(BossArena owner, Material material)
        {
            arena = owner;
            const int size = 24;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 offset = new Vector2(x + .5f, y + .5f) / (size * .5f) - Vector2.one;
                float radius = offset.magnitude;
                Color color = Color.Lerp(new Color(.92f, 1, .78f), new Color(.13f, 1, .32f), radius * 1.5f);
                color.a = 1 - Mathf.SmoothStep(0, 1, (radius - .46f) / .54f);
                pixels[y * size + x] = color;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            sprite = Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * .5f, size);
            var renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 4;
        }

        internal void Drop(Vector2 origin)
        {
            Bounds ground = arena.GroundBounds;
            position = new Vector2(Mathf.Clamp(origin.x, ground.min.x + .5f, ground.max.x - .5f),
                Mathf.Max(origin.y, arena.GroundY + .8f));
            transform.position = position;
            expires = Time.time + Lifetime;
            gameObject.SetActive(true);
        }

        private void Update()
        {
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

        private void OnDestroy()
        {
            Destroy(sprite);
            Destroy(texture);
        }
    }
}
