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
            yield return new WaitForSeconds(.85f);
            while (!arena.Finished)
            {
                yield return FistSlams();
                yield return new WaitForSeconds(.6f);
                if (arena.Finished) yield break;

                yield return StoneAirHockey();
                yield return new WaitForSeconds(.6f);
                if (arena.Finished) yield break;

                yield return FistSweep();
                yield return new WaitForSeconds(.6f);
                if (arena.Finished) yield break;

                yield return ThrowMines();
                if (arena.Finished) yield break;
                StartCoroutine(RollingRocks());
                yield return new WaitForSeconds(.65f);
                if (arena.Finished) yield break;

                yield return FistRockVolleys();
                yield return new WaitForSeconds(1.2f);
            }
        }

        private IEnumerator FistSlams()
        {
            const int slamCount = 3;
            yield return PrepareSlam(0);
            for (int slam = 0; slam < slamCount && !arena.Finished; slam++)
            {
                int index = slam % fists.Length;
                SpriteRenderer fist = fists[index];
                Coroutine nextWindup = slam + 1 < slamCount ? StartCoroutine(PrepareSlam((index + 1) % fists.Length)) : null;
                float x = fist.transform.position.x;
                float bottom = arena.GroundY + fist.bounds.extents.y;
                Intent = "연속 내려찍기";
                yield return MoveFist(fist, new Vector2(x, bottom), 26, 24);
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

        private IEnumerator PrepareSlam(int index)
        {
            while (fistBusy[index] && !arena.Finished) yield return null;
            if (arena.Finished) yield break;

            SpriteRenderer fist = fists[index];
            fistBusy[index] = true;
            float x = ClampFistX(PlayerPosition.x, fist);
            float bottom = arena.GroundY + fist.bounds.extents.y;
            float top = Mathf.Max(bottom + 4.5f, PlayerPosition.y + 2.8f);
            var raised = new Vector2(x, top);
            const float raiseSpeed = 28;
            const float windup = .55f;
            float lead = Vector2.Distance(fist.transform.position, raised) / raiseSpeed + windup;
            Intent = "연속 내려찍기 예고";
            arena.Flash(new Vector2(x, (top + arena.GroundY) * .5f),
                new Vector2(fist.bounds.size.x, top - arena.GroundY), FistLaneColor, lead);
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
            ArenaProjectile puck = arena.Finished ? null : arena.SpawnRock(
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
            fistWindupUntil[index] = Time.time + tracking;
            for (float elapsed = 0; elapsed < tracking && !arena.Finished; elapsed += Time.deltaTime)
            {
                Vector2 follow = new Vector2(startX, Mathf.Max(PlayerPosition.y, bottom));
                fist.transform.position = Vector2.MoveTowards(fist.transform.position, follow, Time.deltaTime * 27);
                yield return null;
            }
            if (arena.Finished) yield break;

            float lockedY = fist.transform.position.y;
            const float warning = .55f;
            fistWindupUntil[index] = Time.time + warning;
            arena.Flash(new Vector2(arena.GroundBounds.center.x, lockedY),
                new Vector2(arena.GroundBounds.size.x, fist.bounds.size.y), FistLaneColor, warning);
            yield return new WaitForSeconds(warning);
            if (arena.Finished) yield break;

            Intent = "수평 쓸기";
            yield return MoveFist(fist, new Vector2(endX, lockedY), 27, 26);
            yield return ReturnFist(index);
        }

        private IEnumerator ThrowMines()
        {
            const int mineCount = 5;
            const float spacing = 2.35f;
            const float halfWidth = (mineCount - 1) * spacing * .5f;
            Bounds bounds = arena.GroundBounds;
            float center = Mathf.Clamp(PlayerPosition.x, bounds.min.x + halfWidth + 1, bounds.max.x - halfWidth - 1);
            int healingMine = Random.value < HealingMineChance ? Random.Range(0, mineCount) : -1;
            Intent = "돌 지뢰밭 예고";
            for (int i = 0; i < mineCount; i++)
            {
                float x = center - halfWidth + i * spacing;
                arena.Flash(new Vector2(x, arena.GroundY + .12f), new Vector2(1.5f, .24f), FistLaneColor, 1.5f);
            }
            yield return new WaitForSeconds(.65f);
            Intent = "돌 지뢰 투척";
            for (int i = 0; i < mineCount && !arena.Finished; i++)
            {
                Vector2 origin = fists[i % fists.Length].transform.position;
                Vector2 destination = new Vector2(center - halfWidth + i * spacing, arena.GroundY + ArenaProjectile.MineLandingOffset);
                const float flightTime = 1f;
                Vector2 velocity = (destination - origin) / flightTime;
                velocity.y += ArenaProjectile.RockGravity * flightTime * .5f;
                arena.SpawnRock(origin, velocity, 15, RockFlight.Mine, healingMine: i == healingMine);
                yield return new WaitForSeconds(.14f);
            }
        }

        private IEnumerator RollingRocks()
        {
            for (int i = 0; i < 6 && !arena.Finished; i++)
            {
                Bounds bounds = arena.GroundBounds;
                float direction = i % 2 == 0 ? 1 : -1;
                float x = direction > 0 ? bounds.min.x + .6f : bounds.max.x - .6f;
                arena.SpawnRock(new Vector2(x, arena.GroundY + .6f), Vector2.right * direction * 12,
                    16, RockFlight.Rolling, holdTime: .28f);
                yield return new WaitForSeconds(.72f);
            }
        }

        private IEnumerator FistRockVolleys()
        {
            Intent = "주먹 돌무더기 예고";
            yield return new WaitForSeconds(.5f);
            Intent = "주먹 돌무더기";
            for (int volley = 0; volley < 4 && !arena.Finished; volley++)
            {
                Vector2 origin = fists[volley % fists.Length].transform.position;
                Vector2 aim = (PlayerPosition - origin).normalized;
                if (aim == Vector2.zero) aim = Vector2.down;
                for (int i = -2; i <= 2; i++)
                {
                    Vector2 direction = Quaternion.Euler(0, 0, i * 13) * aim;
                    arena.SpawnRock(origin + direction * .5f, direction * 12, 12);
                }
                yield return new WaitForSeconds(.52f);
            }
        }

        private float ClampFistX(float x, SpriteRenderer fist)
        {
            Bounds bounds = arena.GroundBounds;
            float halfWidth = fist.bounds.extents.x;
            return Mathf.Clamp(x, bounds.min.x + halfWidth, bounds.max.x - halfWidth);
        }

        private IEnumerator MoveFist(SpriteRenderer fist, Vector2 destination, float speed, float damage = 0)
        {
            bool madeContact = false;
            while (!arena.Finished)
            {
                Vector2 previous = fist.transform.position;
                Vector2 next = Vector2.MoveTowards(previous, destination, speed * Time.deltaTime);
                if (damage > 0 && !madeContact)
                {
                    Bounds contact = fist.bounds;
                    contact.Expand((Vector3)(arena.Player.HurtboxHalfSize * 2));
                    Vector2 player = PlayerPosition;
                    if (ArenaProjectile.BoundsContactTime(contact, player, player - (next - previous)) < float.PositiveInfinity)
                    {
                        madeContact = true;
                        if (arena.Player.Health.Hit(damage, .65f))
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
