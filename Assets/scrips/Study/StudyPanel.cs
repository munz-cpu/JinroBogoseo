using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BossTemplate
{
    internal sealed class StudyPanel
    {
        private static readonly Color Surface = new Color(.05f, .08f, .12f, .98f);
        private static readonly Color Accent = new Color(.16f, .32f, .45f);
        private static readonly Color Selected = new Color(.22f, .5f, .39f);
        private static readonly string[] AgeIds = { "under_18", "18_24", "25_34", "35_44", "45_plus", "undisclosed" };
        private static readonly string[] Ages = { "18세 미만", "18–24세", "25–34세", "35–44세", "45세 이상", "응답 안 함" };
        private static readonly string[] FrequencyIds = { "rarely", "monthly", "weekly", "daily", "undisclosed" };
        private static readonly string[] Frequencies = { "거의 안 함", "월 단위", "주 단위", "거의 매일", "응답 안 함" };
        private static readonly string[] Ratings = { "1", "2", "3", "4", "5" };

        private readonly StudySession session;
        private readonly Font font;
        private readonly GameObject canvas;
        private readonly GameObject events;
        private readonly int[] scores = new int[6];
        private RectTransform view, content, combatButton;
        private Text status;
        private Button enroll, submit, retry;
        private int age = -1, frequency = -1;

        internal StudyPanel(StudySession session, Font font)
        {
            this.session = session;
            this.font = font ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            canvas = new GameObject("연구 참가 화면", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.transform.SetParent(session.transform, false);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.GetComponent<Canvas>().sortingOrder = 100;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0;
            events = new GameObject("연구 화면 입력", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(session.transform, false);
        }

        internal bool ConsumesShot(Vector2 pointer) =>
            combatButton && RectTransformUtility.RectangleContainsScreenPoint(combatButton, pointer);

        internal void Render(StudyScreen screen)
        {
            if (view)
            {
                view.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(view.gameObject);
            }
            view = Rect("연구 단계", canvas.transform);
            content = combatButton = null;
            status = null;
            enroll = submit = retry = null;

            if (screen == StudyScreen.Combat)
            {
                view.anchorMin = view.anchorMax = new Vector2(1, 0);
                view.pivot = new Vector2(1, 0);
                view.anchoredPosition = new Vector2(-22, 20);
                view.sizeDelta = new Vector2(174, 46);
                var button = Action(view, "참여 종료", session.StopStudy);
                combatButton = (RectTransform)button.transform;
                Stretch(combatButton);
                return;
            }

            BuildScrollablePage();
            switch (screen)
            {
                case StudyScreen.Consent:
                    Consent();
                    break;
                case StudyScreen.Waiting:
                    Heading("참가자 등록");
                    Label(content, "서버에 연결하여 등록을 완료하는 중입니다. 최초 등록에는 연결이 필요합니다.");
                    NetworkControls();
                    Action(content, "등록 중단 및 데이터 제외 요청", session.Withdraw);
                    break;
                case StudyScreen.Ready:
                    Heading(session.LostLast ? "보스전 패배" : "보스전 준비");
                    Label(content, "A/D 또는 방향키로 이동, W·↑·Space로 점프, Shift로 회피, Enter 또는 마우스 왼쪽으로 사격합니다.");
                    Action(content, session.LostLast ? "다시 도전" : "도전 시작", session.BeginAttempt);
                    Action(content, "참여 종료 및 설문으로", session.StopStudy);
                    NetworkControls();
                    break;
                case StudyScreen.Survey:
                    Survey();
                    break;
                case StudyScreen.Debrief:
                    Heading("연구 사후 안내");
                    Label(content, session.Protocol.debrief);
                    Label(content, $"참가자 ID  {session.ParticipantId}");
                    Label(content, $"데이터 제외 및 문의  {session.Protocol.contact}");
                    NetworkControls();
                    Action(content, "내 데이터를 연구에서 제외 요청", session.Withdraw);
                    break;
                case StudyScreen.Declined:
                    Heading("참여하지 않음");
                    Label(content, "연구 참여를 종료했습니다. 창을 닫아 주세요.");
                    break;
                case StudyScreen.Withdrawal:
                    Heading("연구 데이터 제외");
                    Label(content, session.WithdrawalPending
                        ? "제외 요청이 대기 중입니다. 인터넷에 연결하고 접수될 때까지 이 화면을 열어 두세요."
                        : "서버에서 제외 요청을 접수했습니다. 분석용 시트는 다음 동기화에서 갱신됩니다.");
                    if (session.WithdrawalPending) NetworkControls();
                    break;
                case StudyScreen.Error:
                    Heading("연구를 계속할 수 없습니다");
                    Label(content, "게임을 다시 실행하세요. 문제가 계속되면 연구 담당자에게 문의하세요.");
                    break;
            }
        }

        private void Consent()
        {
            Heading("보스전 연구 참여 안내");
            var settings = session.Protocol;
            Label(content, settings.consentNotice);
            if (settings.IsConfigured)
            {
                Label(content, $"데이터 보관 기간  {settings.retention}\n삭제·제외 요청  실험 종료 후 화면의 제외 요청 버튼을 사용하거나 참가자 ID와 함께 {settings.contact}에 문의하세요.");
                Choices("연령대", Ages, age, index => { age = index; CheckEnrollment(); });
                Choices("평소 게임 플레이 빈도", Frequencies, frequency, index => { frequency = index; CheckEnrollment(); });
                enroll = Action(content, "안내를 읽었으며 연구 참여에 동의합니다", () => session.Enroll(AgeIds[age], FrequencyIds[frequency]));
                CheckEnrollment();
            }
            else Label(content, "연구 설정이 완료되면 참여할 수 있습니다. 담당자에게 문의하세요.");
            Action(content, "동의하지 않음", session.Decline);
        }

        private void CheckEnrollment()
        {
            if (enroll) enroll.interactable = age >= 0 && frequency >= 0;
        }

        private void Survey()
        {
            Heading(session.Cleared ? "보스전 승리 · 최종 설문" : "도전 종료 · 최종 설문");
            Label(content, "1점은 전혀 그렇지 않다, 3점은 보통이다, 5점은 매우 그렇다입니다. 화면 아래에서 응답을 제출하거나 건너뛸 수 있습니다.");
            var questions = session.Protocol.Questions(session.Cleared);
            for (int i = 0; i < questions.Length; i++)
            {
                int question = i;
                Choices(questions[i], Ratings, scores[i] - 1, answer =>
                {
                    scores[question] = answer + 1;
                    CheckSurvey();
                });
            }
            submit = Action(content, "응답 제출", () => session.SubmitSurvey(scores, false));
            CheckSurvey();
            Action(content, "응답하지 않고 사후 안내 보기", () => session.SubmitSurvey(scores, true));
            NetworkControls();
        }

        private void CheckSurvey()
        {
            if (submit) submit.interactable = Array.TrueForAll(scores, score => score > 0);
        }

        private void NetworkControls()
        {
            status = Label(content, "");
            retry = Action(content, "지금 다시 연결", session.Retry);
            RefreshStatus();
        }

        internal void RefreshStatus()
        {
            if (!status) return;
            string message = session.SyncStatus;
            if (string.IsNullOrEmpty(message) && session.HasPending) message = "서버로 보낼 기록이 대기 중입니다.";
            status.text = message;
            status.gameObject.SetActive(message.Length > 0);
            retry.gameObject.SetActive(message.Length > 0);
        }

        private void BuildScrollablePage()
        {
            Stretch(view);
            view.gameObject.AddComponent<Image>().color = Surface;
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 35;
            var viewport = Rect("스크롤 영역", view);
            Stretch(viewport);
            viewport.offsetMin = new Vector2(48, 24);
            viewport.offsetMax = new Vector2(-48, -24);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Rect("연구 안내와 문항", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 9;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
        }

        private void Choices(string title, string[] choices, int current, Action<int> select)
        {
            Label(content, title, 21);
            var row = Rect(title, content);
            var group = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 8;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = true;
            var buttons = new Button[choices.Length];
            for (int i = 0; i < choices.Length; i++)
            {
                int index = i;
                buttons[i] = Action(row, choices[i], () =>
                {
                    for (int j = 0; j < buttons.Length; j++) buttons[j].image.color = j == index ? Selected : Accent;
                    select(index);
                });
                buttons[i].image.color = i == current ? Selected : Accent;
            }
        }

        private void Heading(string value) => Label(content, value, 34);

        private Text Label(Transform parent, string value, int size = 20)
        {
            var text = Rect("텍스트", parent).gameObject.AddComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private Button Action(Transform parent, string name, Action click)
        {
            var rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Accent;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => click());
            rect.gameObject.AddComponent<LayoutElement>().minHeight = 40;
            var label = Label(rect, name, 18);
            label.alignment = TextAnchor.MiddleCenter;
            Stretch(label.rectTransform);
            return button;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        internal void Dispose()
        {
            if (canvas) UnityEngine.Object.Destroy(canvas);
            if (events) UnityEngine.Object.Destroy(events);
        }
    }
}
