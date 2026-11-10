using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossTemplate
{
    internal static class AdaptiveDifficulty
    {
        internal static readonly BossPattern[] Patterns = (BossPattern[])Enum.GetValues(typeof(BossPattern));
        internal static readonly PatternParameters Neutral = new PatternParameters();

        internal static DifficultySnapshot Choose(StudyProtocol settings, IReadOnlyList<AttemptRecord> history,
            int nextAttempt, int target, string now, out DifficultyChange change, bool fixedHard = false)
        {
            float minimum = Mathf.Clamp01(Mathf.Min(settings.minimumLevel, settings.maximumLevel));
            float maximum = Mathf.Clamp01(Mathf.Max(settings.minimumLevel, settings.maximumLevel));
            if (fixedHard) history = Array.Empty<AttemptRecord>();
            AttemptRecord last = history.Count > 0 ? history[history.Count - 1] : null;
            float previous = fixedHard ? maximum
                : last == null ? Mathf.Clamp(settings.initialLevel, minimum, maximum) : last.difficulty.level;
            float level = previous;
            string reason = fixedHard ? "fixed_hard" : "initial";

            if (last != null)
            {
                // The newest run dominates, but recent boss progress and survival reduce single-run noise.
                float trend = 0;
                float progress = 0;
                float survival = 0;
                float weightSum = 0;
                for (int i = 0; i < history.Count; i++)
                {
                    float weight = i + 1;
                    progress += (1 - history[i].boss_hp_remaining) * weight;
                    survival += Mathf.Clamp01(history[i].duration_seconds / 90f) * weight;
                    weightSum += weight;
                }
                if (history.Count > 1)
                    trend = Mathf.Clamp(history[0].boss_hp_remaining - last.boss_hp_remaining, -.4f, .4f);
                float pressure = Mathf.Clamp01(last.damage_taken / Mathf.Max(30, last.duration_seconds * 3));
                float readiness = .65f * progress / weightSum + .20f * survival / weightSum
                    + .35f * trend - .15f * pressure;
                int remaining = target - nextAttempt;
                float step = Mathf.Clamp(settings.maximumStep, .01f, .25f);

                reason = "hold";
                if (remaining <= 1)
                {
                    level -= step * (remaining <= 0 ? 1f : .6f);
                    reason = "near_target";
                }
                else if (readiness < .27f)
                {
                    level -= step * .65f;
                    reason = "struggling";
                }
                else if (readiness > .57f && last.result == "defeat")
                {
                    level += step * Mathf.Clamp01((readiness - .5f) * 3);
                    reason = "ahead_of_target";
                }
            }

            level = Mathf.Clamp(level, minimum, maximum);
            change = new DifficultyChange
            {
                applied_at = now, previous_level = previous, new_level = level, reason = reason
            };
            var profile = new DifficultySnapshot
            {
                level = level, boss_health = Factor(settings.bossHealth, level, .45f, 1.7f),
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
                    speed = Factor(range.speed, level, .7f, 1.3f),
                    interval = Factor(range.interval, level, .7f, 1.6f),
                    hitbox = Factor(range.hitbox, level, .7f, 1.2f),
                    damage = Factor(range.damage, level, .45f, 1.6f),
                    selection_weight = Factor(range.selectionWeight, selectionLevel, .1f, 5f)
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

        private static float Factor(Vector2 bounds, float level, float minimum, float maximum) =>
            Mathf.Clamp(Mathf.Lerp(bounds.x, bounds.y, level), minimum, maximum);
    }
}
