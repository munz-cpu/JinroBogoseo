using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BossTemplate
{
    internal sealed class StudyMainMenu : MonoBehaviour
    {
        private const float DetailsEntranceDuration = .28f;
        private const float DetailsExitDuration = .22f;

        [Serializable]
        private struct ParallaxLayer
        {
            public Transform target;
            public float distance;
        }

        [SerializeField] private Camera menuCamera;
        [SerializeField] private GameObject scenery, sceneryShade;
        [SerializeField] private SpriteRenderer sky;
        [SerializeField] private ParallaxLayer[] sceneryLayers;
        [SerializeField] private SpriteRenderer golemLayer;
        [SerializeField] private RectTransform navigation, details;
        [SerializeField] private float golemTravel = 12;
        [SerializeField] private Button play, settings, stats, terms, back, backToMenu;
        [SerializeField] private Text playLabel, heading, body, volumeLabel;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private Slider volume;

        private StudySession session;
        private Vector3[] layerOrigins;
        private Vector3 sceneryScale, navigationScale, detailsScale, golemOrigin;
        private Vector2 detailsAnchor, detailsPosition, viewport;
        private float skyWidth, pointerX, travelScale;
        private StudySlideIn navigationSlide;
        private CanvasGroup detailsGroup;
        private GameObject detailsBackdrop;
        private float detailsTransitionStart = float.NegativeInfinity;
        private float detailsFrom, detailsTarget, detailsDuration;
        private bool showingSettings;
        private GameObject previousSelection;
        private StudyVolumeRow[] volumeRows;

        internal void Bind(StudySession owner)
        {
            session = owner;
            layerOrigins = Array.ConvertAll(sceneryLayers, layer => layer.target.localPosition);
            skyWidth = sky.sprite.bounds.size.x * sky.transform.localScale.x;
            sceneryScale = scenery.transform.localScale;
            golemLayer.sortingOrder = ArenaBoss.GiantSortingOrder;
            navigationScale = navigation.localScale;
            navigationSlide = new StudySlideIn((RectTransform)transform, navigation);
            detailsScale = details.localScale;
            detailsAnchor = details.anchorMin;
            detailsPosition = details.anchoredPosition;
            detailsGroup = details.gameObject.AddComponent<CanvasGroup>();
            details.GetComponent<Image>().raycastTarget = true;
            detailsBackdrop = CreateDetailsBackdrop();
            play.onClick.AddListener(session.OpenIntro);
            settings.onClick.AddListener(() => OpenDetails("설정", "", true));
            stats.onClick.AddListener(() => OpenDetails("통계", Stats()));
            terms.onClick.AddListener(() => OpenDetails("실험 약관", Terms()));
            back.onClick.AddListener(CloseDetails);
            backToMenu.onClick.AddListener(session.BackToTitle);
            foreach (var button in new[] { play, settings, stats, terms, back, backToMenu })
                StudyTextHover.Attach(button);
            BindVolumeRows();
        }

        private void BindVolumeRows()
        {
            volumeLabel.rectTransform.anchoredPosition += Vector2.up * 24;
            ((RectTransform)volume.transform).anchoredPosition += Vector2.up * 24;
            volumeRows = new[]
            {
                new StudyVolumeRow(volumeLabel, volume, "전체 음량", StudyAudioChannel.Master),
                CopyVolumeRow("BGM", StudyAudioChannel.Music, 72),
                CopyVolumeRow("효과음", StudyAudioChannel.Effects, 144)
            };
            StudyVolumeRow.SetNavigation(back, volumeRows[2].Slider, volumeRows[0].Slider);
            StudyVolumeRow.SetNavigation(volumeRows[0].Slider, back, volumeRows[1].Slider);
            StudyVolumeRow.SetNavigation(volumeRows[1].Slider, volumeRows[0].Slider, volumeRows[2].Slider);
            StudyVolumeRow.SetNavigation(volumeRows[2].Slider, volumeRows[1].Slider, back);
        }

        private StudyVolumeRow CopyVolumeRow(string title, StudyAudioChannel channel, float offset)
        {
            var label = Instantiate(volumeLabel, volumeLabel.transform.parent, false);
            var slider = Instantiate(volume, volume.transform.parent, false);
            label.gameObject.name = title;
            label.rectTransform.anchoredPosition += Vector2.down * offset;
            ((RectTransform)slider.transform).anchoredPosition += Vector2.down * offset;
            return new StudyVolumeRow(label, slider, title, channel);
        }

        internal void Show()
        {
            RefreshPlayLabel();
            if (details.gameObject.activeSelf) FinishCloseDetails();
            if (EventSystem.current) EventSystem.current.firstSelectedGameObject = play.gameObject;
            navigationSlide.Restart();
            AnimateParallax();
        }

        internal void SetSceneryVisible(bool visible)
        {
            scenery.SetActive(visible);
            golemLayer.gameObject.SetActive(visible);
            if (sceneryShade) sceneryShade.SetActive(visible);
        }

        internal RectTransform ShowBack(bool show)
        {
            backToMenu.gameObject.SetActive(show);
            if (!show) return null;
            backToMenu.transform.SetAsLastSibling();
            return (RectTransform)backToMenu.transform;
        }

        internal void RefreshPlayLabel() => playLabel.text = session.HasContinuation ? "이어하기" : "게임 시작";

        internal void AnimateParallax()
        {
            var screen = ((RectTransform)transform).rect;
            if (screen.width <= 0 || screen.height <= 0) return;
            var mouse = Mouse.current;
            float targetX = mouse == null ? 0 : Mathf.Clamp(1 - mouse.position.ReadValue().x / Mathf.Max(1, Screen.width) * 2, -1, 1);
            pointerX = Mathf.Lerp(pointerX, targetX, 1 - Mathf.Exp(-8 * Time.unscaledDeltaTime));
            float worldPerUi = menuCamera.orthographicSize * 2 / screen.height;
            if (viewport != screen.size)
            {
                viewport = screen.size;
                travelScale = Mathf.Min(1, Mathf.Min(screen.height / 660, screen.width / 1050));
                float figureScale = Mathf.Min(screen.height / 720, screen.width / 1195);
                Bounds sprite = golemLayer.sprite.bounds;
                float scale = 562 * figureScale * worldPerUi / Mathf.Max(sprite.size.x, sprite.size.y);
                golemLayer.transform.localScale = Vector3.one * scale;
                float depth = Vector3.Dot(golemLayer.transform.position - menuCamera.transform.position,
                    menuCamera.transform.forward);
                golemOrigin = menuCamera.ViewportToWorldPoint(new Vector3(.755f, .51f, depth)) - sprite.center * scale;
                float viewWidth = worldPerUi * screen.width;
                float widthScale = Mathf.Max(1, (viewWidth + 1.5f) / (skyWidth * sceneryScale.x));
                scenery.transform.localScale = new Vector3(sceneryScale.x * widthScale, sceneryScale.y, sceneryScale.z);
                navigation.localScale = navigationScale * travelScale;
                float detailScale = Mathf.Min(1, Mathf.Min(screen.height / 500, screen.width / 630));
                details.localScale = detailsScale * detailScale;
                details.anchorMin = details.anchorMax = new Vector2(Mathf.Max(detailsAnchor.x, 272 * detailScale / screen.width), detailsAnchor.y);
            }
            float worldOffset = pointerX * worldPerUi * travelScale;
            for (int i = 0; i < sceneryLayers.Length; i++)
                sceneryLayers[i].target.localPosition = layerOrigins[i] + Vector3.right * (worldOffset * sceneryLayers[i].distance);
            golemLayer.transform.position = golemOrigin + Vector3.right * (worldOffset * golemTravel);
            navigationSlide.Advance();
            AdvanceDetailsTransition();
        }

        internal void CloseDetails()
        {
            if (!details.gameObject.activeSelf || detailsTarget == 0) return;
            detailsGroup.interactable = false;
            StartDetailsTransition(0, DetailsExitDuration);
        }

        private void FinishCloseDetails()
        {
            if (showingSettings) PlayerPrefs.Save();
            details.gameObject.SetActive(false);
            detailsBackdrop.SetActive(false);
            detailsTransitionStart = float.NegativeInfinity;
            SetDetailsPose(1);
            navigation.gameObject.SetActive(true);
            if (EventSystem.current)
                EventSystem.current.SetSelectedGameObject(previousSelection ? previousSelection : play.gameObject);
        }

        private void OnDestroy()
        {
            if (showingSettings && details && details.gameObject.activeSelf)
                PlayerPrefs.Save();
        }

        private void OpenDetails(string title, string text, bool settings = false)
        {
            if (details.gameObject.activeSelf) return;
            previousSelection = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            showingSettings = settings;
            heading.text = title;
            navigation.gameObject.SetActive(false);
            scroll.gameObject.SetActive(!settings);
            foreach (var row in volumeRows) row.SetVisible(settings);
            SetDetailsPose(0);
            detailsGroup.interactable = false;
            StartDetailsTransition(1, DetailsEntranceDuration);
            detailsBackdrop.SetActive(true);
            details.gameObject.SetActive(true);
            if (settings)
            {
                foreach (var row in volumeRows) row.Refresh();
            }
            else
            {
                body.text = text;
                body.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(226, body.preferredHeight + 12));
                scroll.StopMovement();
                scroll.verticalNormalizedPosition = 1;
            }
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(back.gameObject);
        }

        private GameObject CreateDetailsBackdrop()
        {
            var backdrop = new GameObject("상세 메뉴 바깥 클릭 영역", typeof(RectTransform), typeof(Image), typeof(Button));
            backdrop.layer = details.gameObject.layer;
            var rect = (RectTransform)backdrop.transform;
            rect.SetParent(details.parent, false);
            rect.SetSiblingIndex(details.GetSiblingIndex());
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            backdrop.GetComponent<Image>().color = Color.clear;
            var button = backdrop.GetComponent<Button>();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(CloseDetails);
            backdrop.SetActive(false);
            return backdrop;
        }

        private void StartDetailsTransition(float target, float duration)
        {
            detailsFrom = detailsGroup.alpha;
            detailsTarget = target;
            detailsDuration = duration;
            detailsTransitionStart = Time.unscaledTime;
        }

        private void AdvanceDetailsTransition()
        {
            if (!details.gameObject.activeSelf || float.IsNegativeInfinity(detailsTransitionStart)) return;
            float progress = Mathf.Clamp01((Time.unscaledTime - detailsTransitionStart) / detailsDuration);
            float amount = Mathf.SmoothStep(detailsFrom, detailsTarget, progress);
            SetDetailsPose(amount);
            detailsGroup.interactable = detailsTarget > 0 && amount >= .55f;
            if (progress < 1) return;

            detailsTransitionStart = float.NegativeInfinity;
            if (detailsTarget == 0) FinishCloseDetails();
        }

        private void SetDetailsPose(float amount)
        {
            details.anchoredPosition = detailsPosition + Vector2.down * (36 * (1 - amount));
            detailsGroup.alpha = amount;
        }

        private string Stats()
        {
            string totals = $"도전 횟수   {session.Attempts}회\n죽은 횟수   {session.Deaths}회";
            var last = session.LastAttempt;
            if (last == null) return totals + "\n\n아직 완료된 도전 기록이 없습니다.";
            string progress = last.cleared ? "클리어" : last.boss_phase > 0
                ? $"페이즈 {last.boss_phase} · {last.phase_progress_percent:0.#}%" : "기록 없음";
            return $"{totals}\n\n마지막 도전   {progress}\n플레이 타임   {last.duration_seconds:0.#}초\n입은 피해   {last.damage_taken:0.#}";
        }

        private string Terms()
        {
            var protocol = session.Protocol;
            string retention = string.IsNullOrWhiteSpace(protocol.retention) ? "미설정" : protocol.retention;
            string contact = string.IsNullOrWhiteSpace(protocol.contact) ? "미설정" : protocol.contact;
            return $"{protocol.consentNotice}\n\n보관 기간   {retention}\n문의   {contact}";
        }
    }
}
