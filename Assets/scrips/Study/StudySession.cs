using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace BossTemplate
{
    internal enum StudyScreen { Consent, Waiting, Ready, Combat, Survey, Debrief, Declined, Withdrawal, Error, Title }

    internal sealed class StudySession : MonoBehaviour
    {
        internal static StudySession Current { get; private set; }
        internal static readonly BossPattern[] PhaseOne =
        {
            BossPattern.ground_barrage, BossPattern.fault_line, BossPattern.crossfire, BossPattern.ceiling_collapse,
            BossPattern.stone_nova, BossPattern.dash, BossPattern.spread, BossPattern.skipping_rocks, BossPattern.teleport_slam
        };
        internal static readonly BossPattern[] PhaseTwo =
        {
            BossPattern.fist_slams, BossPattern.air_hockey, BossPattern.fist_sweep, BossPattern.throw_mines, BossPattern.rolling_rocks,
            BossPattern.fist_jab, BossPattern.fist_uppercut, BossPattern.fist_backhand, BossPattern.fist_clap, BossPattern.fist_double_slam
        };

        private BossArena arena;
        private StudySettings asset;
        private StudyStorage storage;
        private StudySave state;
        private StudyPanel panel;
        private float startTime, checkpointAt, nextSend;
        private int failures;
        private bool sending, fatal, permanentError, changingScene, quitting;
        private StudyStage stage;
        private string syncStatus = "";
        internal StudyScreen Screen { get; private set; }
        internal bool ConsumesShot(Vector2 pointer) => panel && panel.ConsumesShot(pointer);
        internal bool InCombat => Screen == StudyScreen.Combat;
        internal bool Cleared => state.cleared;
        internal bool LostLast => state.recent.Count > 0 && state.recent[state.recent.Count - 1].result == "defeat";
        internal bool WithdrawalPending => state.withdrawalPending;
        // A save written before enrollment can deserialize with an empty protocol object.
        internal StudyProtocol Protocol => (state?.participant != null ? state.protocol : null) ?? asset.protocol;
        internal bool HasPending => state?.pending.Count > 0;
        internal string SyncStatus => syncStatus;
        internal string ParticipantId => state.participant?.participant_id ?? state.withdrawalParticipantId ?? "";
        internal float BossHealthFactor => state?.active?.difficulty.boss_health ?? 1;
        internal static string Now() => DateTime.UtcNow.ToString("O");

#if UNITY_EDITOR
        internal StudyAssignment InspectorAssignment => state?.assignment;
        internal int InspectorAttemptCount => state?.attempts ?? 0;
        internal AttemptRecord InspectorActiveAttempt => state?.active;
        internal AttemptRecord InspectorLastAttempt => state?.recent.Count > 0 ? state.recent[state.recent.Count - 1] : null;
        internal float InspectorElapsedSeconds => Time.realtimeSinceStartup - startTime;
        internal float InspectorBossHealthRemaining => arena ? arena.StudyBossHealthRemaining : 1;
#endif

        internal void Activate()
        {
            Current = this;
            DontDestroyOnLoad(gameObject);
            gameObject.SetActive(true);
        }

        internal void EnterScene(StudyStage sceneStage, BossArena owner, StudySettings settings, StudyPanel scenePanel)
        {
            stage = sceneStage;
            arena = owner;
            panel = scenePanel;
            panel.Bind(this, settings.font);
            changingScene = false;
            if (arena) arena.AttachStudy(this);
            if (storage != null)
            {
                Show(Screen);
                return;
            }
            asset = settings;
            storage = new StudyStorage(Application.persistentDataPath);
            try
            {
                state = storage.Load();
                if (state.active != null)
                {
                    // The last durable checkpoint marks the end of a process that could not shut down cleanly.
                    Finish("interrupted", useCheckpoint: true);
                }
                else RestoreScreen();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                FailStorage(error);
            }
        }

        private void RestoreScreen()
        {
            if (state.withdrawalPending || state.withdrawn) Show(StudyScreen.Withdrawal);
            else if (state.declined) Show(StudyScreen.Declined);
            else if (state.debrief) Show(StudyScreen.Debrief);
            else if (state.surveyDue) Show(StudyScreen.Survey);
            else if (state.assignment != null) Show(StudyScreen.Ready);
            else if (state.participant != null) Show(StudyScreen.Waiting);
            else Show(stage == StudyStage.Start ? StudyScreen.Title : StudyScreen.Consent);
        }

        internal void OpenIntro()
        {
            if (Screen == StudyScreen.Title) Show(StudyScreen.Consent);
        }

        internal void Enroll(string age, string frequency)
        {
            if (Screen != StudyScreen.Consent || !Protocol.IsConfigured) return;
            state.protocol = JsonUtility.FromJson<StudyProtocol>(JsonUtility.ToJson(asset.protocol));
            state.participant = new ParticipantRecord
            {
                participant_id = Guid.NewGuid().ToString("D"), age_group = age, gaming_experience = frequency,
                consented = true, consented_at = Now(), consent_version = state.protocol.consentVersion.Trim(),
                settings_version = state.protocol.settingsVersion.Trim()
            };
            var token = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(token);
            state.token = Convert.ToBase64String(token).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            if (Persist()) Show(StudyScreen.Waiting);
        }

        internal void Decline()
        {
            if (Screen != StudyScreen.Consent) return;
            state.declined = true;
            if (Persist()) Show(StudyScreen.Declined);
        }

        internal void ReopenConsent()
        {
            if (Screen != StudyScreen.Declined) return;
            state.declined = false;
            if (Persist()) Show(StudyScreen.Consent);
        }

        internal void BeginAttempt()
        {
            if (Screen != StudyScreen.Ready || state.assignment == null || !arena || changingScene) return;
            string now = Now();
            int number = state.attempts + 1;
            var difficulty = AdaptiveDifficulty.Choose(Protocol, state.recent, number,
                state.assignment.target_attempts, now, out var change);
            state.active = new AttemptRecord
            {
                participant_id = state.participant.participant_id, attempt_id = Guid.NewGuid().ToString("D"),
                attempt_number = number, experiment_group = state.assignment.experiment_group,
                assignment_version = state.assignment.assignment_version, settings_version = state.participant.settings_version,
                target_attempts = state.assignment.target_attempts, started_at = now, ended_at = now,
                difficulty = difficulty, difficulty_changes = new[] { change },
                pattern_stats = Array.ConvertAll(difficulty.patterns, p => new PatternStat { pattern_id = p.pattern_id })
            };
            state.attempts = number;
            if (!Persist()) return;
            startTime = Time.realtimeSinceStartup;
            checkpointAt = startTime + 5;
            Show(StudyScreen.Combat);
            arena.ResetStudyFight();
        }

        internal PatternParameters Parameters(BossPattern pattern) =>
            state?.active?.difficulty.patterns[(int)pattern] ?? AdaptiveDifficulty.Neutral;

        internal BossPattern Pick(BossPattern[] patterns, BossPattern? previous)
        {
            float total = 0;
            foreach (var pattern in patterns)
                if (pattern != previous) total += Parameters(pattern).selection_weight;
            float remaining = UnityEngine.Random.value * total;
            BossPattern selected = patterns[0];
            foreach (var pattern in patterns)
            {
                if (pattern == previous) continue;
                selected = pattern;
                remaining -= Parameters(pattern).selection_weight;
                if (remaining <= 0) break;
            }
            return selected;
        }

        internal void Executed(BossPattern pattern)
        {
            if (state?.active != null) state.active.pattern_stats[(int)pattern].executions++;
        }

        internal void RecordDamage(FighterHealth health, float lost, BossPattern? pattern)
        {
            var attempt = state?.active;
            if (attempt == null) return;
            if (health == arena.Boss.Health) attempt.damage_dealt += lost;
            else if (health == arena.Player.Health)
            {
                attempt.damage_taken += lost;
                if (!pattern.HasValue) return;
                var stat = attempt.pattern_stats[(int)pattern.Value];
                stat.hit_count++;
                stat.damage_taken += lost;
                stat.average_damage = stat.damage_taken / stat.hit_count;
            }
        }

        internal void StopStudy()
        {
            if (InCombat)
            {
                Finish(arena.CombatEnded ? (arena.Player.Health.Dead ? "defeat" : "cleared") : "stopped");
                if (Screen != StudyScreen.Ready) return;
            }
            if (Screen != StudyScreen.Ready) return;
            state.surveyDue = true;
            if (Persist()) Show(StudyScreen.Survey);
        }

        private void Sample()
        {
            var attempt = state.active;
            attempt.duration_seconds = Time.realtimeSinceStartup - startTime;
            string now = Now();
            attempt.ended_at = string.CompareOrdinal(now, attempt.started_at) >= 0 ? now : attempt.started_at;
            attempt.boss_hp_remaining = arena.StudyBossHealthRemaining;
        }

        private void Finish(string result, bool useCheckpoint = false)
        {
            if (!useCheckpoint) Sample();
            var attempt = state.active;
            attempt.result = result;
            attempt.cleared = result == "cleared";
            if (attempt.cleared) attempt.boss_hp_remaining = 0;
            state.active = null;
            state.cleared = attempt.cleared;
            state.surveyDue = attempt.cleared || result == "stopped";
            if (result is "defeat" or "cleared")
            {
                state.recent.Add(attempt);
                if (state.recent.Count > 4) state.recent.RemoveAt(0);
            }
            Enqueue("attempts", attempt);
            if (Persist())
            {
                if (arena) arena.EndStudyFight();
                RestoreScreen();
            }
        }

        internal void SubmitRating(int? rating)
        {
            if (Screen != StudyScreen.Survey || rating is < 1 or > 5) return;
            Enqueue("surveys", new SurveyRecord
            {
                participant_id = state.participant.participant_id, settings_version = state.participant.settings_version,
                submitted_at = Now(), actual_attempts = state.attempts, cleared = state.cleared,
                skipped = !rating.HasValue, scores = rating.HasValue ? new[] { rating.Value } : Array.Empty<int>()
            });
            state.surveyDue = false;
            state.debrief = true;
            state.recent.Clear();
            if (Persist()) Show(StudyScreen.Debrief);
        }

        internal void Withdraw()
        {
            if (Screen != StudyScreen.Debrief && Screen != StudyScreen.Waiting) return;
            state.withdrawalParticipantId = ParticipantId;
            state.withdrawalApiUrl = Protocol.apiUrl;
            state.withdrawalPending = true;
            state.pending.Clear();
            state.recent.Clear();
            // An unacknowledged registration must be replayed with the same token before it can be withdrawn.
            if (state.assignment != null) ClearRegistration();
            if (!Persist()) return;
            Retry();
            Show(StudyScreen.Withdrawal);
        }

        private void ClearRegistration()
        {
            state.participant = null;
            state.assignment = null;
            state.protocol = null;
            state.attempts = 0;
            state.cleared = state.debrief = state.surveyDue = false;
        }

        internal void Retry()
        {
            failures = 0;
            permanentError = false;
            nextSend = 0;
            UpdateStatus("");
        }

        private void Enqueue(string route, object record) =>
            state.pending.Add(new PendingSubmission { route = route, json = JsonUtility.ToJson(record) });

        private bool Persist()
        {
            try
            {
                storage.Write(state);
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                FailStorage(error);
                return false;
            }
        }

        private void FailStorage(Exception error)
        {
            fatal = true;
            Debug.LogError("Study storage unavailable (" + error.GetType().Name + ").");
            if (arena) arena.EndStudyFight();
            Show(StudyScreen.Error);
        }

        private void Update()
        {
            if (fatal || state == null) return;
            if (InCombat && arena.CombatEnded)
                Finish(arena.Player.Health.Dead ? "defeat" : "cleared");
            if (InCombat && Time.realtimeSinceStartup >= checkpointAt)
            {
                Sample();
                if (!Persist()) return;
                checkpointAt = Time.realtimeSinceStartup + 5;
            }
            if (!fatal && !sending && !permanentError && Time.realtimeSinceStartup >= nextSend &&
                (state.withdrawalPending || state.participant != null && (state.assignment == null || state.pending.Count > 0)))
                StartCoroutine(SendNext());
        }

        private IEnumerator SendNext()
        {
            sending = true;
            bool registering = state.participant != null && state.assignment == null;
            bool withdrawing = state.withdrawalPending && !registering;
            PendingSubmission pending = withdrawing || registering ? null : state.pending[0];
            string route = registering ? "participants" : withdrawing ? "withdraw" : pending.route;
            string json = registering ? JsonUtility.ToJson(state.participant) : withdrawing
                ? JsonUtility.ToJson(new WithdrawalRecord { participant_id = state.withdrawalParticipantId }) : pending.json;
            string endpoint = withdrawing ? state.withdrawalApiUrl : Protocol.apiUrl;
            using var request = new UnityWebRequest(endpoint.TrimEnd('/') + "/" + route, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + state.token);
            request.timeout = 10;
            request.redirectLimit = 0;
            // Unity may throw synchronously if the project disallows HTTP.
            UnityWebRequestAsyncOperation operation = null;
            try { operation = request.SendWebRequest(); }
            catch (InvalidOperationException) { RejectRequest(0, true); }
            if (operation != null)
            {
                yield return operation;
                if (!fatal && (request.result == UnityWebRequest.Result.Success
                    || registering && state.withdrawalPending && request.responseCode == 410))
                {
                    if (registering && !state.withdrawalPending)
                    {
                        StudyAssignment assigned = null;
                        try { assigned = JsonUtility.FromJson<StudyAssignment>(request.downloadHandler.text); }
                        catch (ArgumentException) { }
                        if (assigned == null || assigned.target_attempts <= 0 || string.IsNullOrWhiteSpace(assigned.experiment_group)
                            || string.IsNullOrWhiteSpace(assigned.assignment_version))
                        {
                            RejectRequest(request.responseCode, true);
                            sending = false;
                            yield break;
                        }
                        state.assignment = assigned;
                    }
                    else if (registering) ClearRegistration();
                    else if (withdrawing)
                    {
                        state.withdrawalPending = false;
                        state.withdrawn = true;
                        state.token = state.withdrawalParticipantId = state.withdrawalApiUrl = null;
                    }
                    else if (!state.withdrawalPending) state.pending.Remove(pending);

                    if (Persist())
                    {
                        Retry();
                        if (registering || withdrawing) RestoreScreen();
                    }
                }
                else if (!fatal && !(state.withdrawalPending && pending != null))
                {
                    long status = request.responseCode;
                    RejectRequest(status, status >= 400 && status < 500 && status != 408 && status != 429);
                }
            }
            sending = false;
        }

        private void RejectRequest(long code, bool permanent)
        {
            permanentError = permanent;
            failures++;
            nextSend = Time.realtimeSinceStartup + Mathf.Min(60, Mathf.Pow(2, Mathf.Min(failures, 6)));
            UpdateStatus(permanent
                ? $"전송에 실패했습니다 ({code}). 다시 시도하거나 담당자에게 문의하세요."
                : "서버에 연결할 수 없습니다. 저장한 기록은 연결되면 다시 보냅니다.");
        }

        private void UpdateStatus(string message)
        {
            syncStatus = message;
            if (panel) panel.RefreshStatus();
        }

        private void Show(StudyScreen screen)
        {
            Screen = screen;
            if (quitting || changingScene) return;
            var destination = screen switch
            {
                StudyScreen.Ready or StudyScreen.Combat => StudyStage.Game,
                StudyScreen.Survey or StudyScreen.Debrief or StudyScreen.Withdrawal => StudyStage.Review,
                StudyScreen.Consent or StudyScreen.Waiting or StudyScreen.Declined => StudyStage.Intro,
                StudyScreen.Error => stage,
                _ => StudyStage.Start
            };
            if (destination == stage)
            {
                panel.Render(screen);
                return;
            }

            string path = StudySceneEntry.ScenePath(destination);
            string sceneName = Path.GetFileNameWithoutExtension(path);
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError("Study scene is missing from Build Settings: " + path);
                fatal = true;
                Screen = StudyScreen.Error;
                panel.Render(Screen);
                return;
            }
            changingScene = true;
            SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && InCombat && !fatal)
            {
                Sample();
                Persist();
            }
        }

        private void OnApplicationQuit()
        {
            quitting = true;
            if (InCombat && !fatal)
            {
                string result = arena.CombatEnded ? (arena.Player.Health.Dead ? "defeat" : "cleared") : "interrupted";
                Finish(result);
            }
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }
    }
}
