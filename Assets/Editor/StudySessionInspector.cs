using System;
using System.IO;
using BossTemplate;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(StudySession))]
internal sealed class StudySessionInspector : Editor
{
    private bool showPatterns = true;
    private bool loadedStoredState, failedToLoad;
    private StudySave storedState;
    private DifficultySnapshot preview;
    private StudySave previewState;
    private StudyAssignment previewAssignment;
    private int previewAttempt;

    public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        bool playing = EditorApplication.isPlaying;
        var session = playing ? StudySession.Current : null;
        var state = playing ? session?.InspectorState : ReadStoredState();
        if (playing) loadedStoredState = false;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("연구 진행 현황", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("진행 단계", session ? session.Screen.ToString() : "편집 모드 · 로컬 기록");
        if (!playing && failedToLoad)
            EditorGUILayout.HelpBox("로컬 연구 기록을 읽을 수 없습니다.", MessageType.Warning);
        EditorGUILayout.LabelField("누적 도전 횟수", (state?.attempts ?? 0).ToString());
        var assignment = state?.assignment;
        if (assignment != null)
        {
            EditorGUILayout.LabelField("실험 그룹", assignment.experiment_group);
            EditorGUILayout.LabelField("목표 도전 횟수", assignment.target_attempts.ToString());
            EditorGUILayout.LabelField("난이도 방식", assignment.FixedHard ? "고정 어려움" : "목표 횟수 곡선");
        }

        var active = state?.active;
        var last = state?.lastAttempt ?? (state?.recent.Count > 0 ? state.recent[state.recent.Count - 1] : null);
        var attempt = active ?? last;
        bool live = session && active != null && session.InCombat;
        var difficulty = attempt?.difficulty ?? PreviewDifficulty(state);
        if (attempt != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(live ? "현재 도전" : active != null ? "저장된 진행 중 도전" : "최근 도전 기록",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField("도전 번호", attempt.attempt_number.ToString());
            if (active == null) EditorGUILayout.LabelField("결과", attempt.result);
            float elapsed = live ? session.InspectorElapsedSeconds : attempt.duration_seconds;
            float bossHealth = live ? session.InspectorBossHealthRemaining : attempt.boss_hp_remaining;
            EditorGUILayout.LabelField("경과 시간", $"{elapsed:F1}초");
            EditorGUILayout.LabelField("보스 잔여 체력", $"{bossHealth:P1}");
            EditorGUILayout.LabelField("준 피해량", $"{attempt.damage_dealt:F1}");
            EditorGUILayout.LabelField("받은 피해량", $"{attempt.damage_taken:F1}");
        }
        else
        {
            EditorGUILayout.HelpBox("표시할 피해 기록이 없습니다. 연구 전투를 시작하면 패턴별 피해와 다음 추첨 확률이 실시간으로 표시됩니다.",
                MessageType.Info);
            if (difficulty != null)
                EditorGUILayout.LabelField("도전 기록이 없을 때의 추첨 미리보기", EditorStyles.boldLabel);
        }
        if (difficulty == null) return;

        EditorGUILayout.LabelField("난이도", $"{difficulty.level:F2}");
        EditorGUILayout.LabelField("보스 체력 배수", $"{difficulty.boss_health:F2}×");
        if (last != null && active == null && assignment != null && !state.surveyDue && !state.debrief)
        {
            var next = PreviewDifficulty(state);
            if (next != null) EditorGUILayout.LabelField("다음 도전 난이도", $"{next.level:F2}");
        }

        showPatterns = EditorGUILayout.Foldout(showPatterns, "공격 패턴별 스탯과 등장 확률", true);
        if (!showPatterns) return;

        int phase = live ? session.InspectorBossPhase : 0;
        var previous = live ? session.InspectorPreviousPattern : null;
        var stats = attempt?.pattern_stats;
        EditorGUILayout.HelpBox("첫 추첨은 같은 페이즈의 전체 패턴을 대상으로 합니다. 전투 중 다음 추첨에서는 직전 패턴이 제외됩니다.", MessageType.Info);
        DrawPhase(difficulty, stats, StudySession.PhaseOne, 1, phase == 1, previous);
        DrawRain(difficulty, stats);
        DrawPhase(difficulty, stats, StudySession.PhaseTwo, 2, phase == 2, previous);
    }

    private StudySave ReadStoredState()
    {
        if (loadedStoredState) return storedState;
        loadedStoredState = true;
        failedToLoad = false;
        try
        {
            storedState = new StudyStorage(Application.persistentDataPath).Load();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            storedState = null;
            failedToLoad = true;
        }
        return storedState;
    }

    private DifficultySnapshot PreviewDifficulty(StudySave state)
    {
        int attempt = (state?.attempts ?? 0) + 1;
        if (preview != null && state == previewState && state?.assignment == previewAssignment && attempt == previewAttempt)
            return preview;
        var settings = AssetDatabase.LoadAssetAtPath<StudySettings>("Assets/Settings/BossStudySettings.asset");
        var protocol = (state?.participant != null ? state.protocol : null) ?? settings?.protocol;
        if (protocol == null) return null;
        previewState = state;
        previewAssignment = state?.assignment;
        previewAttempt = attempt;
        var history = state != null ? (System.Collections.Generic.IReadOnlyList<AttemptRecord>)state.recent : Array.Empty<AttemptRecord>();
        preview = AdaptiveDifficulty.Choose(protocol, history, attempt,
            previewAssignment?.target_attempts ?? 1, StudySession.Now(), out _, previewAssignment?.FixedHard == true,
            deaths: state?.deaths ?? 0);
        return preview;
    }

    private static void DrawPhase(DifficultySnapshot difficulty, PatternStat[] stats, BossPattern[] candidates, int phase,
        bool showNext, BossPattern? previous)
    {
        BossPattern? excluded = showNext && previous.HasValue && Array.IndexOf(candidates, previous.Value) >= 0
            ? previous : null;
        float firstTotal = DifficultySnapshot.SelectionWeightTotal(difficulty, candidates, null);
        float nextTotal = showNext ? DifficultySnapshot.SelectionWeightTotal(difficulty, candidates, excluded) : 0;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"{phase}페이즈 전경 공격", EditorStyles.boldLabel);
        foreach (var pattern in candidates)
        {
            var parameters = difficulty.ParametersFor(pattern);
            if (parameters == null) continue;
            DrawPattern(stats, pattern, parameters);
            if (firstTotal <= 0) continue;
            float weight = parameters.selection_weight;
            string chances = $"첫 추첨 {weight / firstTotal:P1}";
            if (showNext && nextTotal > 0)
                chances += $"  /  다음 {(pattern == excluded ? 0 : weight / nextTotal):P1}";
            EditorGUILayout.LabelField(chances, EditorStyles.miniLabel);
        }
    }

    private static void DrawRain(DifficultySnapshot difficulty, PatternStat[] stats)
    {
        const BossPattern pattern = BossPattern.rock_rain;
        var parameters = difficulty.ParametersFor(pattern);
        if (parameters == null) return;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("보조 낙석", EditorStyles.boldLabel);
        DrawPattern(stats, pattern, parameters);
        if (parameters.selection_weight > 0)
            EditorGUILayout.LabelField($"1페이즈 · 공격 종료 후 {ArenaBoss.RockRainRepeatDelay(parameters):F2}초 대기",
                EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"2페이즈 · 잽·어퍼컷·후려치기에 낙석이 겹칠 확률 {AdaptiveDifficulty.SupportRainChance(difficulty.level):P1}",
            EditorStyles.wordWrappedMiniLabel);
    }

    private static void DrawPattern(PatternStat[] stats, BossPattern pattern, PatternParameters parameters)
    {
        string id = pattern.ToString();
        var stat = stats != null ? Array.Find(stats, item => item?.pattern_id == id) : null;
        EditorGUILayout.LabelField(id, EditorStyles.boldLabel);
        if (stat != null)
            EditorGUILayout.LabelField($"실행 {stat.executions} / 피격 {stat.hit_count} / 피해 {stat.damage_taken:F1} / 평균 {stat.average_damage:F1}",
                EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.LabelField(
            $"가중치 {parameters.selection_weight:F2} / 속도 {parameters.speed:F2}× / 간격 {parameters.interval:F2}× / 판정 {parameters.hitbox:F2}× / 피해 {parameters.damage:F2}×",
            EditorStyles.wordWrappedMiniLabel);
    }
}
