using UnityEngine;

namespace BossTemplate
{
    internal enum StudyStage { Start, Game, Review }

    internal sealed class StudySceneEntry : MonoBehaviour
    {
        internal const string StartPath = "Assets/Scenes/BossStudyStart.unity";
        internal const string GamePath = "Assets/Scenes/BossStudyGame.unity";
        internal const string ReviewPath = "Assets/Scenes/BossStudyReview.unity";

        [SerializeField, InspectorName("연구 단계")] private StudyStage stage;
        [SerializeField, InspectorName("연구 설정")] private StudySettings settings;
        [SerializeField, InspectorName("보스전 경기장")] private BossArena arena;

        internal static string ScenePath(StudyStage stage) => stage switch
        {
            StudyStage.Game => GamePath,
            StudyStage.Review => ReviewPath,
            _ => StartPath
        };

        private void Start()
        {
            var session = FindAnyObjectByType<StudySession>();
            if (!session)
            {
                if (!settings)
                {
                    Debug.LogError("Study scene settings are missing.", this);
                    return;
                }
                var sessionRoot = new GameObject("연구 세션");
                DontDestroyOnLoad(sessionRoot);
                session = sessionRoot.AddComponent<StudySession>();
            }
            session.EnterScene(stage, arena, settings);
        }
    }
}
