using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace BossTemplate
{
    internal enum StudyScreen { Consent, Waiting, Ready, Combat, Survey, Debrief, Declined, Withdrawal, Error, Title, Defeat }

    internal sealed class StudySession : MonoBehaviour
    {
        private sealed class ArchiveUpload
        {
            internal readonly StudySave save;
            internal float nextSend;
            internal int failures;

            internal ArchiveUpload(StudySave save) => this.save = save;
        }

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
        private readonly List<ArchiveUpload> archiveUploads = new List<ArchiveUpload>();
        private StudyPanel panel;
        private StudyLoadingScreen loadingScreen;
        private AsyncOperation sceneLoad;
        private float startTime, checkpointAt, nextSend;
        private float pausedAt, previousTimeScale;
        private bool previousAudioPause;
        private bool victoryFrozen;
        private int resumedFrame = -1;
        private int failures;
        private bool sending, fatal, permanentError, changingScene, quitting;
        private StudyStage stage;
        private string syncStatus = "";
        internal StudyScreen Screen { get; private set; }
        internal bool InCombat => Screen == StudyScreen.Combat;
        internal bool Paused { get; private set; }
        internal bool Frozen => Paused || victoryFrozen;
        internal bool BlocksGameplayInput => Frozen || resumedFrame == Time.frameCount;
        internal int Deaths => state.deaths;
        internal int Attempts => state.attempts;
        internal AttemptRecord LastAttempt => state.lastAttempt;
        internal bool HasContinuation => state.participant != null && !state.debrief;
        internal bool CanSwitchToOffline => Screen == StudyScreen.Waiting && !changingScene
            && state.participant != null && state.assignment == null && state.attempts == 0
            && !state.withdrawalPending && asset.protocol.HasConsentDetails;
        private bool CanPause => !fatal && !changingScene && (Screen is StudyScreen.Ready or StudyScreen.Defeat
            || InCombat && !arena.CombatEnded);
        private float ElapsedSeconds => (Paused ? pausedAt : (arena ? arena.Boss.DefeatedAt : null)
            ?? Time.realtimeSinceStartup) - startTime;
        internal bool Cleared => state.cleared;
        internal bool WithdrawalPending => state.withdrawalPending;
        // A save written before enrollment can deserialize with an empty protocol object.
        internal StudyProtocol Protocol => (state?.participant != null ? state.protocol : null) ?? asset.protocol;
        internal bool HasPending => state?.pending.Count > 0;
        internal bool CanReplay => (Screen is StudyScreen.Debrief or StudyScreen.Title) && state.debrief && !changingScene;
        internal string SyncStatus => syncStatus;
        internal string ParticipantId => state.participant?.participant_id ?? state.withdrawalParticipantId ?? "";
        internal float BossHealthFactor => state?.active?.difficulty.boss_health ?? 1;
        internal float SupportRainChance => AdaptiveDifficulty.SupportRainChance(state?.active?.difficulty.level ?? 0);
        internal static string Now() => DateTime.UtcNow.ToString("O");

#if UNITY_EDITOR
        internal StudySave InspectorState => state;
        internal float InspectorElapsedSeconds => ElapsedSeconds;
        internal float InspectorBossHealthRemaining => arena ? arena.StudyBossHealthRemaining : 1;
        internal int InspectorBossPhase => arena && !arena.Boss.Transitioning ? arena.Boss.Phase : 0;
        internal BossPattern? InspectorPreviousPattern { get; private set; }
#endif

        internal void Activate()
        {
            Current = this;
            StudyAudioVolume.LoadMaster();
            DontDestroyOnLoad(gameObject);
            gameObject.SetActive(true);
        }

        internal void EnterScene(StudyStage sceneStage, BossArena owner, StudySettings settings, StudyPanel scenePanel)
        {
            if (sceneStage != StudyStage.Game) ReleaseVictory();
            stage = sceneStage;
            arena = owner;
            panel = scenePanel;
            panel.Bind(this, settings.font);
            loadingScreen?.Hide();
            sceneLoad = null;
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
                foreach (var archive in storage.LoadPendingArchives(state.participant?.participant_id))
                    archiveUploads.Add(new ArchiveUpload(archive));
                if (state.active != null)
                {
                    // The last durable checkpoint marks the end of a process that could not shut down cleanly.
                    Finish("interrupted", useCheckpoint: true, nextScreen: StudyScreen.Title);
                }
                else if (state.withdrawalPending || state.withdrawn) RestoreScreen();
                else Show(StudyScreen.Title);
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
            else if (state.surveyDue && state.cleared) Show(StudyScreen.Survey);
            else if (state.surveyDue)
            {
                CloseStudy(null);
                if (Persist()) Show(StudyScreen.Debrief);
            }
            else if (state.assignment != null) Show(StudyScreen.Ready);
            else if (state.participant != null) Show(StudyScreen.Waiting);
            else Show(StudyScreen.Title);
        }

        internal void OpenIntro()
        {
            if (Screen != StudyScreen.Title || changingScene) return;
            if (CanReplay) StartNewStudy();
            else if (state.participant != null) RestoreScreen();
            else Show(StudyScreen.Consent);
        }

        internal void BackToTitle()
        {
            if (!changingScene && (Screen is StudyScreen.Consent or StudyScreen.Waiting or StudyScreen.Declined))
                Show(StudyScreen.Title);
        }

        internal void Enroll() => Enroll(offline: false);
        internal void EnrollOffline()
        {
            if (Screen == StudyScreen.Waiting)
            {
                if (!CanSwitchToOffline) return;
                // A lost response can hide an existing online assignment. Retire that ID independently.
                var previous = new StudySave
                {
                    participant = state.participant, protocol = state.protocol, token = state.token,
                    withdrawalPending = true,
                    pending = new List<PendingSubmission>
                    {
                        new PendingSubmission { route = "participants", json = JsonUtility.ToJson(state.participant) },
                        new PendingSubmission { route = "withdraw", json = JsonUtility.ToJson(new WithdrawalRecord
                            { participant_id = state.participant.participant_id }) },
                    },
                };
                try { storage.WriteArchive(previous); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    FailStorage(error);
                    return;
                }
                state = new StudySave();
                archiveUploads.Add(new ArchiveUpload(previous));
                Retry();
            }
            Enroll(offline: true);
        }

        private void Enroll(bool offline)
        {
            bool fromConsent = Screen == StudyScreen.Consent || (offline && Screen == StudyScreen.Waiting && state.participant == null);
            if (!fromConsent || changingScene || !Protocol.HasConsentDetails
                || (!offline && !Protocol.HasValidEndpoint)) return;
            state.declined = false;
            state.protocol = JsonUtility.FromJson<StudyProtocol>(JsonUtility.ToJson(asset.protocol));
            Guid participantId = Guid.NewGuid();
            state.participant = new ParticipantRecord
            {
                participant_id = participantId.ToString("D"), age_group = "undisclosed", gaming_experience = "undisclosed",
                consented = true, consented_at = Now(), consent_version = state.protocol.consentVersion.Trim(),
                settings_version = state.protocol.settingsVersion.Trim()
            };
            var token = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(token);
            state.token = Convert.ToBase64String(token).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            if (offline)
            {
                state.assignment = StudyAssignment.ForOfflineParticipant(participantId);
                state.offlineRegistrationPending = true;
                if (!state.protocol.HasValidEndpoint) state.protocol.apiUrl = null;
            }
            if (Persist()) Show(offline ? StudyScreen.Ready : StudyScreen.Waiting);
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
            if (Screen is not (StudyScreen.Ready or StudyScreen.Defeat)
                || state.assignment == null || !arena || changingScene || Paused) return;
            if (Screen == StudyScreen.Defeat) resumedFrame = Time.frameCount;
            StartAttempt();
        }

        private void StartAttempt()
        {
            string now = Now();
            int number = state.attempts + 1;
            var difficulty = AdaptiveDifficulty.Choose(Protocol, state.recent, number,
                state.assignment.target_attempts, now, out var change, state.assignment.FixedHard);
            state.active = new AttemptRecord
            {
                participant_id = state.participant.participant_id, attempt_id = Guid.NewGuid().ToString("D"),
                attempt_number = number, experiment_group = state.assignment.experiment_group,
                assignment_version = state.assignment.assignment_version, settings_version = state.participant.settings_version,
                target_attempts = state.assignment.target_attempts, started_at = now, ended_at = now, boss_phase = 1,
                difficulty = difficulty, difficulty_changes = new[] { change },
                pattern_stats = Array.ConvertAll(difficulty.patterns, p => new PatternStat { pattern_id = p.pattern_id })
            };
            state.attempts = number;
            if (!Persist()) return;
#if UNITY_EDITOR
            InspectorPreviousPattern = null;
#endif
            startTime = Time.realtimeSinceStartup;
            checkpointAt = startTime + 5;
            Show(StudyScreen.Combat);
            arena.ResetStudyFight();
        }

        internal PatternParameters Parameters(BossPattern pattern) =>
            state?.active?.difficulty.ParametersFor(pattern) ?? AdaptiveDifficulty.Neutral;

        internal BossPattern Pick(BossPattern[] patterns, BossPattern? previous)
        {
            var difficulty = state?.active?.difficulty;
            float total = DifficultySnapshot.SelectionWeightTotal(difficulty, patterns, previous);
            float remaining = UnityEngine.Random.value * total;
            BossPattern selected = patterns[0];
            foreach (var pattern in patterns)
            {
                if (pattern == previous) continue;
                float weight = DifficultySnapshot.SelectionWeightFor(difficulty, pattern);
                if (weight <= 0) continue;
                selected = pattern;
                remaining -= weight;
                if (remaining <= 0) break;
            }
#if UNITY_EDITOR
            InspectorPreviousPattern = selected;
#endif
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

        internal void TogglePause()
        {
            if (Paused)
            {
                Resume();
                return;
            }
            if (!CanPause) return;
            pausedAt = Time.realtimeSinceStartup;
            previousTimeScale = Time.timeScale;
            previousAudioPause = AudioListener.pause;
            Paused = true;
            Time.timeScale = 0;
            AudioListener.pause = true;
            if (InCombat)
            {
                Sample();
                if (!Persist()) return;
            }
            panel.ShowPause(true);
        }

        private void Resume()
        {
            if (!Paused) return;
            Paused = false;
            resumedFrame = Time.frameCount;
            startTime += Time.realtimeSinceStartup - pausedAt;
            checkpointAt = Time.realtimeSinceStartup + 5;
            Time.timeScale = previousTimeScale;
            AudioListener.pause = previousAudioPause;
            if (panel) panel.ShowPause(false);
        }

        internal void ReturnToMenu()
        {
            if (!Paused) return;
            if (InCombat) Finish("interrupted", nextScreen: StudyScreen.Title);
            else Show(StudyScreen.Title);
        }

        private void Sample()
        {
            var attempt = state.active;
            attempt.duration_seconds = ElapsedSeconds;
            string now = arena.Boss.DefeatedAt is float defeatedAt
                ? DateTime.UtcNow.AddSeconds(defeatedAt - Time.realtimeSinceStartup).ToString("O") : Now();
            attempt.ended_at = string.CompareOrdinal(now, attempt.started_at) >= 0 ? now : attempt.started_at;
            attempt.boss_hp_remaining = arena.StudyBossHealthRemaining;
            attempt.boss_phase = arena.Boss.Phase;
            var health = arena.Boss.Health;
            attempt.phase_progress_percent = 100 * Mathf.Clamp01(1 - health.Current / health.Maximum);
        }

        private void Finish(string result, bool useCheckpoint = false, StudyScreen? nextScreen = null)
        {
            if (!useCheckpoint) Sample();
            var attempt = state.active;
            attempt.result = result;
            attempt.cleared = result == "cleared";
            if (attempt.cleared)
            {
                attempt.boss_hp_remaining = 0;
                attempt.boss_phase = 2;
                attempt.phase_progress_percent = 100;
            }
            state.active = null;
            state.lastAttempt = attempt;
            state.cleared = attempt.cleared;
            state.surveyDue = attempt.cleared;
            if (result == "defeat") state.deaths++;
            if (result is "defeat" or "cleared")
            {
                state.recent.Add(attempt);
                if (state.recent.Count > 4) state.recent.RemoveAt(0);
            }
            Enqueue("attempts", attempt);
            if (Persist())
            {
                if (arena)
                {
                    if (attempt.cleared && !quitting && panel.HasVictoryPanel)
                    {
                        previousTimeScale = Time.timeScale;
                        victoryFrozen = true;
                        Time.timeScale = 0;
                        arena.Boss.StopAllCoroutines();
                    }
                    else arena.EndStudyFight();
                }
                if (nextScreen.HasValue) Show(nextScreen.Value);
                else if (result == "defeat" && !quitting) Show(StudyScreen.Defeat);
                else RestoreScreen();
            }
        }

        private void ReleaseVictory()
        {
            if (!victoryFrozen) return;
            victoryFrozen = false;
            Time.timeScale = previousTimeScale;
        }

        internal void SubmitRating(int? rating)
        {
            if (Screen != StudyScreen.Survey || !state.cleared || rating is < 1 or > 5) return;
            CloseStudy(rating);
            if (Persist()) Show(StudyScreen.Debrief);
        }

        private void CloseStudy(int? rating)
        {
            Enqueue("surveys", new SurveyRecord
            {
                participant_id = state.participant.participant_id, settings_version = state.participant.settings_version,
                submitted_at = Now(), actual_attempts = state.attempts, cleared = state.cleared,
                skipped = !rating.HasValue, scores = rating.HasValue ? new[] { rating.Value } : Array.Empty<int>()
            });
            state.surveyDue = false;
            state.debrief = true;
            state.recent.Clear();
        }

        internal void Withdraw()
        {
            if (Screen != StudyScreen.Debrief && Screen != StudyScreen.Waiting) return;
            state.withdrawalParticipantId = ParticipantId;
            state.withdrawalApiUrl = Protocol.apiUrl;
            state.withdrawalPending = true;
            state.pending.Clear();
            state.recent.Clear();
            state.lastAttempt = null;
            // An unacknowledged registration must be replayed with the same token before it can be withdrawn.
            if (state.assignment != null && !state.offlineRegistrationPending) ClearRegistration();
            if (!Persist()) return;
            Retry();
            Show(StudyScreen.Withdrawal);
        }

        internal void StartNewStudy()
        {
            if (!CanReplay) return;
            var completed = state;
            try
            {
                storage.WriteArchive(completed);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                FailStorage(error);
                return;
            }
            state = new StudySave();
            if (!Persist()) return;
            if (completed.pending.Count > 0) archiveUploads.Add(new ArchiveUpload(completed));
            Retry();
            Show(StudyScreen.Consent);
        }

        private void ClearRegistration()
        {
            state.participant = null;
            state.assignment = null;
            state.offlineRegistrationPending = false;
            state.protocol = null;
            state.attempts = 0;
            state.deaths = 0;
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
            if (InCombat && !Paused && arena.ResultsReady)
                Finish(arena.Player.Health.Dead ? "defeat" : "cleared");
            if (InCombat && !Paused && Time.realtimeSinceStartup >= checkpointAt)
            {
                Sample();
                if (!Persist()) return;
                checkpointAt = Time.realtimeSinceStartup + 5;
            }
            if (fatal || sending) return;
            if (!permanentError && Time.realtimeSinceStartup >= nextSend &&
                (state.withdrawalPending || state.participant != null && (NeedsRegistration(state) || state.pending.Count > 0))
                && CanSync(state))
            {
                StartCoroutine(SendNext());
                return;
            }
            float now = Time.realtimeSinceStartup;
            foreach (var archive in archiveUploads)
            {
                if (now < archive.nextSend || !CanSync(archive.save)
                    || (archive.save.withdrawalPending && !archive.save.protocol.HasValidEndpoint)) continue;
                StartCoroutine(SendArchived(archive));
                break;
            }
        }

        private static bool NeedsRegistration(StudySave save) => save.participant != null
            && (save.assignment == null || save.offlineRegistrationPending);

        private bool CanSync(StudySave save) => !save.offlineRegistrationPending || save.protocol.HasValidEndpoint
            || (string.IsNullOrEmpty(save.protocol.apiUrl) && asset.protocol.HasValidEndpoint);

        private bool SetOfflineEndpoint(StudySave save)
        {
            if (!save.offlineRegistrationPending || !string.IsNullOrEmpty(save.protocol.apiUrl)) return false;
            // Lock the first usable endpoint before sending so retries cannot register on another server.
            save.protocol.apiUrl = asset.protocol.apiUrl;
            if (save.withdrawalPending) save.withdrawalApiUrl = save.protocol.apiUrl;
            return true;
        }

        private IEnumerator SendNext()
        {
            if (SetOfflineEndpoint(state) && !Persist()) yield break;
            sending = true;
            var submittedState = state;
            bool offline = state.offlineRegistrationPending;
            bool registering = NeedsRegistration(state);
            bool withdrawing = state.withdrawalPending && !registering;
            PendingSubmission pending = withdrawing || registering ? null : state.pending[0];
            string route = registering ? (offline ? "participants/offline" : "participants")
                : withdrawing ? "withdraw" : pending.route;
            string json = registering ? JsonUtility.ToJson(state.participant) : withdrawing
                ? JsonUtility.ToJson(new WithdrawalRecord { participant_id = state.withdrawalParticipantId }) : pending.json;
            string endpoint = withdrawing ? state.withdrawalApiUrl : Protocol.apiUrl;
            using var request = CreateRequest(endpoint, route, json, state.token);
            // Unity may throw synchronously if the project disallows HTTP.
            UnityWebRequestAsyncOperation operation = null;
            try { operation = request.SendWebRequest(); }
            catch (InvalidOperationException) { RejectRequest(0, true); }
            if (operation != null)
            {
                yield return operation;
                // Replay can replace the current save while this request is in flight.
                // The archive may resend it safely using the original participant and attempt IDs.
                if (state != submittedState)
                {
                    sending = false;
                    yield break;
                }
                if (!fatal && (request.result == UnityWebRequest.Result.Success
                    || registering && state.withdrawalPending && request.responseCode == 410))
                {
                    if (registering && !state.withdrawalPending)
                    {
                        StudyAssignment assigned = ReadAssignment(request);
                        if (assigned == null || (offline && !state.assignment.Matches(assigned)))
                        {
                            RejectRequest(request.responseCode, true);
                            sending = false;
                            yield break;
                        }
                        state.assignment = assigned;
                        state.offlineRegistrationPending = false;
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
                        if (withdrawing || registering && !offline && Screen != StudyScreen.Title) RestoreScreen();
                    }
                }
                else if (!fatal && !(state.withdrawalPending && pending != null))
                {
                    long status = request.responseCode;
                    RejectRequest(status, IsPermanentFailure(status) && !(offline && status == 404));
                }
            }
            sending = false;
        }

        private static StudyAssignment ReadAssignment(UnityWebRequest request)
        {
            try
            {
                var assignment = JsonUtility.FromJson<StudyAssignment>(request.downloadHandler.text);
                if (assignment != null && assignment.target_attempts > 0
                    && !string.IsNullOrWhiteSpace(assignment.experiment_group)
                    && !string.IsNullOrWhiteSpace(assignment.assignment_version)) return assignment;
            }
            catch (ArgumentException) { }
            return null;
        }

        private static bool IsPermanentFailure(long status) => status >= 400 && status < 500 && status != 408 && status != 429;

        private IEnumerator SendArchived(ArchiveUpload archive)
        {
            sending = true;
            var save = archive.save;
            if (SetOfflineEndpoint(save) && !PersistArchive(save))
            {
                save.protocol.apiUrl = null;
                archive.nextSend = Time.realtimeSinceStartup + 5;
                sending = false;
                yield break;
            }
            bool registering = save.offlineRegistrationPending;
            var pending = registering ? null : save.pending[0];
            using var request = CreateRequest(save.protocol.apiUrl, registering ? "participants/offline" : pending.route,
                registering ? JsonUtility.ToJson(save.participant) : pending.json, save.token);
            UnityWebRequestAsyncOperation operation = null;
            try { operation = request.SendWebRequest(); }
            catch (InvalidOperationException) { }
            if (operation != null) yield return operation;

            bool cancelledRegistration = save.withdrawalPending && pending?.route == "participants" && request.responseCode == 410;
            if (operation != null && (request.result == UnityWebRequest.Result.Success || cancelledRegistration)
                && (!registering || save.assignment.Matches(ReadAssignment(request))))
            {
                if (registering) save.offlineRegistrationPending = false;
                else save.pending.RemoveAt(0);
                if (PersistArchive(save))
                {
                    if (save.pending.Count == 0) archiveUploads.Remove(archive);
                    archive.failures = 0;
                    archive.nextSend = 0;
                    sending = false;
                    yield break;
                }
                if (registering) save.offlineRegistrationPending = true;
                else save.pending.Insert(0, pending);
            }
            archive.failures++;
            long status = request.responseCode;
            bool rejected = IsPermanentFailure(status) && !(registering && status == 404);
            archive.nextSend = Time.realtimeSinceStartup + (rejected ? 300 : Mathf.Min(60, Mathf.Pow(2, Mathf.Min(archive.failures, 6))));
            sending = false;
        }

        private bool PersistArchive(StudySave save)
        {
            try
            {
                if (save.withdrawalPending && save.pending.Count == 0) storage.RemoveArchive(save);
                else storage.WriteArchive(save);
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Debug.LogWarning("Could not update an archived study (" + error.GetType().Name + ").");
                return false;
            }
        }

        private static UnityWebRequest CreateRequest(string endpoint, string route, string json, string token)
        {
            var request = new UnityWebRequest(endpoint.TrimEnd('/') + "/" + route, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + token);
            request.timeout = 10;
            request.redirectLimit = 0;
            return request;
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
            Resume();
            Screen = screen;
            if (quitting || changingScene) return;
            var destination = screen switch
            {
                StudyScreen.Ready or StudyScreen.Combat or StudyScreen.Defeat => StudyStage.Game,
                StudyScreen.Survey when victoryFrozen => StudyStage.Game,
                StudyScreen.Survey or StudyScreen.Debrief or StudyScreen.Withdrawal => StudyStage.Review,
                StudyScreen.Consent or StudyScreen.Waiting or StudyScreen.Declined => StudyStage.Intro,
                StudyScreen.Error => stage,
                _ => StudyStage.Intro
            };
            if (destination == stage)
            {
                if (screen != StudyScreen.Survey) ReleaseVictory();
                // Ready waits for the game scene. The menu or consent action already started the run.
                if (screen == StudyScreen.Ready && arena && state.assignment != null) BeginAttempt();
                else panel.Render(screen);
                return;
            }

            LoadScene(StudySceneEntry.ScenePath(destination));
        }

        private void LoadScene(string path)
        {
            string sceneName = Path.GetFileNameWithoutExtension(path);
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                FailSceneLoad("Study scene is missing from Build Settings: " + path);
                return;
            }
            changingScene = true;
            loadingScreen ??= new StudyLoadingScreen(asset.font);
            loadingScreen.Show();
            StartCoroutine(LoadSceneWithProgress(sceneName));
        }

        private IEnumerator LoadSceneWithProgress(string sceneName)
        {
            // Give the current frame a chance to render the overlay before scene loading starts.
            yield return null;
            AsyncOperation operation = null;
            try { operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single); }
            catch (Exception error) when (error is ArgumentException or UnityException)
            {
                Debug.LogException(error);
            }
            if (operation == null)
            {
                FailSceneLoad("Could not start loading study scene: " + sceneName);
                yield break;
            }

            sceneLoad = operation;
            while (sceneLoad == operation && !operation.isDone)
            {
                loadingScreen.SetProgress(operation.progress);
                yield return null;
            }

            // EnterScene clears the overlay before the new screen or combat starts.
            yield return null;
            if (sceneLoad == operation)
                FailSceneLoad("Study scene did not initialize: " + sceneName);
        }

        private void FailSceneLoad(string message)
        {
            Debug.LogError(message);
            loadingScreen?.Hide();
            sceneLoad = null;
            changingScene = false;
            fatal = true;
            ReleaseVictory();
            Screen = StudyScreen.Error;
            if (panel) panel.Render(Screen);
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
            loadingScreen?.Destroy();
            Resume();
            ReleaseVictory();
            if (Current == this) Current = null;
        }
    }
}
