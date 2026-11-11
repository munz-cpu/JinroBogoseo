using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BossTemplate;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
internal static class BossStudyChecks
{
    private const string SmokeKey = "BossStudyChecks.Run";
    private const string ReportPath = "Temp/BossStudySmoke.txt";
    private const string Endpoint = "http://127.0.0.1:18741";
    private const string SettingsPath = "Assets/Settings/BossStudySettings.asset";

    static BossStudyChecks() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

    [MenuItem("Tools/Boss Arena/Research/Run Study Smoke Test")]
    private static void RunSmoke()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode
            || SceneManager.GetActiveScene().path != StudySceneEntry.IntroPath)
            throw new InvalidOperationException("Open BossStudyIntro in Edit Mode before running the study smoke test.");
        Check(UnityEngine.Object.FindAnyObjectByType<StudyMainMenu>(FindObjectsInactive.Include)
            && GameObject.Find("메뉴 전투 배경"), "The menu and background must already exist in Edit Mode");
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
        double deadline = EditorApplication.timeSinceStartup + 5;
        void StartWhenReady()
        {
            var study = UnityEngine.Object.FindAnyObjectByType<StudySession>();
            if (!study && EditorApplication.isPlaying && EditorApplication.timeSinceStartup < deadline) return;
            EditorApplication.update -= StartWhenReady;
            if (!study) { Report("FAIL: StudySession missing"); return; }
            study.StartCoroutine(Guard(Smoke(study)));
        }
        EditorApplication.update += StartWhenReady;
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
        Report("PASS: start → intro → game → review, consent, assignment, UUID reload, early victory, offline retry, rating, new study, withdrawal");
    }

    private static void Report(string result)
    {
        File.WriteAllText(ReportPath, result);
        EditorApplication.ExitPlaymode();
    }

    private static IEnumerator Smoke(StudySession study)
    {
        Check(SceneManager.GetActiveScene().path == StudySceneEntry.IntroPath && study.Screen == StudyScreen.Title,
            "The intro scene should open on the main menu");
        Check(!UnityEngine.Object.FindAnyObjectByType<BossArena>(), "Start scene should have no arena");
        var menu = GameObject.Find("메인 메뉴").transform;
        var items = menu.Find("메뉴 항목");
        var play = items.Find("게임 시작").GetComponent<Button>();
        Check(play.GetComponentInChildren<Text>().text == "게임 시작", "New participant sees Start");
        Canvas.ForceUpdateCanvases();
        var corners = new Vector3[4];
        var menuBounds = ((RectTransform)menu).rect;
        foreach (string item in new[] { "게임 시작", "설정", "통계", "실험 약관" })
        {
            ((RectTransform)items.Find(item)).GetWorldCorners(corners);
            Check(menuBounds.Contains(menu.InverseTransformPoint(corners[0]))
                && menuBounds.Contains(menu.InverseTransformPoint(corners[2])), $"Menu item fits on screen: {item}");
        }
        var scenery = GameObject.Find("메뉴 전투 배경");
        var studyPanel = UnityEngine.Object.FindAnyObjectByType<StudyPanel>();
        var studyBackdrop = (GameObject)new SerializedObject(studyPanel).FindProperty("studyBackdrop").objectReferenceValue;
        Check(!studyBackdrop.activeSelf, "The study pages' background must not cover the arena in the menu");
        var scenerySprites = scenery.GetComponentsInChildren<SpriteRenderer>();
        Check(scenerySprites.Length == 9 && Array.TrueForAll(scenerySprites, layer => layer.sprite && layer.sharedMaterial),
            "Menu reuses the arena background, ground and platform renderers");
        var sky = scenery.transform.Find("Sky");
        Check(sky.GetComponent<SpriteRenderer>().sharedMaterial == AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/PixelLostBackground.mat"),
            "The menu uses the same background material as the arena");
        var golem = (RectTransform)menu.Find("골렘");
        Check(golem.GetComponent<Image>().sprite, "Golem sprite is linked");
        float initialSkyY = sky.localPosition.y;
        float initialGolemY = golem.anchoredPosition.y;
        var mouse = InputSystem.AddDevice<Mouse>();
        try
        {
            mouse.MakeCurrent();
            InputState.Change(mouse, new MouseState { position = new Vector2(Screen.width * .95f, Screen.height * .85f) },
                InputUpdateType.Dynamic);
            yield return new WaitForSecondsRealtime(.2f);
            float worldPerUi = Camera.main.orthographicSize * 2 / menuBounds.height;
            Check(sky.localPosition.x < 0 && golem.anchoredPosition.x * worldPerUi < sky.localPosition.x * 4,
                "Rightward mouse motion moves the scenery left, with stronger foreground parallax");
            InputState.Change(mouse, new MouseState { position = new Vector2(Screen.width * .95f, Screen.height * .12f) },
                InputUpdateType.Dynamic);
            yield return new WaitForSecondsRealtime(.2f);
            Check(Mathf.Approximately(sky.localPosition.y, initialSkyY)
                && Mathf.Approximately(golem.anchoredPosition.y, initialGolemY),
                "Vertical mouse motion does not move the scenery or golem");
            InputState.Change(mouse, new MouseState { position = new Vector2(Screen.width * .05f, Screen.height * .12f) },
                InputUpdateType.Dynamic);
            yield return new WaitForSecondsRealtime(.2f);
            Check(sky.localPosition.x > 0 && golem.anchoredPosition.x * worldPerUi > sky.localPosition.x * 4,
                "Leftward mouse motion moves the scenery right, with stronger foreground parallax");
        }
        finally
        {
            InputSystem.RemoveDevice(mouse);
        }
        var details = menu.Find("메뉴 상세");
        var back = details.Find("돌아가기").GetComponent<Button>();
        items.Find("설정").GetComponent<Button>().onClick.Invoke();
        var menuVolume = details.GetComponentInChildren<Slider>();
        float previousMenuVolume = menuVolume.value;
        menuVolume.value = .4f;
        Check(Mathf.Approximately(AudioListener.volume, .4f), "Settings changes master volume");
        menuVolume.value = previousMenuVolume;
        back.onClick.Invoke();
        items.Find("통계").GetComponent<Button>().onClick.Invoke();
        Check(Array.Exists(details.GetComponentsInChildren<Text>(), t => t.text.Contains("도전 횟수   0회")),
            "Statistics shows the empty record");
        back.onClick.Invoke();
        items.Find("실험 약관").GetComponent<Button>().onClick.Invoke();
        Check(Array.Exists(details.GetComponentsInChildren<Text>(), t => t.text.Contains(study.Protocol.consentNotice)),
            "Terms shows the configured consent notice");
        back.onClick.Invoke();
        play.onClick.Invoke();
        yield return Until(() => SceneManager.GetActiveScene().path == StudySceneEntry.IntroPath
            && study.Screen == StudyScreen.Consent, "Start opens consent from the main menu");
        Check(!scenery.activeSelf && studyBackdrop.activeSelf, "The consent screen restores its study background");
        var panel = UnityEngine.Object.FindAnyObjectByType<StudyPanel>();
        var consentButton = Array.Find(panel.GetComponentsInChildren<UnityEngine.UI.Button>(),
            button => button.name == "연구 참여에 동의하고 시작");
        Check(consentButton && consentButton.interactable == study.Protocol.IsConfigured,
            "Consent button must be visible and follow the protocol configuration");
        Check(UnityEngine.EventSystems.EventSystem.current, "Study UI input module missing");
        var settings = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<StudySettings>(SettingsPath));
        settings.protocol.apiUrl = Endpoint;
        settings.protocol.contact = "Smoke test only";
        settings.protocol.retention = "Smoke test only";
        var storagePath = Path.Combine(Application.temporaryCachePath, "BossStudySmoke-" + Guid.NewGuid().ToString("N"));
        var storage = new StudyStorage(storagePath);
        ReplacePrivate(study, "storage", storage);
        ReplacePrivate(study, "asset", settings);

        study.Decline();
        var declined = storage.Load();
        Check(study.Screen == StudyScreen.Declined && declined.declined, "Decline must persist");
        ReplacePrivate(study, "state", declined);
        var reopenButton = Array.Find(panel.GetComponentsInChildren<UnityEngine.UI.Button>(),
            button => button.name == "참여 안내 다시 보기");
        Check(reopenButton && reopenButton.interactable, "Declined participants must be able to reopen consent");
        reopenButton.onClick.Invoke();
        var reopened = storage.Load();
        Check(study.Screen == StudyScreen.Consent && !reopened.declined && reopened.participant == null,
            "Reopening consent must clear the decline without enrolling");
        Check(ReferenceEquals(study.Protocol, settings.protocol), "Before enrollment, use the current Inspector protocol");
        Check(consentButton.interactable, "A configured protocol must enable enrollment immediately");
        consentButton.onClick.Invoke();
        yield return Until(() => study.Screen == StudyScreen.Ready && SceneManager.GetActiveScene().path == StudySceneEntry.GamePath
            && UnityEngine.Object.FindAnyObjectByType<BossArena>()?.Study == study, "Server assignment and game scene");
        var arena = UnityEngine.Object.FindAnyObjectByType<BossArena>();
        Check(arena.Finished && UnityEngine.Object.FindObjectsByType<StudySession>(FindObjectsSortMode.None).Length == 1,
            "The existing session must own the game scene");
        var saved = storage.Load();
        Check(saved.participant.age_group == "undisclosed" && saved.participant.gaming_experience == "undisclosed",
            "New enrollment must not collect age or gaming frequency");
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
        var hands = (SpriteRenderer[])typeof(ArenaBoss).GetField("battleFists", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(arena.Boss);
        var bossHealth = arena.Boss.Health;
        arena.Player.Health.InvulnerableUntil = Time.time + 12;
        bossHealth.Hit(bossHealth.Maximum * .69f);
        yield return null;
        Check(hands[0].enabled && hands[1].enabled && !hands[2].enabled && !hands[3].enabled,
            "Phase two has two hands above the threshold");
        bossHealth.Hit(bossHealth.Maximum * .02f);
        yield return Until(() => hands[2].enabled && hands[3].enabled, "Two more hands spawn below 30 percent");
        yield return Until(() => arena.Boss.Intent == "네 손 교차 내려찍기", "Four-hand slam attacks");
        yield return Until(() => arena.Boss.Intent == "네 손 협공", "Four-hand pincer attacks");
        arena.SkipCurrentPhase();
        yield return Until(() => study.Screen == StudyScreen.Survey && SceneManager.GetActiveScene().path == StudySceneEntry.GamePath,
            "Clearing the boss opens the rating panel in the game scene");
        Check(arena && arena.Boss.Health.Dead && study.Frozen && Time.timeScale == 0,
            "Rating preserves the defeated boss and freezes gameplay");
        study.TogglePause();
        Check(study.Frozen && !study.Paused, "Escape cannot replace the victory panel with the pause menu");
        saved = storage.Load();
        Check(saved.pending.Count == 1 && saved.recent[0].cleared && saved.recent[0].attempt_number == 1, "Durable attempt record");
        Check(saved.recent[0].pattern_stats[(int)BossPattern.dash].average_damage == 6, "Measured pattern damage");
        Check(saved.recent[0].damage_dealt > 0 && saved.recent[0].boss_hp_remaining == 0, "Both boss phases recorded");
        Check(saved.recent[0].boss_phase == 2 && saved.recent[0].phase_progress_percent == 100, "Cleared phase progress");
        Check(saved.recent[0].pattern_stats[(int)BossPattern.fist_cross_slam].executions == 1
            && saved.recent[0].pattern_stats[(int)BossPattern.fist_pincer_combo].executions == 1,
            "Four-hand patterns are tracked independently");
        yield return Until(() => study.SyncStatus.Length > 0, "Offline attempt transmission");
        study.Protocol.apiUrl = Endpoint;
        yield return Until(() => !study.HasPending, "Automatic retry after connectivity returns");
        var ratingPanel = UnityEngine.Object.FindAnyObjectByType<StudyPanel>();
        var ratings = new SerializedObject(ratingPanel).FindProperty("ratings");
        Check(ratings.arraySize == 5, "Five star ratings");
        var stars = new Image[ratings.arraySize];
        var buttons = new StudyRatingButton[ratings.arraySize];
        for (int i = 0; i < ratings.arraySize; i++)
        {
            buttons[i] = (StudyRatingButton)ratings.GetArrayElementAtIndex(i).objectReferenceValue;
            stars[i] = (Image)new SerializedObject(buttons[i]).FindProperty("star").objectReferenceValue;
            Check(stars[i] && stars[i].sprite, "Imported star sprite");
        }
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        buttons[2].OnPointerEnter(pointer);
        Check(stars[0].color.a == stars[2].color.a && stars[2].color.a > stars[3].color.a,
            "Hover previews the first three stars");
        buttons[2].OnPointerExit(pointer);
        Check(stars[0].color.a == stars[2].color.a && stars[2].color.a == stars[3].color.a,
            "Pointer exit clears the preview");
        buttons[4].OnPointerClick(pointer);
        yield return Until(() => stars[4].color.a == 1 && stars[4].rectTransform.localScale.x > 1,
            "Click expands the selected star");
        yield return Until(() => study.Screen == StudyScreen.Debrief && SceneManager.GetActiveScene().path == StudySceneEntry.ReviewPath
            && !study.HasPending && !study.Frozen, "Persist rating, restore time and open the debrief");
        Check(Time.timeScale == 1 && !UnityEngine.Object.FindAnyObjectByType<BossArena>(), "Debrief unloads the frozen arena");
        var review = UnityEngine.Object.FindAnyObjectByType<StudyPanel>();
        var replay = Array.Find(review.GetComponentsInChildren<UnityEngine.UI.Button>(),
            button => button.name == "다시 하기");
        Check(replay && replay.interactable, "Replay should be available after sync");
        replay.onClick.Invoke();
        yield return Until(() => study.Screen == StudyScreen.Consent && SceneManager.GetActiveScene().path == StudySceneEntry.IntroPath,
            "Replay should open consent for a new study");
        saved = storage.Load();
        Check(saved.participant == null && saved.attempts == 0 &&
            File.Exists(Path.Combine(storagePath, "completed-studies", participant + ".json")),
            "Replay should archive the completed study and reset the current registration");
        study.Enroll();
        yield return Until(() => study.Screen == StudyScreen.Ready && SceneManager.GetActiveScene().path == StudySceneEntry.GamePath,
            "Replay should register and start a new study");
        Check(study.ParticipantId != participant, "Replay must have a fresh participant identity");
        arena = UnityEngine.Object.FindAnyObjectByType<BossArena>();
        study.BeginAttempt();
        arena.Player.Health.Hit(arena.Player.Health.Current);
        yield return Until(() => study.Screen == StudyScreen.Ready && study.Deaths == 1, "Defeat increments the death count");
        study.BeginAttempt();
        Check(study.InCombat && !arena.CombatEnded, "Retry starts live combat");
        var gamePanel = UnityEngine.Object.FindAnyObjectByType<StudyPanel>();
        var keyboard = InputSystem.AddDevice<Keyboard>();
        void PressEscape()
        {
            // Feed keyboard state directly because batch mode has no focused Game view.
            keyboard.MakeCurrent();
            InputState.Change(keyboard, new KeyboardState(Key.Escape), InputUpdateType.Dynamic);
            typeof(StudyPanel).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(gamePanel, null);
            InputState.Change(keyboard, new KeyboardState(), InputUpdateType.Dynamic);
        }
        try
        {
            PressEscape();
            Check(study.Paused && Time.timeScale == 0 && AudioListener.pause, "Escape pauses gameplay and sound");
            Check(Array.Exists(gamePanel.GetComponentsInChildren<Text>(), text => text.text == "죽은 횟수  1회"),
                "Pause menu shows the participant death count");
            var volume = gamePanel.GetComponentInChildren<Slider>();
            float previousVolume = volume.value;
            volume.value = .35f;
            Check(Mathf.Approximately(AudioListener.volume, .35f), "Slider controls master volume");
            volume.value = previousVolume;
            var resume = Array.Find(gamePanel.GetComponentsInChildren<Button>(), button => button.name == "게임으로 돌아가기");
            resume.onClick.Invoke();
            Check(study.InCombat && !study.Paused && Time.timeScale == 1 && !AudioListener.pause,
                "Resume returns to the same fight");
            yield return null;
            PressEscape();
            Check(study.Paused, "Escape reopens the pause menu");
            float activeTime = storage.Load().active.duration_seconds;
            yield return new WaitForSecondsRealtime(.25f);
            var returnToMenu = Array.Find(gamePanel.GetComponentsInChildren<Button>(), button => button.name == "메뉴로 돌아가기");
            returnToMenu.onClick.Invoke();
            yield return Until(() => study.Screen == StudyScreen.Title && SceneManager.GetActiveScene().path == StudySceneEntry.IntroPath
                && GameObject.Find("메인 메뉴"),
                "Pause menu returns to the title");
            saved = storage.Load();
            Check(Time.timeScale == 1 && !AudioListener.pause && saved.deaths == 1
                && saved.lastAttempt.result == "interrupted" && Mathf.Approximately(saved.lastAttempt.duration_seconds, activeTime),
                "Menu return restores time and sound and saves the attempt without pause time");
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);
        }
        var continueButton = GameObject.Find("메인 메뉴").transform.Find("메뉴 항목/게임 시작").GetComponent<Button>();
        Check(continueButton.GetComponentInChildren<Text>().text == "이어하기", "An active participant sees Continue");
        continueButton.onClick.Invoke();
        yield return Until(() => study.Screen == StudyScreen.Ready && SceneManager.GetActiveScene().path == StudySceneEntry.GamePath,
            "Start resumes the same participant");
        Check(storage.Load().attempts == 2 && study.Deaths == 1, "Menu return preserves attempts and deaths");
        arena = UnityEngine.Object.FindAnyObjectByType<BossArena>();
        study.BeginAttempt();
        arena.SkipCurrentPhase();
        yield return Until(() => arena.Boss.Phase == 2 && !arena.Boss.Transitioning, "Second study enters phase two");
        arena.SkipCurrentPhase();
        yield return Until(() => study.Screen == StudyScreen.Survey && SceneManager.GetActiveScene().path == StudySceneEntry.GamePath,
            "Second study can still clear");
        var secondPanel = UnityEngine.Object.FindAnyObjectByType<StudyPanel>();
        var skipRating = (Button)new SerializedObject(secondPanel).FindProperty("skipRating").objectReferenceValue;
        skipRating.onClick.Invoke();
        yield return Until(() => study.Screen == StudyScreen.Debrief && !study.HasPending, "Sync second study");
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
        var history = new List<AttemptRecord>();
        const string now = "2026-01-01T00:00:00Z";
        var profiles = new StudyProtocol();
        foreach (int target in new[] { 3, 7, 12, 20 })
        {
            var first = AdaptiveDifficulty.Choose(profiles, history, 1, target, now, out _);
            var before = AdaptiveDifficulty.Choose(profiles, history, target - 2, target, now, out _);
            var near = AdaptiveDifficulty.Choose(profiles, history, target, target, now, out var change);
            var after = AdaptiveDifficulty.Choose(profiles, history, target + 3, target, now, out _);
            var later = AdaptiveDifficulty.Choose(profiles, history, target + 8, target, now, out _);
            Check(Mathf.Approximately(first.level, .90f) && first.level == before.level,
                "Difficulty must start hard and stay high before the target window");
            Check(before.level > near.level && near.level > after.level && change.reason == "near_target",
                "The curve must ease through the target");
            Check(Mathf.Approximately(after.level, .12f) && after.level == later.level,
                "Difficulty must stay low after the target window");
            Check(first.boss_health > after.boss_health && first.boss_health <= 1.12f && after.boss_health >= .94f,
                "The level curve must adjust boss durability within the subtle range");
            foreach (var profile in new[] { first, near, after })
                foreach (var pattern in profile.patterns)
                    Check(pattern.speed == 1 && pattern.interval == 1 && pattern.hitbox == 1 && pattern.damage == 1,
                        "Difficulty must keep foreground attack timing, collision and damage consistent");
            Check(AdaptiveDifficulty.SupportRainChance(first.level) > AdaptiveDifficulty.SupportRainChance(after.level)
                && ArenaBoss.RockRainRepeatDelay(first.ParametersFor(BossPattern.rock_rain))
                    < ArenaBoss.RockRainRepeatDelay(after.ParametersFor(BossPattern.rock_rain)),
                "Higher difficulty must increase overlap in both phases");
        }
        var initial = AdaptiveDifficulty.Choose(profiles, history, 1, 12, now, out _);
        var baseline = AdaptiveDifficulty.Choose(profiles, history, 12, 12, now, out _);
        history.Add(Defeat(initial, .95f, 6, 100));
        Array.Resize(ref history[0].pattern_stats, (int)BossPattern.fist_jab);
        var upgraded = AdaptiveDifficulty.Choose(profiles, history, 12, 12, now, out _);
        Check(upgraded.patterns.Length == AdaptiveDifficulty.Patterns.Length
            && upgraded.patterns[(int)BossPattern.fist_double_slam].pattern_id == nameof(BossPattern.fist_double_slam),
            "Older attempt records must work with the expanded pattern list");
        Check(upgraded.level == baseline.level && upgraded.patterns[0].selection_weight < baseline.patterns[0].selection_weight,
            "Past damage must still affect pattern weights without moving the base curve");
        var fixedFirst = AdaptiveDifficulty.Choose(profiles, Array.Empty<AttemptRecord>(), 1, 12, now, out _, fixedHard: true);
        var fixedLater = AdaptiveDifficulty.Choose(profiles, history, 13, 12, now, out var fixedChange, fixedHard: true);
        Check(fixedFirst.level == profiles.maximumLevel && fixedChange.reason == "fixed_hard"
            && JsonUtility.ToJson(fixedFirst) == JsonUtility.ToJson(fixedLater),
            "The fixed-hard group must keep the same profile after a defeat or reaching the target");
        var report = new List<string> { "PASS: goal curves for 3, 7, 12, and 20 attempts, subtle durability, fixed attack timings, support rain, legacy history, pattern weights, and fixed hard. Synthetic play model, seed 315." };
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
