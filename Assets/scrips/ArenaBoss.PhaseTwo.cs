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
        private const float FourHandPairDelay = .62f;
        private const float GiantBurrowDistance = 12f;
        private const float GiantBurrowDepth = 11f;
        private const int RollingRockCount = 8;
        private const float RollingRockSpeed = 5f;
        private const float RollingRockSpacing = .45f;
        private const float RollingRockWarning = .28f;
        private bool giantBurrowing;
        private static readonly BossPattern[] EnragePatterns =
        {
            BossPattern.fist_cross_slam, BossPattern.fist_rolling_mines, BossPattern.fist_slam_chain, BossPattern.fist_pincer_combo,
            BossPattern.fist_floor_wave, BossPattern.fist_high_clap, BossPattern.fist_diagonal_rush,
            BossPattern.fist_rising_columns
        };
        private static readonly int[] FistRelayOrder = { 0, 3, 2, 1 };
        private bool EnhancedFourHandPatterns => !arena.Study || arena.Study.Protocol.enhancedFourHandPatterns;
        private bool LinkedFourHandPatterns => !arena.Study || arena.Study.Protocol.linkedFourHandPatterns;
        private float FourHandTimingScale => EnhancedFourHandPatterns ? .68f : 1f;
        private readonly float[] hockeyStrikeTimes = new float[2];
        private float hockeyHitstopUntil;
        internal bool HockeyHitstopActive => Time.time < hockeyHitstopUntil;

        private IEnumerator FightWithFists()
        {
            yield return new WaitForSeconds(.85f);
            BossPattern? previous = null;
            if (arena.Study)
            {
                while (!arena.Finished && !enraged)
                {
                    BossPattern pattern = arena.Study.Pick(StudySession.PhaseTwo, previous);
                    previous = pattern;
                    yield return RunNextFistPattern(pattern);
                    if (arena.Finished) break;
                    yield return new WaitForSeconds(.45f * arena.Parameters(pattern).interval);
                }
            }
            else
            {
                var patterns = (BossPattern[])StudySession.PhaseTwo.Clone();
                while (!arena.Finished && !enraged)
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
                        if (arena.Finished) break;
                        previous = pattern;
                        yield return RunNextFistPattern(pattern);
                        if (arena.Finished || enraged) break;
                        yield return new WaitForSeconds(.45f);
                    }
                }
            }

            if (arena.Finished) yield break;
            yield return new WaitForSeconds(Mathf.Max(0, enragedAt + EnrageRevealDuration - Time.time));
            int nextCombo = 0;
            bool enhanced = EnhancedFourHandPatterns;
            while (!arena.Finished)
            {
                BossPattern combo = EnragePatterns[nextCombo];
                nextCombo = (nextCombo + 1) % EnragePatterns.Length;
                if (!enhanced && combo == BossPattern.fist_rolling_mines) continue;
                int repetitions = enhanced && !LinkedFourHandPatterns && combo != BossPattern.fist_rolling_mines ? 2 : 1;
                for (int repeat = 0; repeat < repetitions && !arena.Finished; repeat++)
                {
                    yield return RunNextFistPattern(combo);
                    if (arena.Finished) yield break;
                    yield return new WaitForSeconds(.38f * FourHandTimingScale * arena.Parameters(combo).interval);
                }
            }
        }

        private IEnumerator RunNextFistPattern(BossPattern pattern)
        {
            if (Mathf.Abs(PlayerPosition.x - transform.position.x) >= GiantBurrowDistance
                && appearance && (!arena.Study || arena.Study.Protocol.phaseTwoBurrow))
            {
                bool wasEnraged = enraged;
                yield return BurrowTowardPlayer();
                if (arena.Finished || enraged != wasEnraged) yield break;
            }

            if (arena.Study)
            {
                arena.Study.Executed(pattern);
                if ((pattern is BossPattern.fist_jab or BossPattern.fist_uppercut or BossPattern.fist_backhand)
                    && Random.value < arena.Study.SupportRainChance)
                    SpawnFistSupportRain();
            }
            yield return RunFistPattern(pattern);
        }

        private IEnumerator BurrowTowardPlayer()
        {
            const float descentDuration = .45f;
            const float ascentDuration = .65f;
            Vector3 surface = transform.position;
            int handCount = enraged ? TotalFistCount : OriginalFistCount;
            giantBurrowing = true;
            giantFollowVelocity = 0;
            Intent = "골렘이 땅속으로 숨는다";
            int behindPlatforms = arena.BehindPlatformSortingOrder;
            for (int i = 0; i < handCount; i++) battleFists[i].sortingOrder = behindPlatforms;

            for (float elapsed = 0; elapsed < descentDuration && !arena.Finished; elapsed += Time.deltaTime)
            {
                PositionBurrowingGiant(surface, Mathf.SmoothStep(0, GiantBurrowDepth, elapsed / descentDuration), handCount);
                yield return null;
            }
            if (arena.Finished) yield break;

            PositionBurrowingGiant(surface, GiantBurrowDepth, handCount);
            SetHidden(true);
            yield return new WaitForSeconds(.12f);
            if (arena.Finished) yield break;

            surface.x = GiantTargetX;
            Intent = "골렘이 땅 위로 솟아오른다";
            PositionBurrowingGiant(surface, GiantBurrowDepth, handCount);
            SetHidden(false);
            var emergence = new Vector2(surface.x, arena.GroundY);
            arena.ShowRockLandingDust(emergence, 6, playSound: false);
            arena.ShakeImpact(emergence, Vector2.down * .25f, allowOffscreen: true);
            for (float elapsed = 0; elapsed < ascentDuration && !arena.Finished; elapsed += Time.deltaTime)
            {
                PositionBurrowingGiant(surface, Mathf.SmoothStep(GiantBurrowDepth, 0, elapsed / ascentDuration), handCount);
                yield return null;
            }
            if (arena.Finished) yield break;

            PositionBurrowingGiant(surface, 0, handCount);
            for (int i = 0; i < handCount; i++) battleFists[i].sortingOrder = fistSortingOrders[i % OriginalFistCount];
            giantBurrowing = false;
            // LateUpdate can trigger enrage before the next pattern starts.
            yield return null;
        }

        private void PositionBurrowingGiant(Vector3 surface, float depth, int handCount)
        {
            transform.position = surface + Vector3.down * depth;
            for (int i = 0; i < handCount; i++)
            {
                SpriteRenderer fist = battleFists[i];
                fist.transform.position = FistOrbitPosition(i) + Vector2.down * depth;
                Color color = i < OriginalFistCount ? Color.white : FistReleaseColor;
                color.a = Mathf.SmoothStep(0, 1, (fist.bounds.max.y - arena.GroundY) / .65f);
                fist.color = color;
                fist.enabled = color.a > 0;
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
            BossPattern.fist_cross_slam => LinkedFourHandPatterns ? RelayFistStrikes(pattern, "네 손 교차 내려찍기", 33, 25) : FourHandSlams(),
            BossPattern.fist_pincer_combo => LinkedFourHandPatterns ? RelayFistStrikes(pattern, "네 손 협공", 31, 24) : FourHandPincer(),
            BossPattern.fist_slam_chain => LinkedFourHandPatterns ? RelayFistStrikes(pattern, "연속 표적 내려찍기", 27, 19) : FourHandSlamChain(),
            BossPattern.fist_floor_wave => LinkedFourHandPatterns ? RelayFistStrikes(pattern, "낮은 양방향 쓸기", 23, 19) : FourHandHorizontal(high: false),
            BossPattern.fist_high_clap => LinkedFourHandPatterns ? RelayFistStrikes(pattern, "높은 박수", 23, 19) : FourHandHorizontal(high: true),
            BossPattern.fist_diagonal_rush => LinkedFourHandPatterns ? RelayFistStrikes(pattern, "엇갈린 대각선 돌진", 23, 20) : FourHandDiagonalRush(),
            BossPattern.fist_rising_columns => LinkedFourHandPatterns ? RelayFistStrikes(pattern, "네 손 상승 기둥", 24, 19) : FourHandRisingColumns(),
            BossPattern.fist_rolling_mines => CatchRollingMines(),
            _ => throw new System.ArgumentOutOfRangeException(nameof(pattern))
        };

        private IEnumerator FistSlams()
        {
            bool faster = !arena.Study || arena.Study.Protocol.fasterFistSlams;
            int slamCount = faster ? 12 : 8;
            const float speedIncreasePerSlam = .08f;
            float initialSpeed = faster ? 1.35f : 1;
            yield return PrepareSlam(0, initialSpeed);
            for (int slam = 0; slam < slamCount && !arena.Finished; slam++)
            {
                int index = slam % fists.Length;
                SpriteRenderer fist = fists[index];
                float speedMultiplier = initialSpeed + slam * speedIncreasePerSlam;
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
                Coroutine recovery = StartCoroutine(ReturnFist(index, faster ? .32f : .55f));
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
                            float timeSinceStrike = Time.time - hockeyStrikeTimes[i];
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
            hockeyHitstopUntil = Time.time + HockeyHitstopDuration;
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

        private IEnumerator WindUpRollingFists()
        {
            Intent = "구르는 돌 예고";
            fistBusy[0] = fistBusy[1] = true;
            Vector2 left = fists[0].transform.position;
            Vector2 right = fists[1].transform.position;
            Vector2 pullback = new Vector2(1.2f, .65f);
            var leftWindup = StartCoroutine(WindUpFist(0, left + pullback, left + Vector2.left, .55f));
            yield return WindUpFist(1, right + new Vector2(-pullback.x, pullback.y), right + Vector2.right, .55f);
            yield return leftWindup;
        }

        private IEnumerator RollingRocks()
        {
            yield return WindUpRollingFists();
            if (arena.Finished) yield break;

            Intent = "바닥 구르는 돌";
            StartCoroutine(ReturnFist(0));
            StartCoroutine(ReturnFist(1));
            for (int i = 0; i < RollingRockCount && !arena.Finished; i++)
            {
                Bounds bounds = arena.GroundBounds;
                float direction = i % 2 == 0 ? 1 : -1;
                float x = direction > 0 ? bounds.min.x + .6f : bounds.max.x - .6f;
                arena.SpawnRock(BossPattern.rolling_rocks, new Vector2(x, arena.GroundY + .6f),
                    Vector2.right * (direction * RollingRockSpeed), 16, RockFlight.Rolling, holdTime: RollingRockWarning);
                yield return new WaitForSeconds(RollingRockSpacing);
            }
        }

        private IEnumerator CatchRollingMines()
        {
            yield return WindUpRollingFists();
            if (arena.Finished) yield break;

            Intent = "바닥 구르는 돌";
            if (!arena.Study || arena.Study.Protocol.overlappingRollingMineThrows)
            {
                var catches = new Coroutine[TotalFistCount];
                for (int i = 0; i < RollingRockCount && !arena.Finished; i++)
                {
                    int slot = i % TotalFistCount;
                    int index = LinkedFourHandPatterns ? FistRelayOrder[slot] : slot;
                    catches[index] = StartCoroutine(CatchAndThrowMine(index, overlapping: true, previousCatch: catches[index]));
                    if (i + 1 < RollingRockCount) yield return new WaitForSeconds(RollingRockSpacing);
                }
                foreach (Coroutine job in catches)
                    if (job != null) yield return job;
                yield break;
            }

            StartCoroutine(ReturnFist(1, .3f));
            for (int i = 0; i < TotalFistCount && !arena.Finished; i++)
                yield return CatchAndThrowMine(i);
        }

        private IEnumerator CatchAndThrowMine(int index, bool overlapping = false, Coroutine previousCatch = null)
        {
            const BossPattern pattern = BossPattern.fist_rolling_mines;
            float reachDuration = overlapping ? .22f : .34f;
            float recovery = overlapping ? .18f : .33f;
            const float flightDuration = 1.35f;
            Bounds bounds = arena.GroundBounds;
            SpriteRenderer fist = battleFists[index];
            float direction = index % 2 == 0 ? 1 : -1;
            float edge = direction > 0 ? bounds.min.x : bounds.max.x;
            float startX = edge + direction * .6f;
            float catchX = edge + direction * Mathf.Min(index < OriginalFistCount ? 4.2f : 5.7f, bounds.extents.x);
            bool aimedThrow = overlapping || !arena.Study || arena.Study.Protocol.aimedRollingMineThrow;
            bool stagedThrow = aimedThrow || arena.Study.Protocol.stagedRollingMineThrow;
            if (stagedThrow)
            {
                if (!overlapping) Intent = "바닥 구르는 돌";
                float left = Mathf.Max(bounds.min.x, arena.ScreenEdgeX(right: false));
                float right = Mathf.Min(bounds.max.x, arena.ScreenEdgeX(right: true));
                if (right > left)
                {
                    float entry = direction > 0 ? left : right;
                    float exit = direction > 0 ? right : left;
                    startX = Mathf.Lerp(entry, exit, .08f);
                    catchX = overlapping
                        ? startX + direction * Mathf.Min(3f, (right - left) * .42f)
                        : Mathf.Lerp(entry, exit, index < OriginalFistCount ? .47f : .51f);
                }
            }
            catchX = ClampFistX(catchX, fist);
            float rollY = arena.GroundY + .6f;
            var catchPosition = new Vector2(catchX, arena.GroundY + fist.bounds.extents.y);
            var rollStart = new Vector2(startX, rollY);
            float rollSpeed = overlapping || index == 0 ? RollingRockSpeed : stagedThrow ? 7f : 9f;
            float speedFactor = arena.Parameters(pattern).speed;
            float rollDistance = Mathf.Abs(catchX - startX);
            if (aimedThrow && !overlapping)
            {
                const float visibleRollBeforeReach = .35f;
                rollSpeed = Mathf.Min(rollSpeed, Mathf.Max(.1f, rollDistance / ((visibleRollBeforeReach + reachDuration) * speedFactor)));
            }
            ArenaProjectile rock = arena.SpawnRock(pattern, rollStart, Vector2.right * (direction * rollSpeed),
                16, RockFlight.Rolling, holdTime: RollingRockWarning);
            uint spawnVersion = rock ? rock.SpawnVersion : 0;
            if (rock) rock.CatchAt(catchX, rollThroughContact: stagedThrow);
            if (stagedThrow && rock)
            {
                float travelTime = rollDistance / (rollSpeed * speedFactor);
                float reachDelay = RollingRockWarning + travelTime - reachDuration;
                yield return new WaitForSeconds(Mathf.Max(overlapping ? RollingRockWarning + .15f : 0, reachDelay));
            }
            if (previousCatch != null) yield return previousCatch;
            if (arena.Finished) yield break;

            fistBusy[index] = true;
            fist.flipX = direction < 0;
            yield return WindUpFist(index, catchPosition + Vector2.up * .8f, catchPosition, reachDuration);
            while (!arena.Finished && rock && rock.SpawnVersion == spawnVersion && rock.IsRollingToFist) yield return null;
            if (arena.Finished) yield break;

            if (rock && rock.SpawnVersion == spawnVersion && rock.IsHeldByFist)
            {
                Intent = stagedThrow ? "돌을 잡고 투척 준비" : "구르는 돌을 잡아 지뢰 투척";
                arena.PlayEffect(fistImpactSound);
                Vector2 grip = (Vector2)rock.transform.position - catchPosition;
                Vector2 raised = catchPosition + Vector2.up * 3.5f;
                if (aimedThrow)
                {
                    yield return AimAndThrowMine(fist, rock, grip, raised);
                    if (arena.Finished) yield break;
                    yield return ReturnFist(index, recovery);
                    yield break;
                }
                if (stagedThrow)
                {
                    float throwDirection = PlayerPosition.x >= catchX ? 1 : -1;
                    fist.flipX = throwDirection < 0;
                    var pullback = new Vector2(ClampFistX(catchX - throwDirection * 2.2f, fist), raised.y);
                    yield return CarryRock(fist, rock, grip, pullback, .44f, 18f * throwDirection);
                    if (!arena.Finished && rock.IsHeldByFist)
                    {
                        yield return new WaitForSeconds(.24f);
                        var release = new Vector2(ClampFistX(catchX + throwDirection * 1.25f, fist), raised.y + .35f);
                        yield return CarryRock(fist, rock, grip, release, .16f, -12f * throwDirection);
                    }
                }
                else yield return CarryRock(fist, rock, grip, raised, .27f, 0);
                if (arena.Finished) yield break;
                if (rock.IsHeldByFist)
                {
                    Intent = "구르는 돌을 잡아 지뢰 투척";
                    float targetX = Mathf.Clamp(PlayerPosition.x, bounds.min.x + 1, bounds.max.x - 1);
                    var destination = new Vector2(targetX, arena.GroundY + ArenaProjectile.MineLandingOffset);
                    Vector2 velocity = (destination - (Vector2)rock.transform.position) / flightDuration;
                    velocity.y += ArenaProjectile.MineGravity * flightDuration * .5f;
                    arena.Flash(new Vector2(targetX, arena.GroundY + .12f), new Vector2(1.5f, .24f), FistLaneColor, flightDuration);
                    rock.ThrowHeldMine(velocity, 15);
                    fist.transform.position += (Vector3)velocity.normalized * .6f;
                }
            }

            yield return ReturnFist(index, recovery);
        }

        private IEnumerator AimAndThrowMine(SpriteRenderer fist, ArenaProjectile rock, Vector2 grip, Vector2 raised)
        {
            const float aimDuration = .7f;
            const float pullbackDuration = .5f;
            const float throwSpeed = 80f;
            bool aimAtPlayer = !arena.Study || arena.Study.Protocol.aimRollingMinesAtPlayer;
            Bounds bounds = arena.GroundBounds;
            float floor = arena.GroundY + ArenaProjectile.MineLandingOffset;
            var aimColor = new Color(1, .3f, .16f, .85f);
            Intent = "돌을 들어 조준";
            yield return CarryRock(fist, rock, grip, raised, .18f, 0);
            if (arena.Finished || !rock.IsHeldByFist) yield break;

            Vector2 destination = PlayerPosition;
            GameObject aimLine = arena.Flash(rock.transform.position, Vector2.one, aimColor, aimDuration);
            for (float elapsed = 0; elapsed < aimDuration && !arena.Finished && rock.IsHeldByFist; elapsed += Time.deltaTime)
            {
                Vector2 player = PlayerPosition;
                destination = aimAtPlayer ? player : new Vector2(Mathf.Clamp(player.x, bounds.min.x + 1, bounds.max.x - 1), floor);
                fist.flipX = destination.x < rock.transform.position.x;
                SetThrowLine(aimLine, rock.transform.position, destination, .065f);
                yield return null;
            }
            if (arena.Finished || !rock.IsHeldByFist) yield break;

            Intent = "조준을 고정하고 손을 뒤로 뺀다";
            float direction = fist.flipX ? -1 : 1;
            var pullback = new Vector2(ClampFistX(raised.x - direction * 3f, fist), raised.y + .7f);
            yield return CarryRock(fist, rock, grip, pullback, pullbackDuration, 28f * direction);
            float forwardReach = Mathf.Min(1.1f, Mathf.Abs(destination.x - raised.x) * .5f);
            var release = new Vector2(ClampFistX(raised.x + direction * forwardReach, fist), raised.y - .35f);
            yield return CarryRock(fist, rock, grip, release, .08f, -20f * direction);
            if (arena.Finished || !rock.IsHeldByFist) yield break;

            Intent = "구르는 돌을 잡아 지뢰 투척";
            Vector2 origin = rock.transform.position;
            rock.ThrowHeldMine((destination - origin).normalized * throwSpeed, 15, destination, fallAfterTarget: aimAtPlayer);
            SetThrowLine(arena.Flash(origin, Vector2.one, new Color(1, .75f, .42f, .65f), .08f),
                origin, destination, .16f);
            arena.PlayEffect(fistImpactSound);
        }

        private static void SetThrowLine(GameObject line, Vector2 origin, Vector2 destination, float width)
        {
            if (!line || !line.activeSelf) return;
            Vector2 path = destination - origin;
            float angle = Mathf.Atan2(path.y, path.x) * Mathf.Rad2Deg;
            line.transform.SetPositionAndRotation((origin + destination) * .5f, Quaternion.Euler(0, 0, angle));
            line.transform.localScale = new Vector3(path.magnitude, width, 1);
        }

        private IEnumerator CarryRock(SpriteRenderer fist, ArenaProjectile rock, Vector2 grip,
            Vector2 destination, float duration, float tilt)
        {
            Vector2 start = fist.transform.position;
            Quaternion startRotation = fist.transform.rotation;
            Quaternion endRotation = Quaternion.Euler(0, 0, tilt);
            for (float elapsed = 0; elapsed < duration && !arena.Finished && rock.IsHeldByFist; elapsed += Time.deltaTime)
            {
                float t = Mathf.SmoothStep(0, 1, elapsed / duration);
                Vector2 position = Vector2.Lerp(start, destination, t);
                fist.transform.SetPositionAndRotation(position, Quaternion.Slerp(startRotation, endRotation, t));
                rock.transform.position = position + grip;
                yield return null;
            }
            if (!arena.Finished && rock.IsHeldByFist)
            {
                fist.transform.SetPositionAndRotation(destination, endRotation);
                rock.transform.position = destination + grip;
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
            clearance = Mathf.Min(clearance, bounds.extents.x);
            return Mathf.Clamp(x, bounds.min.x + clearance, bounds.max.x - clearance);
        }

        private IEnumerator RelayFistStrikes(BossPattern pattern, string attack, float speed, float damage)
        {
            Intent = attack + " 예고";
            var strikes = new Coroutine[TotalFistCount];
            int mirror = PlayerPosition.x > transform.position.x ? 1 : 0;
            int releases = 0;
            System.Action released = () => releases++;
            for (int beat = 0; beat < strikes.Length && !arena.Finished; beat++)
            {
                int index = FistRelayOrder[beat] ^ mirror;
                SpriteRenderer fist = battleFists[index];
                Vector2 target = PlayerPosition;
                float side = index % 2 == 0 ? -1 : 1;
                float hitbox = arena.Parameters(pattern).hitbox;
                float low = arena.GroundY + fist.bounds.extents.y;
                float high = arena.GroundY + fist.bounds.extents.y * hitbox + 1.55f;
                float overhead = Mathf.Max(low + 6.7f, target.y + 3.6f);
                bool opening = beat < 2;

                // Each warning snapshots a fresh player position, then stays fixed through the strike.
                (float fromX, float fromY, float toX, float toY) lane = pattern switch
                {
                    BossPattern.fist_cross_slam => (side * (opening ? 7f : 4.5f), overhead, side * (opening ? 3.6f : 1.25f), low),
                    BossPattern.fist_slam_chain => (0, overhead, 0, low),
                    BossPattern.fist_pincer_combo => (side * 6.5f, opening ? low : high + 2.4f, side * .8f, opening ? low : high),
                    BossPattern.fist_floor_wave => (side * 7.5f, low, -side * (opening ? 2.5f : 4.5f), low),
                    BossPattern.fist_high_clap => (side * 6.8f, opening ? high : high + 1.7f, -side * 1.4f, high),
                    BossPattern.fist_diagonal_rush => (side * 6.7f, opening ? overhead : low, -side * 3.7f, opening ? low : high + 2.5f),
                    BossPattern.fist_rising_columns => (side * (opening ? 3.5f : .85f), low, side * (opening ? 3.5f : .85f), overhead),
                    _ => throw new System.ArgumentOutOfRangeException(nameof(pattern))
                };
                var start = new Vector2(ClampFistX(target.x + lane.fromX, fist), lane.fromY);
                var end = new Vector2(ClampFistX(target.x + lane.toX, fist), lane.toY);
                strikes[beat] = StartCoroutine(StrikeFist(index, start, end, pattern, attack, speed, damage, released));
                while (releases <= beat && !arena.Finished) yield return null;
            }
            foreach (Coroutine strike in strikes)
                if (strike != null) yield return strike;
        }

        private IEnumerator FourHandSlams()
        {
            const BossPattern pattern = BossPattern.fist_cross_slam;
            Vector2 target = PlayerPosition;
            Vector2 half = battleFists[0].bounds.extents;
            float outer = half.x * 2.3f + 1.8f;
            // Leave enough space to stand between the two inner strikes.
            float inner = battleFists[2].bounds.extents.x * arena.Parameters(pattern).hitbox
                + arena.Player.HurtboxHalfSize.x + .2f;
            float center = ClampPairedFistCenter(target.x, outer + half.x);
            var outside = StartCoroutine(SlamPair(0, center, outer, target.y));
            yield return new WaitForSeconds(FourHandPairDelay * FourHandTimingScale * arena.Parameters(pattern).interval);
            if (arena.Finished) yield break;
            yield return SlamPair(2, center, inner, target.y);
            yield return outside;
        }

        private IEnumerator SlamPair(int first, float center, float spacing, float targetY)
        {
            const BossPattern pattern = BossPattern.fist_cross_slam;
            const string attack = "네 손 교차 내려찍기";
            float bottom = arena.GroundY + battleFists[first].bounds.extents.y;
            float top = Mathf.Max(bottom + 7, targetY + 4);
            float leftX = ClampFistX(center - spacing, battleFists[first]);
            float rightX = ClampFistX(center + spacing, battleFists[first + 1]);
            var left = StartCoroutine(StrikeFist(first, new Vector2(leftX, top), new Vector2(leftX, bottom),
                pattern, attack, 33, 25));
            yield return StrikeFist(first + 1, new Vector2(rightX, top), new Vector2(rightX, bottom),
                pattern, attack, 33, 25);
            yield return left;
        }

        private IEnumerator FourHandPincer()
        {
            const BossPattern pattern = BossPattern.fist_pincer_combo;
            Vector2 target = PlayerPosition;
            Vector2 half = battleFists[0].bounds.extents;
            float outer = half.x + 5.1f;
            float center = ClampPairedFistCenter(target.x, outer + half.x);
            const string attack = "네 손 협공";
            var sweep = StartCoroutine(PincerPair(0, center, outer, arena.GroundY + half.y, 0, pattern, attack, 31, 24));
            yield return new WaitForSeconds(FourHandPairDelay * FourHandTimingScale * arena.Parameters(pattern).interval);
            if (arena.Finished) yield break;
            yield return PincerPair(2, center, outer, Mathf.Max(arena.GroundY + 5.2f, target.y), 2.1f, pattern, attack, 31, 24);
            yield return sweep;
        }

        private IEnumerator PincerPair(int first, float center, float outer, float targetY, float approachHeight,
            BossPattern pattern, string attack, float speed, float damage)
        {
            float inner = battleFists[first].bounds.extents.x * .75f;
            var left = StartCoroutine(StrikeFist(first, new Vector2(center - outer, targetY + approachHeight),
                new Vector2(center - inner, targetY), pattern, attack, speed, damage));
            yield return StrikeFist(first + 1, new Vector2(center + outer, targetY + approachHeight),
                new Vector2(center + inner, targetY), pattern, attack, speed, damage);
            yield return left;
        }

        private IEnumerator FourHandSlamChain()
        {
            const BossPattern pattern = BossPattern.fist_slam_chain;
            var strikes = new Coroutine[TotalFistCount];
            float delay = .57f * FourHandTimingScale * arena.Parameters(pattern).interval;
            for (int i = 0; i < strikes.Length; i++)
            {
                if (arena.Finished) yield break;
                Vector2 target = PlayerPosition;
                SpriteRenderer fist = battleFists[i];
                float x = ClampFistX(target.x, fist);
                float bottom = arena.GroundY + fist.bounds.extents.y;
                var start = new Vector2(x, Mathf.Max(bottom + 6.5f, target.y + 4));
                strikes[i] = StartCoroutine(StrikeFist(i, start, new Vector2(x, bottom),
                    pattern, "연속 표적 내려찍기", 27, 19));
                if (i + 1 < strikes.Length) yield return new WaitForSeconds(delay);
            }
            foreach (Coroutine strike in strikes) yield return strike;
        }

        private IEnumerator FourHandHorizontal(bool high)
        {
            BossPattern pattern = high ? BossPattern.fist_high_clap : BossPattern.fist_floor_wave;
            string attack = high ? "높은 박수" : "낮은 양방향 쓸기";
            Vector2 half = battleFists[0].bounds.extents;
            float outer = half.x + 5.8f;
            float center = ClampPairedFistCenter(PlayerPosition.x, outer + half.x);
            float hitbox = arena.Parameters(pattern).hitbox;
            float bottom = arena.GroundY + (high ? 1.45f : -half.y * .45f);
            float firstY = bottom + half.y * hitbox;
            float secondY = bottom + battleFists[2].bounds.extents.y * hitbox;
            var first = StartCoroutine(PincerPair(0, center, outer, firstY, 0, pattern, attack, 23, 19));
            yield return new WaitForSeconds(.78f * FourHandTimingScale * arena.Parameters(pattern).interval);
            yield return PincerPair(2, center, outer, secondY, 0, pattern, attack, 23, 19);
            yield return first;
        }

        private IEnumerator FourHandDiagonalRush()
        {
            const BossPattern pattern = BossPattern.fist_diagonal_rush;
            float outer = battleFists[0].bounds.extents.x + 6f;
            float center = ClampPairedFistCenter(PlayerPosition.x, outer + battleFists[0].bounds.extents.x);
            var first = StartCoroutine(DiagonalPair(0, center, outer, leftDives: true));
            yield return new WaitForSeconds(FourHandPairDelay * FourHandTimingScale * arena.Parameters(pattern).interval);
            yield return DiagonalPair(2, center, outer, leftDives: false);
            yield return first;
        }

        private IEnumerator DiagonalPair(int first, float center, float outer, bool leftDives)
        {
            const BossPattern pattern = BossPattern.fist_diagonal_rush;
            const string attack = "엇갈린 대각선 돌진";
            Vector2 half = battleFists[first].bounds.extents;
            float inner = half.x + arena.Player.HurtboxHalfSize.x + .7f;
            float low = arena.GroundY + half.y;
            float high = low + 5.2f;
            var left = StartCoroutine(StrikeFist(first, new Vector2(center - inner, leftDives ? high : low),
                new Vector2(center - outer, leftDives ? low : high), pattern, attack, 23, 20));
            yield return StrikeFist(first + 1, new Vector2(center + inner, leftDives ? low : high),
                new Vector2(center + outer, leftDives ? high : low), pattern, attack, 23, 20);
            yield return left;
        }

        private IEnumerator FourHandRisingColumns()
        {
            const BossPattern pattern = BossPattern.fist_rising_columns;
            float halfWidth = battleFists[0].bounds.extents.x;
            float outer = halfWidth + 4.2f;
            float center = ClampPairedFistCenter(PlayerPosition.x, outer + halfWidth);
            var first = StartCoroutine(RisingPair(0, center - outer, center + outer));
            yield return new WaitForSeconds(.72f * FourHandTimingScale * arena.Parameters(pattern).interval);
            yield return RisingPair(2, center, center + (DirectionToPlayer < 0 ? -outer : outer) * .78f);
            yield return first;
        }

        private IEnumerator RisingPair(int first, float leftX, float rightX)
        {
            const BossPattern pattern = BossPattern.fist_rising_columns;
            const string attack = "네 손 상승 기둥";
            float bottom = arena.GroundY + battleFists[first].bounds.extents.y;
            const float rise = 6.5f;
            var left = StartCoroutine(StrikeFist(first, new Vector2(leftX, bottom), new Vector2(leftX, bottom + rise),
                pattern, attack, 24, 19));
            yield return StrikeFist(first + 1, new Vector2(rightX, bottom), new Vector2(rightX, bottom + rise),
                pattern, attack, 24, 19);
            yield return left;
        }

        private IEnumerator StrikeFist(int index, Vector2 start, Vector2 end, BossPattern pattern,
            string attack, float speed, float damage, System.Action onRelease = null)
        {
            if (arena.Finished) yield break;
            SpriteRenderer fist = battleFists[index];
            var parameters = arena.Parameters(pattern);
            bool combo = pattern is BossPattern.fist_cross_slam or BossPattern.fist_pincer_combo;
            float timing = pattern >= BossPattern.fist_cross_slam ? FourHandTimingScale : 1f;
            float lead = ((combo ? .33f : .35f) + Mathf.Clamp(.58f * parameters.interval, .5f, .72f)) * timing;
            Vector2 path = end - start;
            Vector2 direction = path.normalized;
            Vector2 size = fist.bounds.size * parameters.hitbox;
            float along = Mathf.Abs(direction.x) * size.x + Mathf.Abs(direction.y) * size.y;
            float across = Mathf.Abs(direction.y) * size.x + Mathf.Abs(direction.x) * size.y;
            var lane = arena.Flash((start + end) * .5f, new Vector2(path.magnitude + along, across), FistLaneColor, lead);
            if (lane) lane.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(path.y, path.x) * Mathf.Rad2Deg);

            fistBusy[index] = true;
            if (path.x != 0) fist.flipX = path.x < 0;
            if (onRelease == null) Intent = attack + " 예고";
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
            if (onRelease != null)
            {
                var streak = arena.Flash((start + end) * .5f, new Vector2(path.magnitude + along, across * .48f),
                    new Color(1, .82f, .55f, .5f), .1f);
                if (streak) streak.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(path.y, path.x) * Mathf.Rad2Deg);
                onRelease();
            }
            yield return MoveFist(fist, end, speed / Mathf.Lerp(1, timing, .65f), damage, pattern);
            if (arena.Finished) yield break;
            if (pattern is BossPattern.fist_double_slam or BossPattern.fist_cross_slam or BossPattern.fist_slam_chain)
            {
                var impact = new Vector2(end.x, arena.GroundY);
                arena.ShowRockLandingDust(impact, fist.bounds.size.x, playSound: false);
                if (onRelease != null || index % 2 == 0 || pattern == BossPattern.fist_slam_chain)
                {
                    arena.ShakeImpact(impact, Vector2.down * .32f);
                    arena.PlayEffect(fistSlamSound);
                }
            }
            yield return ReturnFist(index, .55f * timing);
        }

        private IEnumerator WindUpFist(int index, Vector2 pose, Vector2 ready, float duration, float tilt = 0)
        {
            SpriteRenderer fist = battleFists[index];
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
                BossPattern.fist_slams or BossPattern.fist_double_slam or BossPattern.fist_cross_slam
                    or BossPattern.fist_slam_chain => t * t * t,
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
                if (arena.Frozen)
                {
                    yield return null;
                    continue;
                }
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

        private IEnumerator ReturnFist(int index, float recovery = .55f)
        {
            SpriteRenderer fist = battleFists[index];
            Vector2 start = fist.transform.position;
            Quaternion initialRotation = fist.transform.rotation;
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
