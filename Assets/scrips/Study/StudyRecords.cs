using System;
using System.Collections.Generic;

namespace BossTemplate
{
    internal enum BossPattern
    {
        ground_barrage, fault_line, crossfire, ceiling_collapse, stone_nova, dash, spread,
        skipping_rocks, teleport_slam, rock_rain, fist_slams, air_hockey, fist_sweep, throw_mines, rolling_rocks,
        fist_jab, fist_uppercut, fist_backhand, fist_clap, fist_double_slam,
        fist_cross_slam, fist_pincer_combo, fist_slam_chain, fist_floor_wave, fist_high_clap,
        fist_diagonal_rush, fist_rising_columns, fist_rolling_mines
    }

    [Serializable] internal sealed class ParticipantRecord
    {
        public string participant_id, age_group, gaming_experience, consented_at, consent_version, settings_version;
        public bool consented;
    }

    [Serializable] internal sealed class StudyAssignment
    {
        public string experiment_group, assignment_version;
        public int target_attempts;
        internal bool FixedHard => experiment_group == "D";

        internal static StudyAssignment ForOfflineParticipant(Guid participantId)
        {
            // Match the server's offline-v1 allocation using the final byte of the random UUID.
            int slot = participantId.ToByteArray()[15] % 4;
            return new StudyAssignment
            {
                experiment_group = "ABCD"[slot].ToString(),
                target_attempts = slot == 0 ? 3 : slot == 1 ? 7 : 12,
                assignment_version = "offline-v1",
            };
        }

        internal bool Matches(StudyAssignment other) => other != null
            && experiment_group == other.experiment_group && target_attempts == other.target_attempts
            && assignment_version == other.assignment_version;
    }

    [Serializable] internal sealed class PatternParameters
    {
        public string pattern_id;
        public float speed = 1, interval = 1, hitbox = 1, damage = 1, selection_weight = 1;
    }

    [Serializable] internal sealed class DifficultySnapshot
    {
        public float level, boss_health;
        public PatternParameters[] patterns;

        internal PatternParameters ParametersFor(BossPattern pattern) =>
            patterns != null && (uint)pattern < (uint)patterns.Length ? patterns[(int)pattern] : null;

        internal static float SelectionWeightFor(DifficultySnapshot difficulty, BossPattern pattern) =>
            difficulty == null ? AdaptiveDifficulty.Neutral.selection_weight
                : difficulty.ParametersFor(pattern)?.selection_weight ?? 0;

        internal static float SelectionWeightTotal(DifficultySnapshot difficulty, BossPattern[] candidates, BossPattern? excluded)
        {
            float total = 0;
            foreach (var pattern in candidates)
                if (pattern != excluded)
                    total += SelectionWeightFor(difficulty, pattern);
            return total;
        }
    }

    [Serializable] internal sealed class DifficultyChange
    {
        public string applied_at, reason;
        public float previous_level, new_level;
    }

    [Serializable] internal sealed class PatternStat
    {
        public string pattern_id;
        public int executions, hit_count;
        public float average_damage, damage_taken;
    }

    [Serializable] internal sealed class AttemptRecord
    {
        public string participant_id, attempt_id, experiment_group, assignment_version, settings_version;
        public string started_at, ended_at, result;
        public int attempt_number, target_attempts;
        public float duration_seconds, damage_taken, damage_dealt, boss_hp_remaining = 1;
        public int boss_phase;
        public float phase_progress_percent;
        public bool cleared;
        public PatternStat[] pattern_stats;
        public DifficultySnapshot difficulty;
        public DifficultyChange[] difficulty_changes;
    }

    [Serializable] internal sealed class SurveyRecord
    {
        public string participant_id, settings_version, submitted_at;
        public bool cleared, skipped;
        public int actual_attempts;
        public int[] scores;
    }

    [Serializable] internal sealed class WithdrawalRecord
    {
        public string participant_id;
    }

    [Serializable] internal sealed class PendingSubmission
    {
        public string route, json;
    }

    [Serializable] internal sealed class StudySave
    {
        public int schema = 1;
        public ParticipantRecord participant;
        public StudyProtocol protocol;
        public StudyAssignment assignment;
        public string token;
        public int attempts, deaths;
        public AttemptRecord active;
        public AttemptRecord lastAttempt;
        public List<AttemptRecord> recent = new List<AttemptRecord>();
        public List<PendingSubmission> pending = new List<PendingSubmission>();
        public bool surveyDue, cleared, debrief, declined, withdrawalPending, withdrawn;
        public bool offlineRegistrationPending;
        public string withdrawalParticipantId, withdrawalApiUrl;
    }
}
