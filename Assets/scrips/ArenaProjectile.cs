using UnityEngine;

namespace BossTemplate
{
    internal enum RockFlight { Straight, Falling, Skipping, Launch, Crashing, Burst }

    public sealed class ArenaProjectile : MonoBehaviour
    {
        private const float RockGravity = 25;
        private const float ExplosionRadius = 2.2f;
        // Behind the ground tiles (0), in front of the scenery.
        private const int PlantedSortingOrder = -1;
        private const float RockSizeMultiplier = 1.55f;
        private const float HitFlashDuration = .10f;
        internal static readonly Color HitFlashColor = new Color(.45f, .9f, 1f);
        internal const float PlantedHalfWidth = .5f * RockSizeMultiplier;
        internal const float PlantedHeight = .85f * RockSizeMultiplier;
        internal const float PlantedHitRadius = .8f * RockSizeMultiplier;
        private BossArena arena;
        private SpriteRenderer surface;
        private SpriteRenderer rockShell;
        private Sprite shellSprite;
        private Sprite defaultSprite;
        private int defaultSortingOrder;
        private Vector2 velocity;
        private float damage, radius, expires, fuse;
        private float rockGroundOffset;
        private bool hostile;
        private bool isRock;
        private bool planted;
        private Color normalColor;
        private float hitFlashUntil;
        private RockFlight flight;
        private int rockHits;
        internal bool Planted => gameObject.activeSelf && planted;

        internal void Initialize(BossArena owner, Vector2 speed, float amount, bool enemy,
            Sprite sprite, Sprite shell, RockFlight mode, bool rock)
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
            isRock = rock;
            flight = mode;
            radius = enemy ? .65f : owner.Boss.ProjectileHitRadius;
            expires = Time.time + (mode switch { RockFlight.Launch => .65f, RockFlight.Burst => .48f, _ => 5f });
            planted = false;
            rockHits = !rock ? 0 : mode is RockFlight.Falling or RockFlight.Skipping ? 5 : 3;
            hitFlashUntil = 0;
            transform.localScale = sprite && !enemy ? Vector3.one * 1.65f : sprite ? Vector3.one * RockSizeMultiplier * (mode switch
            {
                RockFlight.Falling => 1.55f,
                RockFlight.Skipping => 1.8f,
                RockFlight.Crashing => 2.15f,
                _ => .8f
            }) : enemy ? new Vector3(.35f, .35f, 1) : new Vector3(.5f, .18f, 1);
            transform.rotation = sprite && !enemy
                ? Quaternion.Euler(0, 0, Mathf.Atan2(speed.y, speed.x) * Mathf.Rad2Deg)
                : Quaternion.identity;
            surface.sprite = sprite ? sprite : defaultSprite;
            rockGroundOffset = (mode is RockFlight.Skipping or RockFlight.Crashing) && sprite
                ? -sprite.bounds.min.y * transform.lossyScale.y : .25f * RockSizeMultiplier;
            surface.sortingOrder = defaultSortingOrder;
            surface.color = normalColor = sprite || !enemy ? Color.white : Color.red;
            shellSprite = shell;
            if (rockShell)
            {
                rockShell.color = Color.white;
                rockShell.enabled = false;
            }
        }

        internal void Strike()
        {
            if (!gameObject.activeSelf || rockHits <= 0) return;
            if (--rockHits == 0)
            {
                arena.ShakeImpact(transform.position, Vector2.down * .09f);
                arena.ShowRockBreak(transform.position, surface.bounds.size);
                gameObject.SetActive(false);
            }
            else
            {
                hitFlashUntil = Time.time + HitFlashDuration;
            }
        }

        private void Update()
        {
            if (arena.Finished) { gameObject.SetActive(false); return; }
            if (Planted)
            {
                if (Time.time >= expires)
                {
                    Vector2 center = transform.position;
                    gameObject.SetActive(false);
                    arena.ExplodeRock(center, ExplosionRadius);
                }
                else if (rockShell && rockShell.enabled)
                {
                    float elapsed = fuse - (expires - Time.time);
                    float progress = elapsed / fuse;
                    float phase = Mathf.Repeat(elapsed * (.55f + .55f * progress), 1);
                    float rise = Mathf.SmoothStep(0, 1, phase / .10f);
                    float fade = 1 - Mathf.SmoothStep(0, 1, (phase - .65f) / .12f);
                    normalColor = Color.Lerp(Color.black, Color.red, rise * fade);
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
            float floor = arena.GroundY + rockGroundOffset;
            bool reachedFloor = (flight is RockFlight.Falling or RockFlight.Skipping or RockFlight.Crashing)
                && velocity.y < 0 && to.y <= floor;
            if (reachedFloor)
                to = Vector2.Lerp(from, to, Mathf.InverseLerp(from.y, to.y, floor));

            if (flight != RockFlight.Launch && ResolveHit(from, to)) return;
            if (reachedFloor)
            {
                arena.ShakeImpact(to, Vector2.down * (flight == RockFlight.Skipping ? .055f : .14f));
                if (flight == RockFlight.Skipping)
                {
                    velocity.y = 16f;
                }
                else
                {
                    Vector2 impact = new Vector2(to.x, arena.GroundY + .25f);
                    if (flight == RockFlight.Crashing || Random.value >= arena.RockPlantChance || arena.Player.OverlapsPlantedRock(to.x))
                    {
                        arena.ShowRockBreak(impact, surface.bounds.size);
                        gameObject.SetActive(false);
                        return;
                    }
                    arena.ShowRockLandingDust(impact, surface.bounds.size.x);
                    to.y = floor;
                    planted = true;
                    fuse = arena.RockFuse;
                    expires = Time.time + fuse;
                    transform.localScale = Vector3.one * (1.35f * RockSizeMultiplier);
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
            normalColor = Color.black;
            rockShell.enabled = true;
        }

        private void LateUpdate()
        {
            if (!isRock) return;
            bool flashing = Time.time < hitFlashUntil;
            if (rockShell && rockShell.enabled)
            {
                rockShell.color = flashing ? HitFlashColor : Color.white;
                surface.color = normalColor;
            }
            else
            {
                surface.color = flashing ? HitFlashColor : normalColor;
            }
        }

        private bool ResolveHit(Vector2 from, Vector2 to)
        {
            FighterHealth target = hostile ? arena.Player.Health : arena.Boss.Health;
            Vector2 targetPosition = target.transform.position;
            float targetTime;
            if (hostile && isRock)
            {
                Bounds rockBounds = surface.bounds;
                rockBounds.Expand((Vector3)(arena.Player.HurtboxHalfSize * 2));
                targetTime = BoundsContactTime(rockBounds, targetPosition, targetPosition - (to - from));
            }
            else
            {
                targetTime = hostile || !arena.Boss.Hidden
                    ? ContactTime(from, to, targetPosition, radius) : float.PositiveInfinity;
            }
            ArenaProjectile rock = arena.FirstRockOnPath(from, to, hostile, flight == RockFlight.Skipping, out float rockTime);
            if (rock && rockTime <= targetTime)
            {
                rock.Strike();
                if (flight == RockFlight.Skipping) return false;
                gameObject.SetActive(false);
                return true;
            }
            if (float.IsPositiveInfinity(targetTime)) return false;

            bool hit = target.Hit(damage, hostile ? .65f : 0);
            if (hostile)
            {
                Vector2 impact = Vector2.Lerp(from, to, targetTime);
                if (hit) arena.ShakeImpact(impact, velocity.normalized * .13f);
                if (isRock) arena.ShowRockBreak(impact, surface.bounds.size);
            }
            gameObject.SetActive(false);
            return true;
        }

        internal float BulletContactTime(Vector2 from, Vector2 to)
        {
            if (!gameObject.activeSelf || !isRock) return float.PositiveInfinity;

            Bounds bounds = Planted
                ? new Bounds(new Vector3(transform.position.x, arena.GroundY + PlantedHeight * .5f, 0),
                    new Vector3(PlantedHalfWidth * 2, PlantedHeight, 0))
                : surface.bounds;
            return BoundsContactTime(bounds, from, to);
        }

        private static float BoundsContactTime(Bounds bounds, Vector2 from, Vector2 to)
        {
            bounds.center = (Vector2)bounds.center;
            if (bounds.Contains(from)) return 0;

            Vector2 step = to - from;
            float length = step.magnitude;
            return length > 0 && bounds.IntersectRay(new Ray(from, step / length), out float distance)
                && distance <= length ? distance / length : float.PositiveInfinity;
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
