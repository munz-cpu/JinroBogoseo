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
        [SerializeField] private Font pauseDisplayFont;
        [SerializeField] private Button attempt;
        [SerializeField] private Text ratingTitle, debrief, participant, contact, withdrawalMessage;
        [SerializeField] private Button copyContact;
        [SerializeField] private StudyMainMenu mainMenu;
        [SerializeField] private RectTransform consentContent, waitingContent, declinedContent;
        [SerializeField] private RectTransform surveyContent, debriefContent;
        [SerializeField] private GameObject studyBackdrop;
        [SerializeField] private GameObject debriefScenery;
        [SerializeField] private ScrollRect debriefScroll;
        [SerializeField] private Sprite victoryStar;
        [SerializeField] private Texture victoryShade;
        [SerializeField] private StudyRatingButton[] ratings;
        [SerializeField] private Button skipRating, retry, replay;
        [SerializeField] private Text status;
        [SerializeField] private bool useSceneFonts;

        private StudySession session;
        private GameObject activeScreen;
        private bool submittingRating;
        private StudyPauseMenu pauseMenu;
        private StudyVictoryPanel victoryPanel;
        private StudySlideIn screenSlide;
        private float contactFeedbackUntil;
        internal bool HasVictoryPanel => victoryPanel != null;

        internal void Bind(StudySession owner, Font font)
        {
            session = owner;
            if (mainMenu) mainMenu.Bind(owner);
            if (victoryStar)
            {
                var reviewFont = pauseDisplayFont ? pauseDisplayFont : font && !useSceneFonts ? font : readyTitle.font;
                var authoredSurvey = screens[(int)StudyScreen.Survey];
                if (authoredSurvey) authoredSurvey.SetActive(false);
                victoryPanel = new StudyVictoryPanel((RectTransform)transform, reviewFont, victoryStar, victoryShade);
                screens[(int)StudyScreen.Survey] = victoryPanel.Root.gameObject;
                surveyContent = victoryPanel.Content;
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
            Listen(retry, session.Retry);
            Listen(replay, session.StartNewStudy);
            Listen(copyContact, CopyContact);

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
            screenSlide?.Advance();
            if (contactFeedbackUntil > 0 && Time.unscaledTime >= contactFeedbackUntil)
            {
                contactFeedbackUntil = 0;
                contact.text = $"문의  {session.Protocol.contact}";
            }
            var keyboard = Keyboard.current;
            bool escape = keyboard?.escapeKey.wasPressedThisFrame == true;
            if (attempt && escape) session.TogglePause();
            else if (session.Screen == StudyScreen.Defeat && keyboard != null
                && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
                session.BeginAttempt();
            pauseMenu?.Advance();
            if (mainMenu == null) return;
            if (session.Screen is StudyScreen.Title or StudyScreen.Consent or StudyScreen.Waiting or StudyScreen.Declined)
                mainMenu.AnimateParallax();
            if (session.Screen == StudyScreen.Title)
            {
                if (escape) mainMenu.CloseDetails();
            }
            else if (escape) session.BackToTitle();
        }

        internal void ShowPause(bool paused)
        {
            if (paused)
            {
                pauseMenu ??= new StudyPauseMenu((RectTransform)transform,
                    pauseDisplayFont ? pauseDisplayFont : readyTitle.font, readyTitle.font, session);
                pauseMenu.Open(session.Deaths);
            }
            else pauseMenu?.Close();
        }

        internal void Render(StudyScreen screen)
        {
            screenSlide?.Finish();
            screenSlide = null;
            if (activeScreen) activeScreen.SetActive(false);
            bool participation = screen is StudyScreen.Consent or StudyScreen.Waiting or StudyScreen.Declined;
            bool showMenuScenery = mainMenu && (screen == StudyScreen.Title || participation);
            mainMenu?.SetSceneryVisible(showMenuScenery);
            bool showDebriefScenery = debriefScenery && (screen is StudyScreen.Debrief or StudyScreen.Survey);
            if (debriefScenery) debriefScenery.SetActive(showDebriefScenery);
            if (studyBackdrop) studyBackdrop.SetActive(screen != StudyScreen.Title && !showMenuScenery && !showDebriefScenery);
            activeScreen = screens[(int)(screen == StudyScreen.Defeat ? StudyScreen.Combat : screen)];
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
                        ? $"보관 기간  {settings.retention}\n기록 제외를 원하면 참가자 ID와 함께 {settings.contact}에 문의하세요."
                        : Application.isEditor && (string.IsNullOrWhiteSpace(settings.retention) || string.IsNullOrWhiteSpace(settings.contact))
                            ? "BossStudySettings → Protocol에서 Retention(보관 기간)과 Contact(문의처)를 입력하세요."
                            : "아직 참여할 수 없습니다. 담당자에게 문의하세요.";
                    enroll.interactable = configured;
                    break;
                case StudyScreen.Ready:
                    readyTitle.text = "보스전 준비";
                    attemptLabel.text = "도전 시작";
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
                    debrief.text = session.Protocol.DebriefText;
                    participant.text = $"참가자 ID  {session.ParticipantId}";
                    contact.text = $"문의  {session.Protocol.contact}";
                    contactFeedbackUntil = 0;
                    if (copyContact) copyContact.interactable = !string.IsNullOrWhiteSpace(session.Protocol.contact);
                    break;
                case StudyScreen.Withdrawal:
                    withdrawalMessage.text = session.WithdrawalPending
                        ? "인터넷에 연결한 뒤 요청이 접수될 때까지 기다려 주세요."
                        : "기록 제외 요청을 접수했습니다.";
                    break;
            }

            activeScreen.SetActive(true);
            var back = mainMenu?.ShowBack(participation);
            if (screen == StudyScreen.Title) mainMenu?.Show();
            var slideContent = screen switch
            {
                StudyScreen.Consent => consentContent,
                StudyScreen.Waiting => waitingContent,
                StudyScreen.Declined => declinedContent,
                StudyScreen.Survey => surveyContent,
                StudyScreen.Debrief => debriefContent,
                _ => null
            };
            if (slideContent)
            {
                float playbackSpeed = screen is StudyScreen.Survey or StudyScreen.Debrief ? StudySlideIn.FastPlayback : 1f;
                screenSlide = new StudySlideIn((RectTransform)transform, slideContent, back, playbackSpeed);
                screenSlide.Restart();
                screenSlide.Advance();
            }
            var scroll = screen == StudyScreen.Debrief && debriefScroll ? debriefScroll : activeScreen.GetComponent<ScrollRect>();
            if (scroll)
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
            bool showNetwork = session.Screen == StudyScreen.Waiting
                || (session.Screen == StudyScreen.Withdrawal && session.WithdrawalPending);
            string message = session.SyncStatus;
            bool visible = showNetwork && message.Length > 0;
            if (status)
            {
                status.text = visible ? message : "";
                status.gameObject.SetActive(visible);
            }
            if (retry) retry.gameObject.SetActive(visible);
        }

        private void CopyContact()
        {
            string address = session.Protocol.contact;
            if (string.IsNullOrWhiteSpace(address)) return;
            GUIUtility.systemCopyBuffer = address;
            contact.text = "문의  복사됨";
            contactFeedbackUntil = Time.unscaledTime + 1.5f;
        }

        private static void Listen(Button button, UnityAction callback)
        {
            if (!button) return;
            button.onClick.AddListener(callback);
            StudyTextHover.Attach(button);
        }
    }
}
