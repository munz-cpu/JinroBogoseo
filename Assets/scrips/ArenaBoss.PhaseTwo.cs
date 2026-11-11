using System.Collections;
using UnityEngine;

namespace BossTemplate
{
    public sealed partial class ArenaBoss
    {
        private static readonly Color FistLaneColor = new Color(1, .55f, .2f, .38f);
        private const float HockeyStrikeReach = .5f;
        private const float HockeyWindupDuration = .26f;
        private const float HockeyHitstopDuration = .065f;
        private readonly float[] hockeyStrikeTimes = new float[2];
        private float hockeyHitstopUntil;
        internal bool HockeyHitstopActive => Time.unscaledTime < hockeyHitstopUntil;

        private IEnumerator FightWithFists()
        {
            yield return new WaitForSeconds(.85f);
            BossPattern? previous = null;
            if (arena.Study)
            {
                while (!arena.Finished)
                {
                    BossPattern pattern = arena.Study.Pick(StudySession.PhaseTwo, previous);
                    previous = pattern;
                    arena.Study.Executed(pattern);
                    if ((pattern is BossPattern.fist_jab or BossPattern.fist_uppercut or BossPattern.fist_backhand)
                        && Random.value < arena.Study.SupportRainChance)
                        SpawnFistSupportRain();
                    yield return RunFistPattern(pattern);
                    yield return new WaitForSeconds(.45f * arena.Parameters(pattern).interval);
                }
                yield break;
            }

            var patterns = (BossPattern[])StudySession.PhaseTwo.Clone();
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
                    yield return RunFistPattern(pattern);
                    yield return new WaitForSeconds(.45f);
                }
            }
        }

        private void SpawnFistSupportRain()
        {
            const float offset = 4.5f;
            const float warning = .75f;
            const float fallSpeed = 11f;
            float left = arena.GroundBounds.min.x + 2;
            float right = arena.GroundBounds.max.x - 2;
            float playerX = PlayerPosition.x;
            float x = playerX + (Random.value < .5f ? -offset : offset);
            if (x < left || x > right) x = 2 * playerX - x;
            x = Mathf.Clamp(x, left, right);
            var rock = arena.SpawnRock(BossPattern.rock_rain, new Vector2(x, arena.GroundY + FallingRockHoverHeight),
                Vector2.down * fallSpeed, 8, RockFlight.Crashing, holdTime: warning);
            if (!rock) return;

            arena.Study.Executed(BossPattern.rock_rain);
            float width = rock.GetComponent<SpriteRenderer>().bounds.size.x;
            arena.Flash(new Vector2(x, arena.GroundY + .12f), new Vector2(width, .24f), FistLaneColor,
                warning + FallingRockHoverHeight / fallSpeed);
        }

        private IEnumerator RunFistPattern(BossPattern pattern) => pattern switch
        {
            BossPattern.fist_slams => FistSlams(),
            BossPattern.air_hockey => StoneAirHockey(),
            BossPattern.fist_sweep => FistSweep(),
            BossPattern.throw_mines => ThrowMines(),
            BossPattern.rolling_rocks => RollingRocks(),
            BossPattern.fist_jab => FistJab(),
            BossPattern.fist_uppercut => FistUppercut(),
            BossPattern.fist_backhand => FistBackhand(),
            BossPattern.fist_clap => FistClap(),
            BossPattern.fist_double_slam => DoubleFistSlam(),
            _ => throw new System.ArgumentOutOfRangeException(nameof(pattern))
        };

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
            Vector2 target = PlayerPosition;
            float x = ClampFistX(target.x, fist);
            float bottom = arena.GroundY + fist.bounds.extents.y;
            float top = Mathf.Max(bottom + 9f, target.y + 5.5f);
            var raised = new Vector2(x, top);
            float raiseSpeed = 28 * speedMultiplier;
            float windup = .55f / speedMultiplier;
            float lead = Vector2.Distance(fist.transform.position, raised) / raiseSpeed + windup;
            Intent = "연속 내려찍기 예고";
            arena.Flash(new Vector2(x, (top + arena.GroundY) * .5f),
                new Vector2(fist.bounds.size.x * arena.Parameters(BossPattern.fist_slams).hitbox, top - arena.GroundY), FistLaneColor, lead);
            var gather = new Vector2(x, Mathf.Min(top, target.y + 3.4f));
            yield return WindUpFist(index, gather, raised, lead);
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
                float t = EaseInOutQuint(elapsed / setup);
                fists[0].transform.position = Vector2.Lerp(leftStart, HockeyPaddlePosition(0, court, HockeyServeHeight(court)), t);
                fists[1].transform.position = Vector2.Lerp(rightStart, HockeyPaddlePosition(1, court, court.center.y), t);
                yield return null;
            }

            court = AirHockeyCourt;
            ArenaProjectile puck = arena.Finished ? null : arena.SpawnRock(BossPattern.air_hockey,
                new Vector2(court.xMin, HockeyServeHeight(court)), Vector2.right * 17.5f, 20,
                RockFlight.AirHockey, holdTime: 2f);
            fistWindupUntil[0] = Time.time + serveWarning;
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
                            if (i == receiver && secondsToHit <= HockeyWindupDuration)
                                fistWindupUntil[i] = Time.time + secondsToHit;
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
            fistWindupUntil[paddle] = 0;
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
            const float strike = .09f;
            const float pullback = -1.15f;
            if (secondsToHit >= strike)
                return Mathf.SmoothStep(0, pullback, (HockeyWindupDuration - secondsToHit) / (HockeyWindupDuration - strike));
            float t = Mathf.Clamp01(1 - secondsToHit / strike);
            return Mathf.Lerp(pullback, HockeyStrikeReach, t * t * t);
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
            Vector2 followVelocity = Vector2.zero;
            for (float elapsed = 0; elapsed < tracking && !arena.Finished; elapsed += Time.deltaTime)
            {
                Vector2 follow = new Vector2(startX, Mathf.Max(PlayerPosition.y, bottom));
                fist.transform.position = Vector2.SmoothDamp(fist.transform.position, follow, ref followVelocity, .18f, 27);
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
            Intent = "수평 쓸기 예고";
            var lockedColor = new Color(1, .35f, .12f, .65f);
            if (lane) lane.GetComponent<ArenaEffect>().Show(laneCenter, laneSize, lockedColor, warning);
            else arena.Flash(laneCenter, laneSize, lockedColor, warning);
            var sweepStart = new Vector2(startX, lockedY);
            yield return WindUpFist(index, sweepStart + Vector2.up * .9f, sweepStart, warning);
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
            float gatherHeight = Mathf.Min(throwHeight - 1, PlayerPosition.y + 3.4f);
            var leftGather = new Vector2(ClampFistX(leftStart.x - 1.2f, fists[0]), gatherHeight);
            var rightGather = new Vector2(ClampFistX(rightStart.x + 1.2f, fists[1]), gatherHeight);
            var leftWindup = StartCoroutine(WindUpFist(0, leftGather, leftRaised, liftTime + windup));
            yield return WindUpFist(1, rightGather, rightRaised, liftTime + windup);
            yield return leftWindup;
            if (arena.Finished) yield break;

            Intent = "돌 지뢰 투척";
            for (int i = 0; i < mineCount && !arena.Finished; i++)
            {
                SpriteRenderer fist = fists[i % fists.Length];
                Vector2 origin = fist.transform.position;
                Vector2 destination = new Vector2(center - halfWidth + i * spacing, arena.GroundY + ArenaProjectile.MineLandingOffset);
                Vector2 velocity = (destination - origin) / flightTime;
                velocity.y += ArenaProjectile.MineGravity * flightTime * .5f;
                arena.SpawnRock(BossPattern.throw_mines, origin, velocity, 15, RockFlight.Mine, healingMine: i == healingMine);
                Vector2 recoil = -velocity.normalized * .65f;
                for (float elapsed = 0; elapsed < throwInterval && !arena.Finished; elapsed += Time.deltaTime)
                {
                    float t = elapsed / throwInterval;
                    float amount = Mathf.SmoothStep(0, 1, t / .25f) * (1 - Mathf.SmoothStep(0, 1, (t - .25f) / .75f));
                    fist.transform.position = origin + recoil * amount;
                    yield return null;
                }
                fist.transform.position = origin;
            }
            StartCoroutine(ReturnFist(1));
            yield return ReturnFist(0);
            holdBothFists = false;
        }

        private IEnumerator RollingRocks()
        {
            Intent = "구르는 돌 예고";
            fistBusy[0] = fistBusy[1] = true;
            Vector2 left = fists[0].transform.position;
            Vector2 right = fists[1].transform.position;
            Vector2 pullback = new Vector2(1.2f, .65f);
            var leftWindup = StartCoroutine(WindUpFist(0, left + pullback, left + Vector2.left, .55f));
            yield return WindUpFist(1, right + new Vector2(-pullback.x, pullback.y), right + Vector2.right, .55f);
            yield return leftWindup;
            if (arena.Finished) yield break;

            Intent = "바닥 구르는 돌";
            StartCoroutine(ReturnFist(0));
            StartCoroutine(ReturnFist(1));
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

        private IEnumerator FistJab()
        {
            Vector2 target = PlayerPosition;
            int index = target.x < transform.position.x ? 0 : 1;
            float direction = index == 0 ? 1 : -1;
            SpriteRenderer fist = fists[index];
            float y = Mathf.Max(arena.GroundY + fist.bounds.extents.y, target.y);
            var start = new Vector2(ClampFistX(target.x - direction * 6, fist), y);
            var end = new Vector2(ClampFistX(target.x + direction * 1.5f, fist), y);
            return StrikeFist(index, start, end, BossPattern.fist_jab, "잽", 28, 17);
        }

        private IEnumerator FistUppercut()
        {
            Vector2 target = PlayerPosition;
            int index = target.x < transform.position.x ? 0 : 1;
            SpriteRenderer fist = fists[index];
            float x = ClampFistX(target.x, fist);
            float bottom = arena.GroundY + fist.bounds.extents.y;
            return StrikeFist(index, new Vector2(x, bottom), new Vector2(x, Mathf.Max(bottom + 5.5f, target.y + 2)),
                BossPattern.fist_uppercut, "어퍼컷", 24, 19);
        }

        private IEnumerator FistBackhand()
        {
            Vector2 target = PlayerPosition;
            // The far hand reaches across the player instead of attacking from the same side as the jab.
            int index = target.x < transform.position.x ? 1 : 0;
            float direction = index == 0 ? 1 : -1;
            SpriteRenderer fist = fists[index];
            float y = Mathf.Max(arena.GroundY + fist.bounds.extents.y, target.y);
            var start = new Vector2(ClampFistX(target.x - direction * 5.5f, fist), y + 4.5f);
            var end = new Vector2(ClampFistX(target.x + direction * 2.5f, fist), y);
            return StrikeFist(index, start, end, BossPattern.fist_backhand, "후려치기", 23, 20);
        }

        private IEnumerator FistClap()
        {
            Vector2 target = PlayerPosition;
            Vector2 half = fists[0].bounds.extents;
            float outer = half.x + 4f;
            float inner = half.x;
            float x = ClampPairedFistCenter(target.x, outer + half.x);
            float hitbox = arena.Parameters(BossPattern.fist_clap).hitbox;
            // Leave room to crouch under a low clap at every difficulty level.
            float y = Mathf.Max(arena.GroundY + half.y * hitbox + 1.4f, target.y);
            var left = StartCoroutine(StrikeFist(0, new Vector2(x - outer, y), new Vector2(x - inner, y),
                BossPattern.fist_clap, "양손 박수", 19, 21));
            yield return StrikeFist(1, new Vector2(x + outer, y), new Vector2(x + inner, y),
                BossPattern.fist_clap, "양손 박수", 19, 21);
            yield return left;
        }

        private IEnumerator DoubleFistSlam()
        {
            Vector2 target = PlayerPosition;
            Vector2 half = fists[0].bounds.extents;
            float offset = half.x * 1.2f + 1.6f;
            float x = ClampPairedFistCenter(target.x, offset + half.x);
            float bottom = arena.GroundY + half.y;
            float top = Mathf.Max(bottom + 6.5f, target.y + 3.5f);
            var left = StartCoroutine(StrikeFist(0, new Vector2(x - offset, top), new Vector2(x - offset, bottom),
                BossPattern.fist_double_slam, "양손 내려찍기", 24, 22));
            yield return StrikeFist(1, new Vector2(x + offset, top), new Vector2(x + offset, bottom),
                BossPattern.fist_double_slam, "양손 내려찍기", 24, 22);
            yield return left;
        }

        private float ClampPairedFistCenter(float x, float clearance)
        {
            Bounds bounds = arena.GroundBounds;
            return Mathf.Clamp(x, bounds.min.x + clearance, bounds.max.x - clearance);
        }

        private IEnumerator StrikeFist(int index, Vector2 start, Vector2 end, BossPattern pattern,
            string attack, float speed, float damage)
        {
            if (arena.Finished) yield break;
            SpriteRenderer fist = fists[index];
            var parameters = arena.Parameters(pattern);
            float lead = .35f + Mathf.Clamp(.58f * parameters.interval, .5f, .72f);
            Vector2 path = end - start;
            Vector2 direction = path.normalized;
            Vector2 size = fist.bounds.size * parameters.hitbox;
            float along = Mathf.Abs(direction.x) * size.x + Mathf.Abs(direction.y) * size.y;
            float across = Mathf.Abs(direction.y) * size.x + Mathf.Abs(direction.x) * size.y;
            var lane = arena.Flash((start + end) * .5f, new Vector2(path.magnitude + along, across), FistLaneColor, lead);
            if (lane) lane.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(path.y, path.x) * Mathf.Rad2Deg);

            fistBusy[index] = true;
            if (path.x != 0) fist.flipX = path.x < 0;
            Intent = attack + " 예고";
            Vector2 pose = start - direction * 1.35f;
            if (pattern == BossPattern.fist_uppercut)
                pose = start + new Vector2(index == 0 ? -1.25f : 1.25f, .85f);
            pose.x = ClampFistX(pose.x, fist);
            if (direction.y < 0) pose.y = Mathf.Min(pose.y, PlayerPosition.y + 3.4f);
            // A flipped sprite already faces left. Measure the remaining tilt from that direction.
            float tilt = path.x != 0 && path.y != 0
                ? Mathf.Atan2(path.y * Mathf.Sign(path.x), Mathf.Abs(path.x)) * Mathf.Rad2Deg
                : 0;
            yield return WindUpFist(index, pose, start, lead, tilt);
            if (arena.Finished) yield break;

            Intent = attack;
            yield return MoveFist(fist, end, speed, damage, pattern);
            if (arena.Finished) yield break;
            if (pattern == BossPattern.fist_double_slam)
            {
                var impact = new Vector2(end.x, arena.GroundY);
                arena.ShowRockLandingDust(impact, fist.bounds.size.x, playSound: false);
                if (index == 0)
                {
                    arena.ShakeImpact(impact, Vector2.down * .32f);
                    arena.PlayEffect(fistSlamSound);
                }
            }
            yield return ReturnFist(index);
        }

        private IEnumerator WindUpFist(int index, Vector2 pose, Vector2 ready, float duration, float tilt = 0)
        {
            SpriteRenderer fist = fists[index];
            Vector2 origin = fist.transform.position;
            Quaternion initialRotation = fist.transform.rotation;
            Quaternion readyRotation = Quaternion.Euler(0, 0, tilt);
            const float gatherEnd = .58f;
            const float releaseStart = .74f;
            fistWindupUntil[index] = Time.time + duration;
            for (float elapsed = 0; elapsed < duration && !arena.Finished; elapsed += Time.deltaTime)
            {
                float t = elapsed / duration;
                float gather = Mathf.SmoothStep(0, 1, t / gatherEnd);
                float release = Mathf.InverseLerp(releaseStart, 1, t);
                fist.transform.position = t < gatherEnd
                    ? Vector2.Lerp(origin, pose, gather)
                    : Vector2.Lerp(pose, ready, release * release);
                fist.transform.rotation = Quaternion.Slerp(initialRotation, readyRotation, gather);
                yield return null;
            }
            if (!arena.Finished) fist.transform.SetPositionAndRotation(ready, readyRotation);
            fistWindupUntil[index] = 0;
        }

        private static float EaseInOutQuint(float t)
        {
            float edge = t < .5f ? 2 * t : 2 * (1 - t);
            float eased = .5f * edge * edge * edge * edge * edge;
            return t < .5f ? eased : 1 - eased;
        }

        private static float FistStrikeProgress(float t, BossPattern pattern)
        {
            float remaining = 1 - t;
            float eased = pattern switch
            {
                BossPattern.fist_slams or BossPattern.fist_double_slam => t * t * t,
                BossPattern.fist_jab or BossPattern.fist_uppercut => 1 - remaining * remaining * remaining,
                _ => Mathf.SmoothStep(0, 1, t)
            };
            return Mathf.Lerp(t, eased, .75f);
        }

        private float ClampFistX(float x, SpriteRenderer fist)
        {
            Bounds bounds = arena.GroundBounds;
            float halfWidth = fist.bounds.extents.x;
            return Mathf.Clamp(x, bounds.min.x + halfWidth, bounds.max.x - halfWidth);
        }

        private IEnumerator MoveFist(SpriteRenderer fist, Vector2 destination, float speed, float damage, BossPattern pattern)
        {
            PatternParameters parameters = arena.Parameters(pattern);
            Vector2 origin = fist.transform.position;
            float duration = Vector2.Distance(origin, destination) / (speed * parameters.speed);
            damage *= parameters.damage;
            bool madeContact = false;
            float elapsed = 0;
            while (!arena.Finished)
            {
                elapsed += Time.deltaTime;
                float t = duration > 0 ? Mathf.Clamp01(elapsed / duration) : 1;
                Vector2 previous = fist.transform.position;
                Vector2 next = Vector2.Lerp(origin, destination, FistStrikeProgress(t, pattern));
                if (damage > 0 && !madeContact)
                {
                    Bounds contact = FistHitBounds(fist);
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
                if (t >= 1) yield break;
                yield return null;
            }
        }

        private IEnumerator ReturnFist(int index)
        {
            SpriteRenderer fist = fists[index];
            Vector2 start = fist.transform.position;
            Quaternion initialRotation = fist.transform.rotation;
            const float recovery = .55f;
            for (float elapsed = 0; elapsed < recovery && !arena.Finished; elapsed += Time.deltaTime)
            {
                float t = elapsed / recovery;
                float blend = Mathf.SmoothStep(0, 1, t);
                float arc = 4 * t * (1 - t);
                Vector2 position = Vector2.Lerp(start, FistOrbitPosition(index), blend) + Vector2.up * (.8f * arc * arc);
                fist.transform.SetPositionAndRotation(position, Quaternion.Slerp(initialRotation, Quaternion.identity, blend));
                yield return null;
            }
            if (!arena.Finished) fist.transform.SetPositionAndRotation(FistOrbitPosition(index), Quaternion.identity);
            fistBusy[index] = false;
        }
    }
}
