using UnityEngine;

namespace BossTemplate
{
    internal enum StudyStage { Start, Game, Review, Intro }

    internal sealed class StudySceneEntry : MonoBehaviour
    {
        internal const string StartPath = "Assets/Scenes/BossStudyStart.unity";
        internal const string IntroPath = "Assets/Scenes/BossStudyIntro.unity";
        internal const string GamePath = "Assets/Scenes/BossStudyGame.unity";
        internal const string ReviewPath = "Assets/Scenes/BossStudyReview.unity";

        [SerializeField, InspectorName("연구 단계")] private StudyStage stage;
        [SerializeField, InspectorName("연구 설정")] private StudySettings settings;
        [SerializeField, InspectorName("보스전 경기장")] private BossArena arena;
        [SerializeField, InspectorName("연구 세션")] private StudySession sceneSession;
        [SerializeField, InspectorName("연구 화면")] private StudyPanel panel;

        internal static string ScenePath(StudyStage stage) => stage switch
        {
            StudyStage.Intro => IntroPath,
            StudyStage.Game => GamePath,
            StudyStage.Review => ReviewPath,
            _ => StartPath
        };

        private void Start()
        {
            if (!settings || !sceneSession || !panel)
            {
                Debug.LogError("Study scene references are missing.", this);
                return;
            }
            var session = StudySession.Current;
            if (!session)
            {
                session = sceneSession;
                session.Activate();
            }
            session.EnterScene(stage, arena, settings, panel);
        }
    }
}
