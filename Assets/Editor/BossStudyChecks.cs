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
        yield return new WaitForSecondsRealtime(1.3f);
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
        var golem = (SpriteRenderer)new SerializedObject(menu.GetComponent<StudyMainMenu>())
            .FindProperty("golemLayer").objectReferenceValue;
        Check(golem && golem.sprite && golem.sortingOrder == ArenaBoss.GiantSortingOrder
            && sky.GetComponent<SpriteRenderer>().sortingOrder < golem.sortingOrder
            && Array.Exists(scenerySprites, layer => layer.sortingOrder > golem.sortingOrder),
            "The menu golem uses the battle sort order between scenery and platforms");
        float initialSkyY = sky.localPosition.y;
        float initialGolemY = golem.transform.position.y;
        var mouse = InputSystem.AddDevice<Mouse>();
        try
        {
            mouse.MakeCurrent();
            InputState.Change(mouse, new MouseState { position = new Vector2(Screen.width * .95f, Screen.height * .85f) },
                InputUpdateType.Dynamic);
            yield return new WaitForSecondsRealtime(.2f);
            float leftOffset = Camera.main.WorldToViewportPoint(golem.bounds.center).x - .755f;
            Check(sky.localPosition.x < 0 && leftOffset < 0 && leftOffset > -.02f,
                "Rightward mouse motion shifts the golem slightly to the left");
            InputState.Change(mouse, new MouseState { position = new Vector2(Screen.width * .95f, Screen.height * .12f) },
                InputUpdateType.Dynamic);
            yield return new WaitForSecondsRealtime(.2f);
            Check(Mathf.Approximately(sky.localPosition.y, initialSkyY)
                && Mathf.Approximately(golem.transform.position.y, initialGolemY),
                "Vertical mouse motion does not move the scenery or golem");
            InputState.Change(mouse, new MouseState { position = new Vector2(Screen.width * .05f, Screen.height * .12f) },
                InputUpdateType.Dynamic);
            yield return new WaitForSecondsRealtime(.2f);
            float rightOffset = Camera.main.WorldToViewportPoint(golem.bounds.center).x - .755f;
            Check(sky.localPosition.x > 0 && rightOffset > 0 && rightOffset < .02f,
                "Leftward mouse motion shifts the golem slightly to the right");
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
        Check(scenery.activeSelf && golem.gameObject.activeInHierarchy && !studyBackdrop.activeSelf,
            "The consent screen uses the title scenery and golem");
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
        yield return Until(() => study.InCombat && SceneManager.GetActiveScene().path == StudySceneEntry.GamePath
            && UnityEngine.Object.FindAnyObjectByType<BossArena>()?.Study == study,
            "Consent and assignment start combat without a second button");
        var arena = UnityEngine.Object.FindAnyObjectByType<BossArena>();
        Check(!arena.Finished && UnityEngine.Object.FindObjectsByType<StudySession>(FindObjectsSortMode.None).Length == 1,
            "The existing session must own the game scene");
        var saved = storage.Load();
        Check(saved.participant.age_group == "undisclosed" && saved.participant.gaming_experience == "undisclosed",
            "New enrollment must not collect age or gaming frequency");
        string participant = saved.participant.participant_id;
        Check(Guid.TryParse(participant, out _) && saved.assignment.target_attempts == 7, "UUID and fixed test assignment");
        Check(study.ParticipantId == participant, "UUID persisted and reloaded");
        Check(saved.active.attempt_number == 1 && !GameObject.Find("도전 시작"),
            "The first attempt starts once and the extra start button stays hidden");

        // Set the isolated test profile offline before the attempt closes, then restore it after a failure.
        study.Protocol.apiUrl = "http://127.0.0.1:1";
        arena.Player.Health.Hit(6, 0, true, BossPattern.dash);
        arena.Boss.Health.Hit(arena.Boss.Health.Current);
        var hands = (SpriteRenderer[])typeof(ArenaBoss).GetField("battleFists", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(arena.Boss);
        yield return Until(() => arena.Boss.Transitioning && hands[0].enabled, "Hands rise with the giant");
        Check(hands[0].sortingOrder <= arena.BehindPlatformSortingOrder
            && hands[1].sortingOrder <= arena.BehindPlatformSortingOrder
            && hands[0].maskInteraction == SpriteMaskInteraction.VisibleInsideMask
            && hands[1].maskInteraction == SpriteMaskInteraction.VisibleInsideMask,
            "Rising hands are masked at the ground and render behind the platforms");
        yield return Until(() => arena.Boss.Phase == 2 && !arena.Boss.Transitioning, "Natural phase transition");
        Check(hands[0].maskInteraction == SpriteMaskInteraction.None && hands[1].maskInteraction == SpriteMaskInteraction.None,
            "Combat hands restore their normal rendering");
        var bossHealth = arena.Boss.Health;
        arena.Player.Health.InvulnerableUntil = Time.time + 45;
        bossHealth.Hit(bossHealth.Maximum * .69f);
        yield return null;
        Check(hands[0].enabled && hands[1].enabled && !hands[2].enabled && !hands[3].enabled,
            "Phase two has two hands above the threshold");
        bossHealth.Hit(bossHealth.Maximum * .02f);
        yield return Until(() => hands[2].enabled && hands[3].enabled, "Two more hands spawn below 30 percent");
        var enrageAttacks = new (BossPattern pattern, string intent)[]
        {
            (BossPattern.fist_cross_slam, "네 손 교차 내려찍기"),
            (BossPattern.fist_slam_chain, "연속 표적 내려찍기"),
            (BossPattern.fist_pincer_combo, "네 손 협공"),
            (BossPattern.fist_floor_wave, "낮은 양방향 쓸기"),
            (BossPattern.fist_high_clap, "높은 박수"),
            (BossPattern.fist_diagonal_rush, "엇갈린 대각선 돌진"),
            (BossPattern.fist_rising_columns, "네 손 상승 기둥")
        };
        foreach (var attack in enrageAttacks)
            yield return Until(() => arena.Boss.Intent == attack.intent, $"Enrage attack {attack.pattern}");
        arena.SkipCurrentPhase();
        Check(arena.CombatEnded && !arena.ResultsReady && study.Screen == StudyScreen.Combat,
            "A lethal blow stops combat before displaying the result");
        float combatDuration = study.InspectorElapsedSeconds;
        yield return Until(() => study.Screen == StudyScreen.Survey && SceneManager.GetActiveScene().path == StudySceneEntry.GamePath,
            "Clearing the boss opens the rating panel in the game scene");
        Check(arena && arena.Boss.Health.Dead && study.Frozen && Time.timeScale == 0,
            "Rating preserves the defeated boss and freezes gameplay");
        Check(arena.Boss.Hidden && Array.TrueForAll(hands, hand => !hand.enabled),
            "The giant and fists finish collapsing before the rating appears");
        study.TogglePause();
        Check(study.Frozen && !study.Paused, "Escape cannot replace the victory panel with the pause menu");
        saved = storage.Load();
        Check(saved.pending.Count == 1 && saved.recent[0].cleared && saved.recent[0].attempt_number == 1, "Durable attempt record");
        Check(Mathf.Approximately(saved.recent[0].duration_seconds, combatDuration),
            "Defeat animation time is excluded from the recorded combat duration");
        Check(saved.recent[0].pattern_stats[(int)BossPattern.dash].average_damage == 6, "Measured pattern damage");
        Check(saved.recent[0].damage_dealt > 0 && saved.recent[0].boss_hp_remaining == 0, "Both boss phases recorded");
        Check(saved.recent[0].boss_phase == 2 && saved.recent[0].phase_progress_percent == 100, "Cleared phase progress");
        foreach (var attack in enrageAttacks)
            Check(saved.recent[0].pattern_stats[(int)attack.pattern].executions == 1,
                $"Enrage attack {attack.pattern} is tracked independently");
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
        study.Protocol.apiUrl = "http://127.0.0.1:1";
        buttons[4].OnPointerClick(pointer);
        yield return Until(() => stars[4].color.a == 1 && stars[4].rectTransform.localScale.x > 1,
            "Click expands the selected star");
        yield return Until(() => study.Screen == StudyScreen.Debrief && SceneManager.GetActiveScene().path == StudySceneEntry.ReviewPath
            && !study.Frozen, "Persist rating, restore time and open the debrief");
        Check(Time.timeScale == 1 && !UnityEngine.Object.FindAnyObjectByType<BossArena>(), "Debrief unloads the frozen arena");
        var review = UnityEngine.Object.FindAnyObjectByType<StudyPanel>();
        var reviewData = new SerializedObject(review);
        var reviewScenery = (GameObject)reviewData.FindProperty("debriefScenery").objectReferenceValue;
        var reviewBackdrop = (GameObject)reviewData.FindProperty("studyBackdrop").objectReferenceValue;
        Check(reviewScenery.activeInHierarchy && !reviewBackdrop.activeSelf, "Debrief shows the battle backdrop");
        Check(((Text)reviewData.FindProperty("debrief").objectReferenceValue).text == study.Protocol.debrief,
            "Debrief preserves the full configured notice");
        yield return Until(() => study.HasPending && study.SyncStatus.Length > 0, "Rating remains queued after an offline submission");
        var retryStatus = (Text)reviewData.FindProperty("status").objectReferenceValue;
        var retryButton = (Button)reviewData.FindProperty("retry").objectReferenceValue;
        Check(!retryStatus.gameObject.activeSelf && !retryButton.gameObject.activeSelf,
            "Review hides the upload failure and retry button");
        var replay = Array.Find(review.GetComponentsInChildren<UnityEngine.UI.Button>(),
            button => button.name == "다시 하기");
        Check(replay && replay.interactable, "Replay is available while the rating is still pending");
        study.Protocol.apiUrl = Endpoint;
        replay.onClick.Invoke();
        var archived = new List<StudySave>(storage.LoadPendingArchives(null));
        Check(archived.Count == 1 && archived[0].participant.participant_id == participant
            && archived[0].pending[0].route == "surveys", "Pending rating survives replay and can be reloaded from disk");
        Check(new List<StudySave>(storage.LoadPendingArchives(participant)).Count == 0,
            "An archive matching the current participant must not send twice after an interrupted replay");
        yield return Until(() => study.Screen == StudyScreen.Consent && SceneManager.GetActiveScene().path == StudySceneEntry.IntroPath,
            "Replay should open consent for a new study");
        saved = storage.Load();
        Check(saved.participant == null && saved.attempts == 0 &&
            File.Exists(Path.Combine(storagePath, "completed-studies", participant + ".json")),
            "Replay should archive the completed study and reset the current registration");
        study.Enroll();
        yield return Until(() => study.InCombat && SceneManager.GetActiveScene().path == StudySceneEntry.GamePath,
            "Replay should register and start a new study");
        yield return Until(() => new List<StudySave>(storage.LoadPendingArchives(null)).Count == 0,
            "The old participant rating syncs from the archive in the background");
        Check(study.ParticipantId != participant, "Replay must have a fresh participant identity");
        arena = UnityEngine.Object.FindAnyObjectByType<BossArena>();
        arena.Player.Health.Hit(arena.Player.Health.Current);
        yield return Until(() => study.Screen == StudyScreen.Defeat && study.Deaths == 1, "Defeat waits for retry");
        yield return new WaitForSecondsRealtime(.15f);
        saved = storage.Load();
        Check(saved.lastAttempt.result == "defeat" && saved.lastAttempt.attempt_number == 1 && saved.active == null,
            "Defeat is stored without starting the next attempt");
        var gamePanel = UnityEngine.Object.FindAnyObjectByType<StudyPanel>();
        var hud = UnityEngine.Object.FindAnyObjectByType<ArenaHud>();
        var resultLabel = (Text)new SerializedObject(hud).FindProperty("result").objectReferenceValue;
        Check(resultLabel.isActiveAndEnabled && resultLabel.text == "Enter로 다시 도전" && !GameObject.Find("보스전 준비 화면"),
            "Defeat shows only the Enter prompt over the arena");
        var keyboard = InputSystem.AddDevice<Keyboard>();
        void PressKey(Key key)
        {
            // Feed keyboard state directly because batch mode has no focused Game view.
            keyboard.MakeCurrent();
            InputState.Change(keyboard, new KeyboardState(key), InputUpdateType.Dynamic);
            typeof(StudyPanel).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(gamePanel, null);
            InputState.Change(keyboard, new KeyboardState(), InputUpdateType.Dynamic);
        }
        try
        {
            PressKey(Key.Enter);
            saved = storage.Load();
            Check(study.InCombat && saved.active.attempt_number == 2
                && saved.active.participant_id == saved.lastAttempt.participant_id
                && arena.Player.Health.Current == arena.Player.Health.Maximum && arena.Boss.Phase == 1,
                "Enter restarts phase one with full health and the same participant");
            yield return null;
            Check(resultLabel.text.Length == 0, "Retry clears the HUD prompt");
            PressKey(Key.Escape);
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
            PressKey(Key.Escape);
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
        yield return Until(() => study.InCombat && SceneManager.GetActiveScene().path == StudySceneEntry.GamePath,
            "Continue starts combat without a second button");
        saved = storage.Load();
        Check(saved.attempts == 3 && saved.active.attempt_number == 3 && study.Deaths == 1,
            "Continue starts a new attempt and preserves the prior attempts and death count");
        arena = UnityEngine.Object.FindAnyObjectByType<BossArena>();
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
