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
        private const string DebriefContactNotice = "기록 제외를 원하면 참가자 ID와 함께 아래 문의처로 연락해 주세요.";
        [Header("서버와 연구 버전")]
        public string apiUrl = "http://127.0.0.1:8000";
        public string settingsVersion = "combat-v7";
        public string consentVersion = "consent-v6";
        [Header("동의와 사후 안내")]
        [TextArea(4, 10)] public string consentNotice =
            "진로 보고서에 쓸 자료를 모으는 보스전 테스트입니다. 자세한 목적과 플레이 조건은 게임이 끝난 뒤 안내합니다.\n\n" +
            "도전 횟수, 플레이 시간, 피해량, 전투 진행, 난이도 변경, 별점을 참가자 ID와 함께 기록합니다. " +
            "참여는 자유이며 언제든 그만둘 수 있습니다.";
        public string retention = "";
        public string contact = "";
        [TextArea(3, 8)] public string debrief =
            "도전 횟수에 따라 성취감이 달라지는지 알아보는 테스트였습니다.\n\n" +
            "목표 도전 횟수는 무작위로 정했습니다. 일부 그룹에서는 보스가 목표 횟수 근처에서 점차 쉬워졌고, 고정 난이도 그룹은 처음부터 끝까지 어려운 설정을 사용했습니다. " +
            "목표보다 일찍 클리어할 수도 있었습니다.\n\n" +
            DebriefContactNotice;
        internal string DebriefText => debrief?.Replace("기록을 보고서에서 빼려면 아래 버튼을 눌러 주세요.", DebriefContactNotice)
            .Replace("원하면 아래 버튼으로 연구 데이터 제외를 요청할 수 있어요.", DebriefContactNotice);
        [Header("연구 난이도와 전투 배수")]
        [Range(0, 1)] public float minimumLevel = 0;
        [Range(0, 1)] public float maximumLevel = 1;
        [Tooltip("X는 낮은 난이도, Y는 높은 난이도의 보스 체력 배수입니다.")]
        public Vector2 bossHealth = new Vector2(.94f, 1.12f);
        [Tooltip("패턴 등장 가중치의 X는 낮은 난이도, Y는 높은 난이도입니다.")]
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
        public Vector2 selectionWeight = new Vector2(.8f, 1.2f);
    }
}
