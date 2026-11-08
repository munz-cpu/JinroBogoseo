using UnityEngine;

namespace BossTemplate
{
    internal enum RockFlight { Straight, Falling, Skipping, Launch, Crashing, Burst, Mine, Rolling, AirHockey }

    public sealed class ArenaProjectile : MonoBehaviour
    {
        internal const float RockGravity = 25;
        private const float ExplosionRadius = 2.2f;
        // Behind the ground tiles (0), in front of the scenery.
        private const int PlantedSortingOrder = -1;
        private const float RockSizeMultiplier = 1.55f;
        private const float AirHockeyMaxSpeed = 31f;
        internal const float MineLandingOffset = .25f * RockSizeMultiplier;
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
        private float damage, radius, launchAt, expires, fuse;
        private float rockGroundOffset;
        private bool hostile;
        private bool isRock;
        private bool planted;
        private bool healingMine;
        private Color normalColor;
        private float hitFlashUntil;
        private RockFlight flight;
        private int rockHits;
        private AudioClip hockeyBounceSound;
        internal bool Planted => gameObject.activeSelf && planted;
        internal bool IsAirHockeyPuck => gameObject.activeSelf && flight == RockFlight.AirHockey;

        internal void Initialize(BossArena owner, Vector2 speed, float amount, bool enemy,
            Sprite sprite, Sprite shell, RockFlight mode, bool rock, float holdTime)
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
            launchAt = Time.time + holdTime;
            expires = launchAt + (mode switch { RockFlight.Launch => .65f, RockFlight.Burst => .48f, _ => 5f });
            planted = false;
            healingMine = false;
            rockHits = !rock ? 0 : mode is RockFlight.Falling or RockFlight.Skipping or RockFlight.Mine ? 5 : 3;
            hitFlashUntil = 0;
            transform.localScale = sprite && !enemy ? Vector3.one * 1.65f : sprite ? Vector3.one * RockSizeMultiplier * (mode switch
            {
                RockFlight.Falling or RockFlight.Mine => 1.55f,
                RockFlight.Rolling or RockFlight.AirHockey => 1.2f,
                RockFlight.Skipping => 1.8f,
                RockFlight.Crashing => 2.15f,
                _ => .8f
            }) : enemy ? new Vector3(.35f, .35f, 1) : new Vector3(.5f, .18f, 1);
            transform.rotation = sprite && !enemy
                ? Quaternion.Euler(0, 0, Mathf.Atan2(speed.y, speed.x) * Mathf.Rad2Deg)
                : Quaternion.identity;
            surface.sprite = sprite ? sprite : defaultSprite;
            rockGroundOffset = (mode is RockFlight.Skipping or RockFlight.Crashing) && sprite
                ? -sprite.bounds.min.y * transform.lossyScale.y : MineLandingOffset;
            surface.sortingOrder = defaultSortingOrder;
            surface.color = normalColor = sprite || !enemy ? Color.white : Color.red;
            shellSprite = shell;
            if (rockShell)
            {
                rockShell.color = Color.white;
                rockShell.enabled = false;
            }
        }

        internal void LaunchAirHockey(float duration, AudioClip bounceSound, int paddle)
        {
            hockeyBounceSound = bounceSound;
            launchAt = Time.time;
            expires = launchAt + duration;
            Vector2 position = transform.position;
            AimAirHockeyAtPlayer(position, paddle == 0 ? 1 : -1, velocity.magnitude, arena.Boss.AirHockeyCourt);
            arena.Boss.RecordHockeyStrike(paddle, position.y);
            arena.ShowHockeyImpact(position, velocity.normalized, hockeyBounceSound, paddle: true);
        }

        internal float PredictAirHockeyReturn(Rect court, out int receiver, out float seconds)
        {
            receiver = velocity.x > 0 ? 1 : 0;
            float edge = receiver == 1 ? court.xMax : court.xMin;
            seconds = Mathf.Max(0, (edge - transform.position.x) / velocity.x);
            float projectedY = transform.position.y - court.yMin + velocity.y * seconds;
            return court.yMin + Mathf.PingPong(projectedY, court.height);
        }

        internal void SeparateAirHockeyPuck(ArenaProjectile other, Rect court)
        {
            Vector2 first = transform.position;
            Vector2 second = other.transform.position;
            Vector2 offset = second - first;
            Vector2 firstExtent = surface.bounds.extents;
            Vector2 secondExtent = other.surface.bounds.extents;
            Vector2 clearance = firstExtent + secondExtent + Vector2.one * .05f;
            if (Mathf.Abs(offset.x) >= clearance.x || Mathf.Abs(offset.y) >= clearance.y) return;

            Vector2 relativeVelocity = velocity - other.velocity;
            Vector2 normal = offset.sqrMagnitude > .0001f ? offset.normalized : relativeVelocity.normalized;
            if (normal == Vector2.zero) normal = Vector2.up;
            float gap = Mathf.Min(clearance.x / Mathf.Max(.001f, Mathf.Abs(normal.x)),
                clearance.y / Mathf.Max(.001f, Mathf.Abs(normal.y)));
            if (Mathf.Abs(normal.x) * gap > court.width || Mathf.Abs(normal.y) * gap > court.height)
                normal = court.width >= court.height ? Vector2.right : Vector2.up;
            gap = Mathf.Min(gap, Mathf.Max(court.width, court.height));

            Vector2 halfGap = normal * (gap * .5f);
            Vector2 center = (first + second) * .5f;
            float horizontal = Mathf.Abs(halfGap.x);
            float vertical = Mathf.Abs(halfGap.y);
            center.x = Mathf.Clamp(center.x, court.xMin + horizontal, court.xMax - horizontal);
            center.y = Mathf.Clamp(center.y, court.yMin + vertical, court.yMax - vertical);
            transform.position = center - halfGap;
            other.transform.position = center + halfGap;

            float approach = Vector2.Dot(relativeVelocity, normal);
            if (approach <= 0) return;
            ApplyHockeyDeflection(velocity - normal * approach);
            other.ApplyHockeyDeflection(other.velocity + normal * approach);
        }

        private void ApplyHockeyDeflection(Vector2 direction)
        {
            // Keep a horizontal component so the puck can still reach a paddle.
            if (Mathf.Abs(direction.x) < 3f)
                direction.x = 3f * Mathf.Sign(direction.x == 0 ? velocity.x : direction.x);
            velocity = direction.normalized * velocity.magnitude;
        }

        internal void MarkHealingMine() => healingMine = true;

        internal void Strike(bool destroyedByPlayer)
        {
            if (!gameObject.activeSelf || rockHits <= 0) return;
            if (--rockHits == 0)
            {
                if (healingMine && destroyedByPlayer) arena.DropHealthOrb(transform.position);
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
            if (Time.time < launchAt) return;
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
            if (flight == RockFlight.AirHockey)
            {
                if (!arena.Boss.HockeyHitstopActive) UpdateAirHockey(Time.deltaTime);
                return;
            }

            Vector2 from = transform.position;
            float step = Time.deltaTime;
            if (flight is RockFlight.Skipping or RockFlight.Mine) velocity.y -= RockGravity * step;
            Vector2 to = from + velocity * step;
            float floor = arena.GroundY + rockGroundOffset;
            bool reachedFloor = (flight is RockFlight.Falling or RockFlight.Skipping or RockFlight.Crashing or RockFlight.Mine)
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
                    if (flight == RockFlight.Crashing || (flight != RockFlight.Mine && Random.value >= arena.RockPlantChance)
                        || arena.Player.OverlapsPlantedRock(to.x))
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
            if (flight == RockFlight.Rolling)
                transform.Rotate(0, 0, -velocity.x * step * 105);
            if ((flight is RockFlight.Skipping or RockFlight.Rolling) &&
                (to.x < arena.GroundBounds.min.x || to.x > arena.GroundBounds.max.x))
                gameObject.SetActive(false);
        }

        private void UpdateAirHockey(float deltaTime)
        {
            float speed = velocity.magnitude;
            velocity *= Mathf.Min(AirHockeyMaxSpeed, speed + deltaTime * 1.25f) / speed;
            Rect court = arena.Boss.AirHockeyCourt;
            Vector2 position = transform.position;
            position.x = Mathf.Clamp(position.x, court.xMin, court.xMax);
            position.y = Mathf.Clamp(position.y, court.yMin, court.yMax);
            float remaining = deltaTime;
            for (int collision = 0; collision < 3 && remaining > 0; collision++)
            {
                float side = velocity.x > 0 ? court.xMax : court.xMin;
                float wall = velocity.y > 0 ? court.yMax : court.yMin;
                float timeToSide = (side - position.x) / velocity.x;
                float timeToWall = velocity.y == 0 ? float.PositiveInfinity : (wall - position.y) / velocity.y;
                float travel = Mathf.Clamp(Mathf.Min(timeToSide, timeToWall), 0, remaining);
                Vector2 next = position + velocity * travel;
                ResolveHit(position, next);
                position = next;
                remaining -= travel;
                if (remaining <= 0) break;

                bool paddle = timeToSide <= timeToWall;
                if (paddle)
                {
                    position.x = side;
                    float direction = -Mathf.Sign(velocity.x);
                    AimAirHockeyAtPlayer(position, direction, Mathf.Min(AirHockeyMaxSpeed, velocity.magnitude + 1.4f), court);
                    arena.Boss.RecordHockeyStrike(direction > 0 ? 0 : 1, position.y);
                }
                else
                {
                    position.y = wall;
                    velocity.y = -velocity.y;
                }
                arena.ShowHockeyImpact(position, velocity.normalized, hockeyBounceSound, paddle);
                if (paddle) break;
            }
            transform.position = position;
            transform.Rotate(0, 0, -deltaTime * velocity.x * 55);
        }

        private void AimAirHockeyAtPlayer(Vector2 position, float direction, float speed, Rect court)
        {
            Vector2 player = arena.Player.transform.position;
            float inward = Mathf.Max(.75f, (player.x - position.x) * direction);
            float targetY = Mathf.Clamp(player.y, court.yMin, court.yMax);
            velocity = new Vector2(direction * inward, targetY - position.y).normalized * speed;
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
            Color rockColor = normalColor;
            if (healingMine)
            {
                float pulse = .5f + .5f * Mathf.Sin(Time.time * 9);
                rockColor = Color.Lerp(new Color(.12f, .78f, .2f), new Color(.57f, 1, .48f), pulse);
            }
            if (rockShell && rockShell.enabled)
            {
                rockShell.color = flashing ? HitFlashColor : healingMine ? rockColor : Color.white;
                surface.color = rockColor;
            }
            else
            {
                Color color = rockColor;
                if (Time.time < launchAt)
                {
                    float pulse = Mathf.PingPong((launchAt - Time.time) * 5f, 1f);
                    color = Color.Lerp(rockColor, new Color(1, .58f, .25f), pulse * .7f);
                }
                surface.color = flashing ? HitFlashColor : color;
            }
        }

        private bool ResolveHit(Vector2 from, Vector2 to)
        {
            FighterHealth target = hostile ? arena.Player.Health : arena.Boss.Health;
            Vector2 targetPosition = target.transform.position;
            float targetTime;
            int fist = -1;
            if (hostile && isRock)
            {
                Bounds rockBounds = surface.bounds;
                rockBounds.Expand((Vector3)(arena.Player.HurtboxHalfSize * 2));
                targetTime = BoundsContactTime(rockBounds, targetPosition, targetPosition - (to - from));
            }
            else
            {
                if (!hostile && arena.Boss.Phase == 2) targetTime = arena.Boss.FistContactTime(from, to, out fist);
                else if (!hostile && !arena.Boss.CanBeHit) targetTime = float.PositiveInfinity;
                else targetTime = ContactTime(from, to, targetPosition, radius);
            }
            if (!(flight is RockFlight.AirHockey or RockFlight.Rolling or RockFlight.Mine))
            {
                ArenaProjectile rock = arena.FirstRockOnPath(from, to, hostile, flight == RockFlight.Skipping, out float rockTime);
                if (rock && rockTime <= targetTime)
                {
                    rock.Strike(destroyedByPlayer: !hostile);
                    if (flight == RockFlight.Skipping) return false;
                    gameObject.SetActive(false);
                    return true;
                }
            }
            if (float.IsPositiveInfinity(targetTime)) return false;

            if (fist >= 0)
            {
                arena.Boss.HitFist(fist, damage);
                gameObject.SetActive(false);
                return true;
            }
            bool hit = target.Hit(damage, hostile ? .65f : 0);
            if (hostile)
            {
                Vector2 impact = Vector2.Lerp(from, to, targetTime);
                if (hit) arena.ShakeImpact(impact, velocity.normalized * .13f);
                if (flight == RockFlight.AirHockey) return false;
                if (isRock) arena.ShowRockBreak(impact, surface.bounds.size);
            }
            gameObject.SetActive(false);
            return true;
        }

        internal float BulletContactTime(Vector2 from, Vector2 to)
        {
            if (!gameObject.activeSelf || !isRock || flight == RockFlight.AirHockey)
                return float.PositiveInfinity;

            Bounds bounds = Planted
                ? new Bounds(new Vector3(transform.position.x, arena.GroundY + PlantedHeight * .5f, 0),
                    new Vector3(PlantedHalfWidth * 2, PlantedHeight, 0))
                : surface.bounds;
            return BoundsContactTime(bounds, from, to);
        }

        internal static float BoundsContactTime(Bounds bounds, Vector2 from, Vector2 to)
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
