using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BossTemplate
{
    [DefaultExecutionOrder(-100)]
    internal sealed class StudyPanel : MonoBehaviour
    {
        [SerializeField] private GameObject[] screens;
        [SerializeField] private Button start, enroll, decline, reopen, cancelRegistration;
        [SerializeField] private Text consentNotice, consentDetails;
        [SerializeField] private Text readyTitle, attemptLabel;
        [SerializeField] private Button attempt;
        [SerializeField] private Text ratingTitle, debrief, participant, contact, withdrawalMessage;
        [SerializeField] private Font menuFont;
        [SerializeField] private Sprite[] menuBackground;
        [SerializeField] private Sprite menuGolem;
        [SerializeField] private Sprite victoryStar;
        [SerializeField] private StudyRatingButton[] ratings;
        [SerializeField] private Button skipRating, withdraw, retry, replay;
        [SerializeField] private Text status;
        [SerializeField] private bool useSceneFonts;

        private StudySession session;
        private GameObject activeScreen;
        private bool submittingRating;
        private StudyPauseMenu pauseMenu;
        private StudyVictoryPanel victoryPanel;
        private StudyMainMenu mainMenu;
        internal bool HasVictoryPanel => victoryPanel != null;

        internal void Bind(StudySession owner, Font font)
        {
            session = owner;
            if (menuGolem && menuBackground?.Length == 4)
            {
                mainMenu = new StudyMainMenu((RectTransform)transform, menuFont ? menuFont : consentNotice.font,
                    font && !useSceneFonts ? font : consentNotice.font, menuBackground, menuGolem, session);
                screens[(int)StudyScreen.Title] = mainMenu.Root.gameObject;
            }
            if (victoryStar)
            {
                victoryPanel = new StudyVictoryPanel((RectTransform)transform, font && !useSceneFonts ? font : readyTitle.font, victoryStar);
                screens[(int)StudyScreen.Survey] = victoryPanel.Root.gameObject;
                ratingTitle = victoryPanel.Title;
                ratings = victoryPanel.Ratings;
                skipRating = victoryPanel.Skip;
            }
            foreach (var screen in screens)
                if (screen) screen.SetActive(false);
            if (font && !useSceneFonts)
                foreach (var text in GetComponentsInChildren<Text>(true)) text.font = font;

            Listen(start, session.OpenIntro);
            Listen(enroll, session.Enroll);
            Listen(decline, session.Decline);
            Listen(reopen, session.ReopenConsent);
            Listen(cancelRegistration, session.Withdraw);
            Listen(attempt, session.BeginAttempt);
            Listen(skipRating, () => session.SubmitRating(null));
            Listen(withdraw, session.Withdraw);
            Listen(retry, session.Retry);
            Listen(replay, session.StartNewStudy);

            for (int i = 0; i < ratings.Length; i++)
            {
                int rating = i + 1;
                ratings[i].PreviewChanged += PreviewRating;
                Listen(ratings[i], () =>
                {
                    if (!submittingRating) StartCoroutine(ConfirmRating(rating));
                });
            }
        }

        private void Update()
        {
            if (!session) return;
            bool escape = Keyboard.current?.escapeKey.wasPressedThisFrame == true;
            if (attempt && escape) session.TogglePause();
            if (mainMenu == null) return;
            if (session.Screen == StudyScreen.Title)
            {
                mainMenu.Update();
                if (escape) mainMenu.CloseDetails();
            }
            else if (escape) session.BackToTitle();
        }

        private void OnDestroy() => mainMenu?.Destroy();

        internal void ShowPause(bool paused)
        {
            if (paused)
            {
                pauseMenu ??= new StudyPauseMenu((RectTransform)transform, readyTitle.font, session);
                pauseMenu.Open(session.Deaths);
            }
            else pauseMenu?.Close();
        }

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
                    bool configured = settings.IsConfigured;
                    consentDetails.text = configured
                        ? $"보관 기간  {settings.retention}\n기록 삭제는 종료 화면에서 요청하거나, 참가자 ID와 함께 {settings.contact}에 문의하세요."
                        : Application.isEditor && (string.IsNullOrWhiteSpace(settings.retention) || string.IsNullOrWhiteSpace(settings.contact))
                            ? "BossStudySettings → Protocol에서 Retention(보관 기간)과 Contact(문의처)를 입력하세요."
                            : "아직 참여할 수 없습니다. 담당자에게 문의하세요.";
                    enroll.interactable = configured;
                    break;
                case StudyScreen.Ready:
                    readyTitle.text = session.LostLast ? "보스전 패배" : "보스전 준비";
                    attemptLabel.text = session.LostLast ? "다시 도전" : "도전 시작";
                    break;
                case StudyScreen.Survey:
                    victoryPanel?.Fit();
                    ratingTitle.text = session.Cleared ? "보스전 승리" : "도전 종료";
                    submittingRating = false;
                    foreach (var button in ratings) button.interactable = true;
                    skipRating.interactable = true;
                    PreviewRating();
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
            mainMenu?.ShowBack(screen is StudyScreen.Consent or StudyScreen.Waiting or StudyScreen.Declined);
            if (screen == StudyScreen.Title) mainMenu?.Show();
            if (activeScreen.TryGetComponent(out ScrollRect scroll))
            {
                scroll.StopMovement();
                scroll.verticalNormalizedPosition = 1;
            }
            RefreshStatus();
        }

        private void PreviewRating()
        {
            if (submittingRating) return;
            int hovered = 0, focused = 0;
            for (int i = 0; i < ratings.Length; i++)
            {
                if (ratings[i].Hovered) hovered = i + 1;
                if (ratings[i].Focused) focused = i + 1;
            }
            int preview = hovered > 0 ? hovered : focused;
            for (int i = 0; i < ratings.Length; i++)
                ratings[i].SetStar(i < preview ? .68f : .28f, 1);
        }

        private IEnumerator ConfirmRating(int rating)
        {
            submittingRating = true;
            foreach (var button in ratings) button.interactable = false;
            skipRating.interactable = false;

            const float duration = .32f;
            for (float elapsed = 0; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                float pulse = Mathf.Sin(Mathf.PI * elapsed / duration);
                for (int i = 0; i < ratings.Length; i++)
                {
                    float expansion = i == rating - 1 ? .34f : .17f;
                    ratings[i].SetStar(i < rating ? 1 : .28f, i < rating ? 1 + expansion * pulse : 1);
                }
                yield return null;
            }
            session.SubmitRating(rating);
        }

        internal void RefreshStatus()
        {
            if (session.Screen == StudyScreen.Title) mainMenu?.RefreshPlayLabel();
            if (replay) replay.interactable = session.CanReplay;
            if (!status) return;
            bool showNetwork = session.Screen is StudyScreen.Waiting or StudyScreen.Ready or StudyScreen.Survey or StudyScreen.Debrief
                || (session.Screen == StudyScreen.Withdrawal && session.WithdrawalPending);
            string message = session.SyncStatus;
            if (string.IsNullOrEmpty(message) && session.HasPending) message = "기록 전송 대기 중";
            status.text = message;
            status.gameObject.SetActive(showNetwork && message.Length > 0);
            retry.gameObject.SetActive(showNetwork && message.Length > 0);
        }

        private static void Listen(Button button, UnityAction callback)
        {
            if (button) button.onClick.AddListener(callback);
        }
    }
}
