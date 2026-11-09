using System.Collections;
using UnityEngine;

namespace BossTemplate
{
    public sealed partial class ArenaBoss
    {
        private static readonly Color FistLaneColor = new Color(1, .55f, .2f, .38f);
        private const float HockeyStrikeReach = .5f;
        private const float HockeyHitstopDuration = .065f;
        private readonly float[] hockeyStrikeTimes = new float[2];
        private float hockeyHitstopUntil;
        internal bool HockeyHitstopActive => Time.unscaledTime < hockeyHitstopUntil;

        private IEnumerator FightWithFists()
        {
            if (arena.Study)
            {
                yield return new WaitForSeconds(.85f);
                BossPattern? previousPattern = null;
                while (!arena.Finished)
                {
                    BossPattern pattern = arena.Study.Pick(StudySession.PhaseTwo, previousPattern);
                    previousPattern = pattern;
                    arena.Study.Executed(pattern);
                    yield return pattern switch
                    {
                        BossPattern.fist_slams => FistSlams(),
                        BossPattern.air_hockey => StoneAirHockey(),
                        BossPattern.fist_sweep => FistSweep(),
                        BossPattern.throw_mines => ThrowMines(),
                        _ => BeginRollingPattern()
                    };
                    yield return new WaitForSeconds(.45f * arena.Parameters(pattern).interval);
                }
                yield break;
            }

            var patterns = new System.Func<IEnumerator>[]
            {
                FistSlams, StoneAirHockey, FistSweep, ThrowMines, BeginRollingPattern
            };
            System.Func<IEnumerator> previous = null;
            yield return new WaitForSeconds(.85f);
            while (!arena.Finished)
            {
                for (int i = patterns.Length - 1; i > 0; i--)
                {
                    int pick = Random.Range(0, i + 1);
                    (patterns[i], patterns[pick]) = (patterns[pick], patterns[i]);
                }
                if (patterns[0] == previous)
                {
                    int pick = Random.Range(1, patterns.Length);
                    (patterns[0], patterns[pick]) = (patterns[pick], patterns[0]);
                }

                foreach (var pattern in patterns)
                {
                    if (arena.Finished) yield break;
                    previous = pattern;
                    yield return pattern();
                    yield return new WaitForSeconds(.45f);
                }
            }
        }

        private IEnumerator FistSlams()
        {
            const int slamCount = 8;
            const float speedIncreasePerSlam = .08f;
            yield return PrepareSlam(0, 1);
            for (int slam = 0; slam < slamCount && !arena.Finished; slam++)
            {
                int index = slam % fists.Length;
                SpriteRenderer fist = fists[index];
                float speedMultiplier = 1 + slam * speedIncreasePerSlam;
                Coroutine nextWindup = slam + 1 < slamCount
                    ? StartCoroutine(PrepareSlam((index + 1) % fists.Length, speedMultiplier + speedIncreasePerSlam))
                    : null;
                float x = fist.transform.position.x;
                float bottom = arena.GroundY + fist.bounds.extents.y;
                Intent = "연속 내려찍기";
                yield return MoveFist(fist, new Vector2(x, bottom), 26 * speedMultiplier, 24, BossPattern.fist_slams);
                if (arena.Finished) yield break;
                var impact = new Vector2(x, arena.GroundY);
                arena.ShowRockLandingDust(impact, fist.bounds.size.x, playSound: false);
                arena.ShakeImpact(impact, Vector2.down * .34f);
                arena.PlayEffect(fistSlamSound);
                Coroutine recovery = StartCoroutine(ReturnFist(index));
                if (nextWindup != null) yield return nextWindup;
                else yield return recovery;
            }
        }

        private IEnumerator PrepareSlam(int index, float speedMultiplier)
        {
            while (fistBusy[index] && !arena.Finished) yield return null;
            if (arena.Finished) yield break;

            SpriteRenderer fist = fists[index];
            fistBusy[index] = true;
            float x = ClampFistX(PlayerPosition.x, fist);
            float bottom = arena.GroundY + fist.bounds.extents.y;
            float top = Mathf.Max(bottom + 9f, PlayerPosition.y + 5.5f);
            var raised = new Vector2(x, top);
            float raiseSpeed = 28 * speedMultiplier;
            float windup = .55f / speedMultiplier;
            float lead = Vector2.Distance(fist.transform.position, raised) / raiseSpeed + windup;
            Intent = "연속 내려찍기 예고";
            arena.Flash(new Vector2(x, (top + arena.GroundY) * .5f),
                new Vector2(fist.bounds.size.x * arena.Parameters(BossPattern.fist_slams).hitbox, top - arena.GroundY), FistLaneColor, lead);
            fistWindupUntil[index] = Time.time + lead;
            yield return MoveFist(fist, raised, raiseSpeed);
            yield return new WaitForSeconds(windup);
        }

        internal Rect AirHockeyCourt => arena.GetAirHockeyCourt(fists[0].bounds.extents.x);

        private IEnumerator StoneAirHockey()
        {
            const float setup = .55f;
            const float serveWarning = .5f;
            const float duration = 10f;
            Vector2 leftStart = fists[0].transform.position;
            Vector2 rightStart = fists[1].transform.position;
            holdBothFists = true;
            hockeyHitstopUntil = 0;
            Intent = "돌 에어하키 예고";
            fists[0].flipX = false;
            fists[1].flipX = true;

            Rect court = AirHockeyCourt;
            for (float elapsed = 0; elapsed < setup && !arena.Finished; elapsed += Time.deltaTime)
            {
                court = AirHockeyCourt;
                float t = Mathf.SmoothStep(0, 1, elapsed / setup);
                fists[0].transform.position = Vector2.Lerp(leftStart, HockeyPaddlePosition(0, court, HockeyServeHeight(court)), t);
                fists[1].transform.position = Vector2.Lerp(rightStart, HockeyPaddlePosition(1, court, court.center.y), t);
                yield return null;
            }

            court = AirHockeyCourt;
            ArenaProjectile puck = arena.Finished ? null : arena.SpawnRock(BossPattern.air_hockey,
                new Vector2(court.xMin, HockeyServeHeight(court)), Vector2.right * 17.5f, 20,
                RockFlight.AirHockey, holdTime: 2f);
            for (float elapsed = 0; elapsed < serveWarning && !arena.Finished; elapsed += Time.deltaTime)
            {
                court = AirHockeyCourt;
                float serveHeight = HockeyServeHeight(court);
                fists[0].transform.position = HockeyPaddlePosition(0, court, serveHeight, HockeyStrikeOffset(serveWarning - elapsed));
                fists[1].transform.position = HockeyPaddlePosition(1, court, court.center.y);
                if (puck && puck.IsAirHockeyPuck) puck.transform.position = new Vector2(court.xMin, serveHeight);
                yield return null;
            }

            if (!arena.Finished && puck && puck.IsAirHockeyPuck)
            {
                Intent = "돌 에어하키";
                hockeyStrikeTimes[0] = hockeyStrikeTimes[1] = float.NegativeInfinity;
                puck.LaunchAirHockey(duration, fistImpactSound);
                while (!arena.Finished && puck.IsAirHockeyPuck)
                {
                    if (!HockeyHitstopActive)
                    {
                        court = AirHockeyCourt;
                        float receiveY = puck.PredictAirHockeyReturn(court, out int receiver, out float secondsToHit);
                        for (int i = 0; i < fists.Length; i++)
                        {
                            float y = fists[i].transform.position.y;
                            if (i == receiver) y = Mathf.MoveTowards(y, receiveY, Time.deltaTime * 24);
                            float timeSinceStrike = Time.unscaledTime - hockeyStrikeTimes[i];
                            float stroke = timeSinceStrike < .15f
                                ? Mathf.SmoothStep(HockeyStrikeReach, 0, timeSinceStrike / .15f)
                                : i == receiver ? HockeyStrikeOffset(secondsToHit) : 0;
                            fists[i].transform.position = HockeyPaddlePosition(i, court, y, stroke);
                        }
                    }
                    yield return null;
                }
            }

            StartCoroutine(ReturnFist(1));
            yield return ReturnFist(0);
            holdBothFists = false;
        }

        internal void RecordHockeyStrike(int paddle, float impactY)
        {
            hockeyHitstopUntil = Time.unscaledTime + HockeyHitstopDuration;
            hockeyStrikeTimes[paddle] = hockeyHitstopUntil;
            fists[paddle].transform.position = HockeyPaddlePosition(paddle, AirHockeyCourt, impactY, HockeyStrikeReach);
        }

        private static float HockeyServeHeight(Rect court) => Mathf.Lerp(court.yMin, court.yMax, .24f);

        private Vector2 HockeyPaddlePosition(int index, Rect court, float y, float stroke = 0)
        {
            Vector2 halfSize = fists[index].bounds.extents;
            float offset = halfSize.x - stroke;
            float x = index == 0 ? court.xMin - offset : court.xMax + offset;
            float bottom = Mathf.Max(arena.GroundY + halfSize.y, court.yMin + halfSize.y - .25f);
            float top = Mathf.Max(bottom, court.yMax - Mathf.Max(0, halfSize.y - .25f));
            return new Vector2(x, Mathf.Clamp(y, bottom, top));
        }

        private static float HockeyStrikeOffset(float secondsToHit)
        {
            const float windup = .26f;
            const float strike = .09f;
            const float pullback = -.65f;
            if (secondsToHit >= strike)
                return Mathf.SmoothStep(0, pullback, (windup - secondsToHit) / (windup - strike));
            float t = Mathf.Clamp01(1 - secondsToHit / strike);
            return Mathf.Lerp(pullback, HockeyStrikeReach, t * t);
        }

        private IEnumerator FistSweep()
        {
            int index = PlayerPosition.x < arena.GroundBounds.center.x ? 0 : 1;
            SpriteRenderer fist = fists[index];
            fistBusy[index] = true;
            float startX = ClampFistX(index == 0 ? arena.GroundBounds.min.x : arena.GroundBounds.max.x, fist);
            float endX = ClampFistX(index == 0 ? arena.GroundBounds.max.x : arena.GroundBounds.min.x, fist);
            float bottom = arena.GroundY + fist.bounds.extents.y;
            fist.flipX = endX < startX;
            Intent = "주먹이 플레이어 높이를 추적한다";
            const float tracking = 2f;
            const float warning = .55f;
            Bounds ground = arena.GroundBounds;
            var laneCenter = new Vector2(ground.center.x, fist.transform.position.y);
            var laneSize = new Vector2(ground.size.x, fist.bounds.size.y * arena.Parameters(BossPattern.fist_sweep).hitbox);
            GameObject lane = arena.Flash(laneCenter, laneSize, FistLaneColor, tracking + warning);
            fistWindupUntil[index] = Time.time + tracking;
            for (float elapsed = 0; elapsed < tracking && !arena.Finished; elapsed += Time.deltaTime)
            {
                Vector2 follow = new Vector2(startX, Mathf.Max(PlayerPosition.y, bottom));
                fist.transform.position = Vector2.MoveTowards(fist.transform.position, follow, Time.deltaTime * 27);
                laneCenter.y = fist.transform.position.y;
                if (lane) lane.transform.position = laneCenter;
                yield return null;
            }
            if (arena.Finished)
            {
                if (lane) lane.SetActive(false);
                yield break;
            }

            float lockedY = fist.transform.position.y;
            fistWindupUntil[index] = Time.time + warning;
            Intent = "수평 쓸기 예고";
            var lockedColor = new Color(1, .35f, .12f, .65f);
            if (lane) lane.GetComponent<ArenaEffect>().Show(laneCenter, laneSize, lockedColor, warning);
            else arena.Flash(laneCenter, laneSize, lockedColor, warning);
            yield return new WaitForSeconds(warning);
            if (arena.Finished) yield break;

            Intent = "수평 쓸기";
            yield return MoveFist(fist, new Vector2(endX, lockedY), 30, 26, BossPattern.fist_sweep);
            yield return ReturnFist(index);
        }

        private IEnumerator ThrowMines()
        {
            const int mineCount = 9;
            const float liftTime = .55f;
            const float windup = .25f;
            const float throwInterval = .14f;
            const float flightTime = 2.6f;
            Bounds bounds = arena.GroundBounds;
            float spacing = Mathf.Min(2.35f, Mathf.Max(0, bounds.size.x - 2) / (mineCount - 1));
            float halfWidth = (mineCount - 1) * spacing * .5f;
            float center = Mathf.Clamp(PlayerPosition.x, bounds.min.x + halfWidth + 1, bounds.max.x - halfWidth - 1);
            int healingMine = Random.value < HealingMineChance ? Random.Range(0, mineCount) : -1;
            holdBothFists = true;
            Intent = "돌 지뢰밭 예고";
            for (int i = 0; i < mineCount; i++)
            {
                float x = center - halfWidth + i * spacing;
                arena.Flash(new Vector2(x, arena.GroundY + .12f), new Vector2(1.5f, .24f), FistLaneColor,
                    liftTime + windup + i * throwInterval + flightTime);
            }

            Vector2 leftStart = fists[0].transform.position;
            Vector2 rightStart = fists[1].transform.position;
            float throwHeight = Mathf.Max(arena.GroundY + 8.5f, PlayerPosition.y + 2.5f);
            var leftRaised = new Vector2(leftStart.x, throwHeight);
            var rightRaised = new Vector2(rightStart.x, throwHeight);
            for (float elapsed = 0; elapsed < liftTime && !arena.Finished; elapsed += Time.deltaTime)
            {
                float t = Mathf.SmoothStep(0, 1, elapsed / liftTime);
                fists[0].transform.position = Vector2.Lerp(leftStart, leftRaised, t);
                fists[1].transform.position = Vector2.Lerp(rightStart, rightRaised, t);
                yield return null;
            }
            if (arena.Finished) yield break;
            fists[0].transform.position = leftRaised;
            fists[1].transform.position = rightRaised;
            yield return new WaitForSeconds(windup);

            Intent = "돌 지뢰 투척";
            for (int i = 0; i < mineCount && !arena.Finished; i++)
            {
                Vector2 origin = fists[i % fists.Length].transform.position;
                Vector2 destination = new Vector2(center - halfWidth + i * spacing, arena.GroundY + ArenaProjectile.MineLandingOffset);
                Vector2 velocity = (destination - origin) / flightTime;
                velocity.y += ArenaProjectile.MineGravity * flightTime * .5f;
                arena.SpawnRock(BossPattern.throw_mines, origin, velocity, 15, RockFlight.Mine, healingMine: i == healingMine);
                yield return new WaitForSeconds(throwInterval);
            }
            StartCoroutine(ReturnFist(1));
            yield return ReturnFist(0);
            holdBothFists = false;
        }

        private IEnumerator BeginRollingPattern()
        {
            Intent = "바닥 구르는 돌";
            StartCoroutine(RollingRocks());
            yield return null;
        }

        private IEnumerator RollingRocks()
        {
            for (int i = 0; i < 8 && !arena.Finished; i++)
            {
                Bounds bounds = arena.GroundBounds;
                float direction = i % 2 == 0 ? 1 : -1;
                float x = direction > 0 ? bounds.min.x + .6f : bounds.max.x - .6f;
                arena.SpawnRock(BossPattern.rolling_rocks, new Vector2(x, arena.GroundY + .6f), Vector2.right * direction * 5,
                    16, RockFlight.Rolling, holdTime: .28f);
                yield return new WaitForSeconds(.45f);
            }
        }

        private float ClampFistX(float x, SpriteRenderer fist)
        {
            Bounds bounds = arena.GroundBounds;
            float halfWidth = fist.bounds.extents.x;
            return Mathf.Clamp(x, bounds.min.x + halfWidth, bounds.max.x - halfWidth);
        }

        private IEnumerator MoveFist(SpriteRenderer fist, Vector2 destination, float speed, float damage = 0, BossPattern? pattern = null)
        {
            PatternParameters parameters = pattern.HasValue ? arena.Parameters(pattern.Value) : AdaptiveDifficulty.Neutral;
            speed *= parameters.speed;
            damage *= parameters.damage;
            bool madeContact = false;
            while (!arena.Finished)
            {
                Vector2 previous = fist.transform.position;
                Vector2 next = Vector2.MoveTowards(previous, destination, speed * Time.deltaTime);
                if (damage > 0 && !madeContact)
                {
                    Bounds contact = fist.bounds;
                    contact.size *= parameters.hitbox;
                    contact.Expand((Vector3)(arena.Player.HurtboxHalfSize * 2));
                    Vector2 player = PlayerPosition;
                    if (ArenaProjectile.BoundsContactTime(contact, player, player - (next - previous)) < float.PositiveInfinity)
                    {
                        madeContact = true;
                        if (arena.Player.Health.Hit(damage, .65f, true, pattern))
                            arena.ShakeImpact(next, (next - previous).normalized * .23f);
                    }
                }
                fist.transform.position = next;
                if (next == destination) yield break;
                yield return null;
            }
        }

        private IEnumerator ReturnFist(int index)
        {
            SpriteRenderer fist = fists[index];
            Vector2 start = fist.transform.position;
            const float recovery = .55f;
            for (float elapsed = 0; elapsed < recovery && !arena.Finished; elapsed += Time.deltaTime)
            {
                fist.transform.position = Vector2.Lerp(start, FistOrbitPosition(index), Mathf.SmoothStep(0, 1, elapsed / recovery));
                yield return null;
            }
            fistBusy[index] = false;
        }
    }
}
