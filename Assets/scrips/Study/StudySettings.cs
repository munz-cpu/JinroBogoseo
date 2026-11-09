using System;
using UnityEngine;

namespace BossTemplate
{
    [CreateAssetMenu(menuName = "Boss Arena/Study Settings")]
    public sealed class StudySettings : ScriptableObject
    {
        public Font font;
        public StudyProtocol protocol = new StudyProtocol();
    }

    [Serializable]
    public sealed class StudyProtocol
    {
        [Header("서버와 연구 버전")]
        public string apiUrl = "http://127.0.0.1:8000";
        public string settingsVersion = "combat-v2";
        public string consentVersion = "consent-v3";
        [Header("동의와 사후 안내")]
        [TextArea(4, 10)] public string consentNotice =
            "보스전에 도전해 보고, 공격을 피하는 재미와 전투 난이도가 어땠는지 알려주세요. " +
            "학교 연구용 플레이테스트예요. 자세한 연구 질문과 플레이 조건은 끝난 뒤 알려드려요.\n\n" +
            "도전 횟수, 시간, 피해량, 전투 진행과 난이도 조정 기록, 연령대, 게임 빈도, 별점을 참가자 ID로 연결해 기록해요. " +
            "참여는 자유이고 언제든 그만둘 수 있어요.";
        public string retention = "";
        public string contact = "";
        [TextArea(3, 8)] public string debrief =
            "플레이해 주셔서 고마워요! 이번 테스트에서는 보스에게 몇 번 도전했는지와 플레이 중 느낀 성취감이 어떤 관계인지 살펴봐요. " +
            "이 질문을 미리 알면 플레이 방식에 영향을 줄 수 있어서 끝난 뒤에 알려드려요.\n\n" +
            "목표 도전 횟수는 참가자마다 무작위로 배정됐어요. 도전 사이에 전투 기록과 목표까지 남은 횟수에 따라 보스의 공격과 체력이 조정됐고, " +
            "목표 횟수 전에도 클리어할 수 있었어요. 원하면 아래 버튼으로 연구 데이터 제외를 요청할 수 있어요.";
        [Header("연구 난이도와 전투 배수")]
        [Range(0, 1)] public float minimumLevel = 0;
        [Range(0, 1)] public float maximumLevel = 1;
        [Range(0, 1)] public float initialLevel = .48f;
        [Range(.01f, .25f)] public float maximumStep = .12f;
        [Tooltip("X는 낮은 난이도, Y는 높은 난이도의 보스 체력 배수입니다.")]
        public Vector2 bossHealth = new Vector2(.72f, 1.24f);
        [Tooltip("각 항목의 X는 낮은 난이도, Y는 높은 난이도의 배수입니다.")]
        public PatternRange defaultPattern = new PatternRange();
        [Tooltip("Pattern Id가 일치하는 공격에 개별 배수를 적용합니다. 예: fist_sweep")]
        public PatternRange[] patternOverrides = Array.Empty<PatternRange>();

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
