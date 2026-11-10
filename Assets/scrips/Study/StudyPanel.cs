using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BossTemplate
{
    internal sealed class StudyPanel : MonoBehaviour
    {
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
        [SerializeField] private bool useSceneFonts;
        [SerializeField] private Color choiceColor = new Color(.16f, .32f, .45f);
        [SerializeField] private Color selectedChoiceColor = new Color(.22f, .5f, .39f);

        private StudySession session;
        private GameObject activeScreen;
        private int age = -1, frequency = -1;

        internal void Bind(StudySession owner, Font font)
        {
            session = owner;
            foreach (var screen in screens)
                if (screen) screen.SetActive(false);
            if (font && !useSceneFonts)
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
                        ? $"보관 기간  {settings.retention}\n기록 삭제는 종료 화면에서 요청하거나, 참가자 ID와 함께 {settings.contact}에 문의하세요."
                        : Application.isEditor && (string.IsNullOrWhiteSpace(settings.retention) || string.IsNullOrWhiteSpace(settings.contact))
                            ? "BossStudySettings → Protocol에서 Retention(보관 기간)과 Contact(문의처)를 입력하세요."
                            : "아직 참여할 수 없습니다. 담당자에게 문의하세요.";
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
                    contact.text = $"문의  {session.Protocol.contact}";
                    break;
                case StudyScreen.Withdrawal:
                    withdrawalMessage.text = session.WithdrawalPending
                        ? "인터넷에 연결한 뒤 요청이 접수될 때까지 기다려 주세요."
                        : "기록 제외 요청을 접수했습니다.";
                    break;
            }

            activeScreen.SetActive(true);
            if (activeScreen.TryGetComponent(out ScrollRect scroll))
            {
                scroll.StopMovement();
                scroll.verticalNormalizedPosition = 1;
            }
            RefreshStatus();
        }

        internal void RefreshStatus()
        {
            if (!status) return;
            bool showNetwork = session.Screen is StudyScreen.Waiting or StudyScreen.Ready or StudyScreen.Survey or StudyScreen.Debrief
                || (session.Screen == StudyScreen.Withdrawal && session.WithdrawalPending);
            string message = session.SyncStatus;
            if (string.IsNullOrEmpty(message) && session.HasPending) message = "기록 전송 대기 중";
            status.text = message;
            status.gameObject.SetActive(showNetwork && message.Length > 0);
            retry.gameObject.SetActive(showNetwork && message.Length > 0);
        }

        private void UpdateEnrollment()
        {
            for (int i = 0; i < ages.Length; i++) ages[i].image.color = i == age ? selectedChoiceColor : choiceColor;
            for (int i = 0; i < frequencies.Length; i++) frequencies[i].image.color = i == frequency ? selectedChoiceColor : choiceColor;
            enroll.interactable = age >= 0 && frequency >= 0 && session.Protocol.IsConfigured;
        }

        private static void Listen(Button button, UnityAction callback)
        {
            if (button) button.onClick.AddListener(callback);
        }
    }
}
