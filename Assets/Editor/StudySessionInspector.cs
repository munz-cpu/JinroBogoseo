using BossTemplate;
using UnityEditor;

[CustomEditor(typeof(StudySession))]
internal sealed class StudySessionInspector : Editor
{
    private bool showPatterns;

    public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var session = (StudySession)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("연구 진행 현황", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("진행 단계", session.Screen.ToString());
        EditorGUILayout.LabelField("누적 도전 횟수", session.InspectorAttemptCount.ToString());
        var assignment = session.InspectorAssignment;
        if (assignment != null)
        {
            EditorGUILayout.LabelField("실험 그룹", assignment.experiment_group);
            EditorGUILayout.LabelField("목표 도전 횟수", assignment.target_attempts.ToString());
        }

        var active = session.InspectorActiveAttempt;
        var attempt = active ?? session.InspectorLastAttempt;
        if (attempt == null)
        {
            EditorGUILayout.HelpBox("표시할 도전 기록이 없습니다.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(active != null ? "현재 도전" : "최근 도전 기록", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("도전 번호", attempt.attempt_number.ToString());
        if (active == null) EditorGUILayout.LabelField("결과", attempt.result);
        float elapsed = active != null ? session.InspectorElapsedSeconds : attempt.duration_seconds;
        float bossHealth = active != null ? session.InspectorBossHealthRemaining : attempt.boss_hp_remaining;
        EditorGUILayout.LabelField("경과 시간", $"{elapsed:F1}초");
        EditorGUILayout.LabelField("보스 잔여 체력", $"{bossHealth:P1}");
        EditorGUILayout.LabelField("준 피해량", $"{attempt.damage_dealt:F1}");
        EditorGUILayout.LabelField("받은 피해량", $"{attempt.damage_taken:F1}");
        EditorGUILayout.LabelField("적용 난이도", $"{attempt.difficulty.level:F2}");
        EditorGUILayout.LabelField("보스 체력 배수", $"{attempt.difficulty.boss_health:F2}×");

        showPatterns = EditorGUILayout.Foldout(showPatterns, "공격 패턴별 스탯", true);
        if (!showPatterns) return;

        foreach (var stat in attempt.pattern_stats)
        {
            EditorGUILayout.LabelField(stat.pattern_id, EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"실행 {stat.executions}회 / 피격 {stat.hit_count}회 / 피해 {stat.damage_taken:F1}");
        }
    }
}
