using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BossTemplate;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class BossStudyChecks
{
    private const string SmokeKey = "BossStudyChecks.Run";
    private const string ReportPath = "Temp/BossStudySmoke.txt";
    private const string Endpoint = "http://127.0.0.1:18741";
    private const string StudyScenePath = "Assets/Scenes/BossStudy.unity";
    private const string SettingsPath = "Assets/Settings/BossStudySettings.asset";

    static BossStudyChecks() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

    [MenuItem("Tools/Boss Arena/Research/Run Study Smoke Test")]
    private static void RunSmoke()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode
            || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != StudyScenePath)
            throw new InvalidOperationException("Open BossStudy in Edit Mode before running the study smoke test.");
        if (File.Exists(Path.Combine(Application.persistentDataPath, "boss-study-v1.json")))
            throw new InvalidOperationException("Use a test editor profile without existing study data.");
        File.WriteAllText(ReportPath, "RUNNING");
        SessionState.SetBool(SmokeKey, true);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(SmokeKey, false)) return;
        SessionState.EraseBool(SmokeKey);
        var arena = UnityEngine.Object.FindAnyObjectByType<BossArena>();
        if (!arena) { Report("FAIL: BossArena missing"); return; }
        arena.StartCoroutine(Guard(Smoke(arena)));
    }

    private static IEnumerator Guard(IEnumerator test)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(test);
        while (stack.Count > 0)
        {
            object current;
            try
            {
                if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                current = stack.Peek().Current;
            }
            catch (Exception error)
            {
                Report("FAIL: " + error);
                yield break;
            }
            if (current is IEnumerator nested) stack.Push(nested);
            else yield return current;
        }
        Report("PASS: consent gate, assignment, UUID reload, real damage, early victory, offline retry, survey, withdrawal");
    }

    private static void Report(string result)
    {
        File.WriteAllText(ReportPath, result);
        EditorApplication.ExitPlaymode();
    }

    private static IEnumerator Smoke(BossArena arena)
    {
        var study = arena.Study;
        Check(study && arena.Finished && study.Screen == StudyScreen.Consent, "Consent must gate combat");
        Check(UnityEngine.EventSystems.EventSystem.current, "Study UI input module missing");
        var settings = UnityEngine.Object.Instantiate(arena.ResearchSettings);
        settings.protocol.apiUrl = Endpoint;
        settings.protocol.contact = "Smoke test only";
        settings.protocol.retention = "Smoke test only";
        var storage = new StudyStorage(Path.GetFullPath("Temp/BossStudySmoke-" + Guid.NewGuid().ToString("N")));
        ReplacePrivate(study, "storage", storage);
        ReplacePrivate(study, "asset", settings);

        study.Enroll("undisclosed", "undisclosed");
        yield return Until(() => study.Screen == StudyScreen.Ready, "Server assignment");
        var saved = storage.Load();
        string participant = saved.participant.participant_id;
        Check(Guid.TryParse(participant, out _) && saved.assignment.target_attempts == 7, "UUID and fixed test assignment");
        Check(study.ParticipantId == participant, "UUID persisted and reloaded");
        study.BeginAttempt();
        Check(!arena.Finished && study.InCombat, "Start after consent and assignment");

        // Set the isolated test profile offline before the attempt closes, then restore it after a failure.
        study.Protocol.apiUrl = "http://127.0.0.1:1";
        arena.Player.Health.Hit(6, 0, true, BossPattern.dash);
        arena.Boss.Health.Hit(arena.Boss.Health.Current);
        yield return Until(() => arena.Boss.Phase == 2 && !arena.Boss.Transitioning, "Natural phase transition");
        arena.Boss.HitFist(0, arena.Boss.Health.Current);
        yield return Until(() => study.Screen == StudyScreen.Survey, "Clear at attempt 1 despite target 7");
        saved = storage.Load();
        Check(saved.pending.Count == 1 && saved.recent[0].cleared && saved.recent[0].attempt_number == 1, "Durable attempt record");
        Check(saved.recent[0].pattern_stats[(int)BossPattern.dash].average_damage == 6, "Measured pattern damage");
        Check(saved.recent[0].damage_dealt > 0 && saved.recent[0].boss_hp_remaining == 0, "Both boss phases recorded");
        yield return Until(() => study.SyncStatus.Length > 0, "Offline attempt transmission");
        study.Protocol.apiUrl = Endpoint;
        yield return Until(() => !study.HasPending, "Automatic retry after connectivity returns");
        study.SubmitSurvey(new[] { 5, 4, 3, 4, 5, 2 }, false);
        yield return Until(() => study.Screen == StudyScreen.Debrief && !study.HasPending, "Persist survey on real FastAPI");
        study.Withdraw();
        yield return Until(() => study.Screen == StudyScreen.Withdrawal && !study.WithdrawalPending, "Confirmed withdrawal");
        saved = storage.Load();
        Check(saved.withdrawn && saved.participant == null && string.IsNullOrEmpty(saved.token), "Local study data removed");
    }

    private static void ReplacePrivate(StudySession session, string name, object value) =>
        typeof(StudySession).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(session, value);

    private static IEnumerator Until(Func<bool> condition, string description)
    {
        float limit = Time.realtimeSinceStartup + 15;
        while (!condition())
        {
            Check(Time.realtimeSinceStartup < limit, "Timeout: " + description);
            yield return null;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [MenuItem("Tools/Boss Arena/Research/Validate DDA and Simulate")]
    private static void ValidateDda()
    {
        var protocol = AssetDatabase.LoadAssetAtPath<StudySettings>(SettingsPath).protocol;
        var profiles = new StudyProtocol();
        var history = new List<AttemptRecord>();
        const string now = "2026-01-01T00:00:00Z";
        var initial = AdaptiveDifficulty.Choose(profiles, history, 1, 7, now, out _);
        history.Add(Defeat(initial, .05f, 140, 25));
        var far = AdaptiveDifficulty.Choose(profiles, history, 2, 7, now, out var increase);
        var near = AdaptiveDifficulty.Choose(profiles, history, 2, 3, now, out var decrease);
        Check(far.level > initial.level && increase.reason == "ahead_of_target", "DDA should raise pressure on a close early loss");
        Check(near.level < initial.level && decrease.reason == "near_target", "DDA should ease near the target");
        Check(Mathf.Abs(far.level - initial.level) <= profiles.maximumStep + 1e-6f, "DDA step limit");
        history[0] = Defeat(initial, .95f, 6, 100);
        var easier = AdaptiveDifficulty.Choose(profiles, history, 2, 12, now, out _);
        Check(easier.level < initial.level, "DDA should ease for a struggling player far from target");
        var report = new List<string> { "PASS: four DDA checks. Synthetic play model, seed 315." };
        var rng = new System.Random(315);
        foreach (int target in new[] { 3, 7, 12 })
        foreach (float skill in new[] { .25f, .5f, .75f })
        {
            const int population = 300;
            int clears = 0, withinOne = 0, absoluteError = 0;
            for (int player = 0; player < population; player++)
            {
                history.Clear();
                for (int attempt = 1; attempt <= 24; attempt++)
                {
                    var profile = AdaptiveDifficulty.Choose(protocol, history, attempt, target, now, out _);
                    float performance = skill + .018f * (attempt - 1) - .55f * profile.level
                        + .65f * ((float)rng.NextDouble() - .5f);
                    if (performance >= .50f)
                    {
                        clears++;
                        absoluteError += Math.Abs(attempt - target);
                        if (Math.Abs(attempt - target) <= 1) withinOne++;
                        break;
                    }
                    float progress = Mathf.Clamp01((performance + .25f) / .75f);
                    if (attempt > 2 && rng.NextDouble() < .09 * (1 - progress)) break;
                    history.Add(Defeat(profile, 1 - progress, 10 + 125 * progress, 100 - 35 * progress));
                    if (history.Count > 4) history.RemoveAt(0);
                }
            }
            string mae = clears == 0 ? "n/a" : ((double)absoluteError / clears).ToString("F2");
            string within = clears == 0 ? "n/a" : ((double)withinOne / clears).ToString("P1");
            report.Add($"target={target,2}, skill={skill:F2}, MAE={mae}, within ±1={within}, dropout={1 - (double)clears / population:P1}");
        }
        File.WriteAllLines("Temp/BossStudySimulation.txt", report);
        Debug.Log(string.Join("\n", report));
    }

    private static AttemptRecord Defeat(DifficultySnapshot profile, float remaining, float duration, float damage) =>
        new AttemptRecord
        {
            difficulty = profile, result = "defeat", boss_hp_remaining = remaining, duration_seconds = duration, damage_taken = damage,
            pattern_stats = Array.ConvertAll(profile.patterns, pattern => new PatternStat
            {
                pattern_id = pattern.pattern_id, executions = 3, hit_count = 1, average_damage = damage / profile.patterns.Length
            })
        };
}
