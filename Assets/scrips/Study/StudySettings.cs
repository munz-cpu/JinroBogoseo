using System;
using UnityEngine;

namespace BossTemplate
{
    [CreateAssetMenu(menuName = "Boss Arena/Study Settings")]
    public sealed class StudySettings : ScriptableObject
    {
        public bool enabledForResearch = true;
        public Font font;
        public StudyProtocol protocol = new StudyProtocol();
    }

    [Serializable]
    public sealed class StudyProtocol
    {
        public string apiUrl = "http://127.0.0.1:8000";
        public string settingsVersion = "combat-v1";
        public string consentVersion = "consent-v1";
        [TextArea(4, 10)] public string consentNotice =
            "이 학교 연구는 게임 플레이 경험을 분석합니다. 도전 횟수, 플레이 시간, 피해량, 게임 진행 상태, " +
            "난이도 변경 기록, 연령대와 게임 빈도 및 설문 응답을 수집합니다. 일부 연구 가설과 실험 조건은 종료 후 설명합니다. " +
            "참여 여부는 자유이며 언제든 중단할 수 있습니다. UUID로 기록을 연결합니다.";
        public string retention = "";
        public string contact = "";
        [TextArea(3, 8)] public string debrief =
            "이 연구는 보스전의 반복 도전 횟수와 성취감의 관계를 알아봅니다. 참가자마다 목표 횟수를 무작위로 배정했습니다. " +
            "게임은 도전 사이에 전투 기록과 목표까지 남은 횟수를 바탕으로 보스의 공격과 체력을 조절했습니다. " +
            "목표 횟수에 도달하기 전에도 클리어할 수 있었습니다. 아래 버튼으로 연구 데이터 제외를 요청할 수 있습니다.";
        public string achievementCleared = "보스를 클리어하는 과정에서 성취감을 느꼈다.";
        public string achievementStopped = "보스에 도전하는 과정에서 성취감을 느꼈다.";
        public string immersion = "게임에 몰입할 수 있었다.";
        public string difficulty = "보스의 난이도가 적절했다고 느꼈다.";
        public string fairness = "보스의 공격이 공정하다고 느꼈다.";
        public string retryIntention = "다시 도전하고 싶다는 생각이 들었다.";
        public string frustration = "플레이 과정에서 좌절감을 느꼈다.";
        [Range(0, 1)] public float minimumLevel = 0;
        [Range(0, 1)] public float maximumLevel = 1;
        [Range(0, 1)] public float initialLevel = .48f;
        [Range(.01f, .25f)] public float maximumStep = .12f;
        public Vector2 bossHealth = new Vector2(.72f, 1.24f);
        public PatternRange defaultPattern = new PatternRange();
        public PatternRange[] patternOverrides = Array.Empty<PatternRange>();

        internal string[] Questions(bool cleared) => new[]
        {
            cleared ? achievementCleared : achievementStopped,
            immersion, difficulty, fairness, retryIntention, frustration
        };

        internal bool IsConfigured =>
            !string.IsNullOrWhiteSpace(retention) && !string.IsNullOrWhiteSpace(contact)
            && !string.IsNullOrWhiteSpace(consentNotice) && !string.IsNullOrWhiteSpace(debrief)
            && ValidLabel(settingsVersion) && ValidLabel(consentVersion) && ValidEndpoint(apiUrl);

        private static bool ValidLabel(string value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 80;

        private static bool ValidEndpoint(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0
                || uri.Query.Length > 0 || uri.Fragment.Length > 0) return false;
            if (uri.Scheme == Uri.UriSchemeHttps) return true;
            return uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback && (Application.isEditor || Debug.isDebugBuild);
        }
    }

    [Serializable]
    public sealed class PatternRange
    {
        public string patternId;
        public Vector2 speed = new Vector2(.85f, 1.15f);
        public Vector2 interval = new Vector2(1.3f, .82f);
        public Vector2 hitbox = new Vector2(.83f, 1.12f);
        public Vector2 damage = new Vector2(.75f, 1.18f);
        public Vector2 selectionWeight = new Vector2(.8f, 1.2f);
    }
}
