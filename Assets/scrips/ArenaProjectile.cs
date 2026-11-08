using UnityEngine;

namespace BossTemplate
{
    internal enum RockFlight { Straight, Falling, Skipping, Launch }

    public sealed class ArenaProjectile : MonoBehaviour
    {
        private const float RockGravity = 25;
        private const float ExplosionRadius = 2.2f;
        // Behind the ground tiles (0), in front of the scenery.
        private const int PlantedSortingOrder = -1;
        internal const float PlantedHalfWidth = .5f;
        internal const float PlantedHeight = .85f;
        private BossArena arena;
        private SpriteRenderer surface;
        private SpriteRenderer rockShell;
        private Sprite shellSprite;
        private Sprite defaultSprite;
        private int defaultSortingOrder;
        private Vector2 velocity;
        private float damage, radius, expires, fuse;
        private bool hostile;
        private RockFlight flight;
        private int rockHits, lastStrike;
        internal bool Planted => gameObject.activeSelf && rockHits > 0;

        internal void Initialize(BossArena owner, Vector2 speed, float amount, bool enemy,
            Sprite sprite, Sprite shell, RockFlight mode)
        {
            if (!surface)
            {
                surface = GetComponent<SpriteRenderer>();
                defaultSprite = surface.sprite;
                defaultSortingOrder = surface.sortingOrder;
            }
            arena = owner;
            velocity = speed;
            damage = amount;
            hostile = enemy;
            flight = mode;
            radius = sprite ? mode switch
            {
                RockFlight.Falling => .65f,
                RockFlight.Skipping => .8f,
                _ => .45f
            } : enemy ? .65f : 1.35f;
            expires = Time.time + (mode == RockFlight.Launch ? .65f : 5);
            rockHits = lastStrike = 0;
            transform.localScale = sprite ? Vector3.one * (mode switch
            {
                RockFlight.Falling => 1.55f,
                RockFlight.Skipping => 1.8f,
                _ => .8f
            }) : enemy ? new Vector3(.35f, .35f, 1) : new Vector3(.5f, .18f, 1);
            surface.sprite = sprite ? sprite : defaultSprite;
            surface.sortingOrder = defaultSortingOrder;
            surface.color = sprite || !enemy ? Color.white : Color.red;
            shellSprite = shell;
            if (rockShell) rockShell.enabled = false;
        }

        internal bool Strike(int strike = 0)
        {
            if (!Planted || strike != 0 && strike == lastStrike) return false;
            lastStrike = strike;
            if (--rockHits == 0)
            {
                arena.ShakeImpact(transform.position, Vector2.down * .09f);
                gameObject.SetActive(false);
            }
            return true;
        }

        private void Update()
        {
            if (arena.Finished) { gameObject.SetActive(false); return; }
            if (Planted)
            {
                if (Time.time >= expires)
                {
                    arena.ExplodeRock(transform.position, ExplosionRadius);
                    gameObject.SetActive(false);
                }
                else if (rockShell && rockShell.enabled)
                {
                    float elapsed = fuse - (expires - Time.time);
                    float progress = elapsed / fuse;
                    float phase = Mathf.Repeat(elapsed * (.55f + .55f * progress), 1);
                    float firstBeat = Mathf.Clamp01(1 - Mathf.Abs(phase - .16f) / .15f);
                    float secondBeat = Mathf.Clamp01(1 - Mathf.Abs(phase - .41f) / .11f) * .55f;
                    surface.color = Color.Lerp(Color.black, Color.red,
                        Mathf.SmoothStep(0, 1, Mathf.Max(firstBeat, secondBeat)));
                }
                return;
            }
            if (flight != RockFlight.Skipping && Time.time >= expires)
            {
                gameObject.SetActive(false);
                return;
            }

            Vector2 from = transform.position;
            float step = Time.deltaTime;
            if (flight == RockFlight.Skipping) velocity.y -= RockGravity * step;
            Vector2 to = from + velocity * step;
            float floor = arena.GroundY + (flight == RockFlight.Skipping && surface.sprite != defaultSprite ? .82f : .25f);
            bool reachedFloor = (flight == RockFlight.Falling || flight == RockFlight.Skipping)
                && velocity.y < 0 && to.y <= floor;
            if (reachedFloor)
                to = Vector2.Lerp(from, to, Mathf.InverseLerp(from.y, to.y, floor));

            if (flight != RockFlight.Launch && ResolveHit(from, to)) return;
            if (reachedFloor)
            {
                arena.ShakeImpact(to, Vector2.down * (flight == RockFlight.Skipping ? .055f : .14f));
                if (flight == RockFlight.Skipping)
                {
                    velocity.y = 6.5f;
                }
                else
                {
                    if (Random.value >= arena.RockPlantChance || arena.Player.OverlapsPlantedRock(to.x))
                    {
                        gameObject.SetActive(false);
                        return;
                    }
                    to.y = floor;
                    rockHits = 3;
                    fuse = arena.RockFuse;
                    expires = Time.time + fuse;
                    transform.localScale = Vector3.one * 1.35f;
                    surface.sortingOrder = PlantedSortingOrder;
                    ShowRockShell();
                }
            }
            transform.position = to;
            if (flight == RockFlight.Skipping && (to.x < arena.GroundBounds.min.x || to.x > arena.GroundBounds.max.x))
                gameObject.SetActive(false);
        }

        private void ShowRockShell()
        {
            if (!shellSprite) return;
            if (!rockShell)
            {
                var layer = new GameObject("Rock Shell");
                layer.transform.SetParent(transform, false);
                rockShell = layer.AddComponent<SpriteRenderer>();
            }
            rockShell.sprite = shellSprite;
            rockShell.sharedMaterial = surface.sharedMaterial;
            rockShell.sortingLayerID = surface.sortingLayerID;
            rockShell.sortingOrder = PlantedSortingOrder;
            surface.sortingOrder = PlantedSortingOrder - 1;
            surface.color = Color.black;
            rockShell.enabled = true;
        }

        private bool ResolveHit(Vector2 from, Vector2 to)
        {
            FighterHealth target = hostile ? arena.Player.Health : arena.Boss.Health;
            float targetTime = hostile || !arena.Boss.Hidden
                ? ContactTime(from, to, target.transform.position, radius) : float.PositiveInfinity;
            ArenaProjectile rock = arena.FirstRockOnPath(from, to, flight == RockFlight.Skipping, out float rockTime);
            if (rock && rockTime <= targetTime)
            {
                rock.Strike();
                if (flight == RockFlight.Skipping) return false;
                gameObject.SetActive(false);
                return true;
            }
            if (float.IsPositiveInfinity(targetTime)) return false;

            if (target.Hit(damage, hostile ? .65f : 0) && hostile)
                arena.ShakeImpact(Vector2.Lerp(from, to, targetTime), velocity.normalized * .13f);
            if (flight == RockFlight.Skipping) return false;
            gameObject.SetActive(false);
            return true;
        }

        internal static float ContactTime(Vector2 from, Vector2 to, Vector2 center, float radius)
        {
            Vector2 offset = from - center;
            float c = offset.sqrMagnitude - radius * radius;
            if (c <= 0) return 0;
            Vector2 step = to - from;
            float a = step.sqrMagnitude;
            float b = Vector2.Dot(offset, step);
            float discriminant = b * b - a * c;
            if (a <= 0 || b >= 0 || discriminant < 0) return float.PositiveInfinity;
            float time = (-b - Mathf.Sqrt(discriminant)) / a;
            return time <= 1 ? time : float.PositiveInfinity;
        }
    }
}
