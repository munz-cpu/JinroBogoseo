using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossTemplate
{
    internal static class AdaptiveDifficulty
    {
        internal static readonly BossPattern[] Patterns = (BossPattern[])Enum.GetValues(typeof(BossPattern));
        internal static readonly PatternParameters Neutral = new PatternParameters();
        private const float HardPlateau = .90f;
        private const float EasyPlateau = .12f;

        internal static DifficultySnapshot Choose(StudyProtocol settings, IReadOnlyList<AttemptRecord> history,
            int nextAttempt, int target, string now, out DifficultyChange change, bool fixedHard = false)
        {
            float minimum = Mathf.Clamp01(Mathf.Min(settings.minimumLevel, settings.maximumLevel));
            float maximum = Mathf.Clamp01(Mathf.Max(settings.minimumLevel, settings.maximumLevel));
            if (fixedHard) history = Array.Empty<AttemptRecord>();

            int easingStart = Mathf.Max(1, target - 2);
            int easingEnd = target + 3;
            float progress = Mathf.InverseLerp(easingStart, easingEnd, nextAttempt);
            float level = fixedHard ? maximum : Mathf.Lerp(minimum, maximum,
                Mathf.SmoothStep(HardPlateau, EasyPlateau, progress));
            AttemptRecord last = history.Count > 0 ? history[history.Count - 1] : null;
            string reason = fixedHard ? "fixed_hard" : nextAttempt == 1 ? "initial"
                : nextAttempt > easingStart && nextAttempt <= easingEnd ? "near_target" : "hold";
            change = new DifficultyChange
            {
                applied_at = now, previous_level = last?.difficulty.level ?? level, new_level = level, reason = reason
            };
            var profile = new DifficultySnapshot
            {
                level = level,
                boss_health = Mathf.Lerp(Mathf.Clamp(settings.bossHealth.x, .94f, 1.12f),
                    Mathf.Clamp(settings.bossHealth.y, .94f, 1.12f), level),
                patterns = new PatternParameters[Patterns.Length]
            };
            foreach (var pattern in Patterns)
            {
                string id = pattern.ToString();
                PatternRange range = Array.Find(settings.patternOverrides ?? Array.Empty<PatternRange>(),
                    item => item != null && item.patternId == id) ?? settings.defaultPattern ?? new PatternRange();
                float vulnerability = Vulnerability(history, (int)pattern);
                // High damage per execution indicates a weak response. Give that pattern less weight on easier runs.
                float selectionLevel = Mathf.Clamp01(level - (1 - level) * vulnerability * .35f);
                profile.patterns[(int)pattern] = new PatternParameters
                {
                    pattern_id = id,
                    selection_weight = Mathf.Clamp(Mathf.Lerp(range.selectionWeight.x, range.selectionWeight.y,
                        selectionLevel), .1f, 5f)
                };
            }
            return profile;
        }

        private static float Vulnerability(IReadOnlyList<AttemptRecord> history, int pattern)
        {
            int executions = 0;
            float damage = 0;
            foreach (var attempt in history)
            {
                if (pattern >= attempt.pattern_stats.Length) continue;
                var stat = attempt.pattern_stats[pattern];
                executions += stat.executions;
                damage += stat.average_damage * stat.hit_count;
            }
            return Mathf.Clamp01(damage / Mathf.Max(1, executions * 25f));
        }

        internal static float SupportRainChance(float level) => Mathf.Lerp(.05f, .55f, level);
    }
}
