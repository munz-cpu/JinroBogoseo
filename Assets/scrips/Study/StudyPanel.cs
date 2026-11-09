using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BossTemplate
{
    internal sealed class StudyPanel : MonoBehaviour
    {
        private static readonly Color Accent = new Color(.16f, .32f, .45f);
        private static readonly Color Selected = new Color(.22f, .5f, .39f);
        private static readonly string[] AgeIds = { "under_18", "18_24", "25_34", "35_44", "45_plus", "undisclosed" };
        private static readonly string[] FrequencyIds = { "rarely", "monthly", "weekly", "daily", "undisclosed" };

        [SerializeField] private GameObject[] screens;
        [SerializeField] private Button start, enroll, decline, reopen, cancelRegistration;
        [SerializeField] private Text consentNotice, consentDetails;
        [SerializeField] private GameObject demographics;
        [SerializeField] private Button[] ages, frequencies;
        [SerializeField] private Text readyTitle, attemptLabel;
        [SerializeField] private Button attempt, stopFromReady, stopFromCombat;
        [SerializeField] private Text ratingTitle, debrief, participant, contact, withdrawalMessage;
        [SerializeField] private Button[] ratings;
        [SerializeField] private Button skipRating, withdraw, retry;
        [SerializeField] private Text status;

        private StudySession session;
        private GameObject activeScreen;
        private int age = -1, frequency = -1;

        internal void Bind(StudySession owner, Font font)
        {
            session = owner;
            if (font)
                foreach (var text in GetComponentsInChildren<Text>(true)) text.font = font;

            Listen(start, session.OpenIntro);
            Listen(enroll, () =>
            {
                if (enroll.interactable) session.Enroll(AgeIds[age], FrequencyIds[frequency]);
            });
            Listen(decline, session.Decline);
            Listen(reopen, session.ReopenConsent);
            Listen(cancelRegistration, session.Withdraw);
            Listen(attempt, session.BeginAttempt);
            Listen(stopFromReady, session.StopStudy);
            Listen(stopFromCombat, session.StopStudy);
            Listen(skipRating, () => session.SubmitRating(null));
            Listen(withdraw, session.Withdraw);
            Listen(retry, session.Retry);

            for (int i = 0; i < ages.Length; i++)
            {
                int index = i;
                Listen(ages[i], () => { age = index; UpdateEnrollment(); });
            }
            for (int i = 0; i < frequencies.Length; i++)
            {
                int index = i;
                Listen(frequencies[i], () => { frequency = index; UpdateEnrollment(); });
            }
            for (int i = 0; i < ratings.Length; i++)
            {
                int rating = i + 1;
                Listen(ratings[i], () => session.SubmitRating(rating));
            }
        }

        internal bool ConsumesShot(Vector2 pointer) =>
            stopFromCombat && stopFromCombat.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)stopFromCombat.transform, pointer);

        internal void Render(StudyScreen screen)
        {
            if (activeScreen) activeScreen.SetActive(false);
            activeScreen = screens[(int)screen];
            if (!activeScreen)
            {
                Debug.LogError($"Study screen {screen} has no scene reference.", this);
                return;
            }

            switch (screen)
            {
                case StudyScreen.Consent:
                    var settings = session.Protocol;
                    consentNotice.text = settings.consentNotice;
                    demographics.SetActive(settings.IsConfigured);
                    consentDetails.text = settings.IsConfigured
                        ? $"데이터 보관 기간  {settings.retention}\n삭제·제외 요청  실험 종료 후 화면의 제외 요청 버튼을 사용하거나 참가자 ID와 함께 {settings.contact}에 문의하세요."
                        : Application.isEditor && (string.IsNullOrWhiteSpace(settings.retention) || string.IsNullOrWhiteSpace(settings.contact))
                            ? "참여하려면 BossStudySettings의 Protocol에 데이터 보관 기간(Retention)과 담당자 연락처(Contact)를 입력하세요."
                            : "연구 설정이 완료되면 참여할 수 있습니다. 담당자에게 문의하세요.";
                    UpdateEnrollment();
                    break;
                case StudyScreen.Ready:
                    readyTitle.text = session.LostLast ? "보스전 패배" : "보스전 준비";
                    attemptLabel.text = session.LostLast ? "다시 도전" : "도전 시작";
                    break;
                case StudyScreen.Survey:
                    ratingTitle.text = session.Cleared ? "보스전 승리" : "도전 종료";
                    break;
                case StudyScreen.Debrief:
                    debrief.text = session.Protocol.debrief;
                    participant.text = $"참가자 ID  {session.ParticipantId}";
                    contact.text = $"데이터 제외 및 문의  {session.Protocol.contact}";
                    break;
                case StudyScreen.Withdrawal:
                    withdrawalMessage.text = session.WithdrawalPending
                        ? "제외 요청이 대기 중입니다. 인터넷에 연결하고 접수될 때까지 이 화면을 열어 두세요."
                        : "서버에서 제외 요청을 접수했습니다. 분석용 시트는 다음 동기화에서 갱신됩니다.";
                    break;
            }

            if (activeScreen.TryGetComponent(out ScrollRect scroll)) scroll.verticalNormalizedPosition = 1;
            activeScreen.SetActive(true);
            RefreshStatus();
        }

        internal void RefreshStatus()
        {
            if (!status) return;
            bool showNetwork = session.Screen is StudyScreen.Waiting or StudyScreen.Ready or StudyScreen.Survey or StudyScreen.Debrief
                || (session.Screen == StudyScreen.Withdrawal && session.WithdrawalPending);
            string message = session.SyncStatus;
            if (string.IsNullOrEmpty(message) && session.HasPending) message = "서버로 보낼 기록이 대기 중입니다.";
            status.text = message;
            status.gameObject.SetActive(showNetwork && message.Length > 0);
            retry.gameObject.SetActive(showNetwork && message.Length > 0);
        }

        private void UpdateEnrollment()
        {
            for (int i = 0; i < ages.Length; i++) ages[i].image.color = i == age ? Selected : Accent;
            for (int i = 0; i < frequencies.Length; i++) frequencies[i].image.color = i == frequency ? Selected : Accent;
            enroll.interactable = age >= 0 && frequency >= 0 && session.Protocol.IsConfigured;
        }

        private static void Listen(Button button, UnityAction callback)
        {
            if (button) button.onClick.AddListener(callback);
        }
    }
}
