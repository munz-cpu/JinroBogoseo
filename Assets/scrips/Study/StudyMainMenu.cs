using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BossTemplate
{
    internal sealed class StudyMainMenu : MonoBehaviour
    {
        [Serializable]
        private struct ParallaxLayer
        {
            public Transform target;
            public float distance;
        }

        [SerializeField] private Camera menuCamera;
        [SerializeField] private GameObject scenery;
        [SerializeField] private SpriteRenderer sky;
        [SerializeField] private ParallaxLayer[] sceneryLayers;
        [SerializeField] private RectTransform golemLayer, navigation, details;
        [SerializeField] private float golemTravel = 60;
        [SerializeField] private Button play, settings, stats, terms, back, backToMenu;
        [SerializeField] private Text playLabel, heading, body, volumeLabel;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private Slider volume;

        private StudySession session;
        private Vector3[] layerOrigins;
        private Vector3 sceneryScale, golemScale, navigationScale, detailsScale;
        private Vector2 golemOrigin, detailsAnchor, viewport;
        private float skyWidth, pointerX, travelScale;
        private bool showingSettings;
        private GameObject previousSelection;

        internal void Bind(StudySession owner)
        {
            session = owner;
            layerOrigins = Array.ConvertAll(sceneryLayers, layer => layer.target.localPosition);
            skyWidth = sky.sprite.bounds.size.x * sky.transform.localScale.x;
            sceneryScale = scenery.transform.localScale;
            golemOrigin = golemLayer.anchoredPosition;
            golemScale = golemLayer.localScale;
            navigationScale = navigation.localScale;
            detailsScale = details.localScale;
            detailsAnchor = details.anchorMin;

            play.onClick.AddListener(session.OpenIntro);
            settings.onClick.AddListener(() => OpenDetails("설정", "", true));
            stats.onClick.AddListener(() => OpenDetails("통계", Stats()));
            terms.onClick.AddListener(() => OpenDetails("실험 약관", Terms()));
            back.onClick.AddListener(CloseDetails);
            backToMenu.onClick.AddListener(session.BackToTitle);
            volume.onValueChanged.AddListener(value =>
            {
                AudioListener.volume = value;
                UpdateVolumeLabel(value);
            });
        }

        internal void Show()
        {
            RefreshPlayLabel();
            CloseDetails();
            if (EventSystem.current) EventSystem.current.firstSelectedGameObject = play.gameObject;
            AnimateParallax();
        }

        internal void SetSceneryVisible(bool visible) => scenery.SetActive(visible);

        internal void ShowBack(bool show)
        {
            backToMenu.gameObject.SetActive(show);
            if (show) backToMenu.transform.SetAsLastSibling();
        }

        internal void RefreshPlayLabel() => playLabel.text = session.HasContinuation ? "이어하기" : "게임 시작";

        internal void AnimateParallax()
        {
            var screen = ((RectTransform)transform).rect;
            if (screen.width <= 0 || screen.height <= 0) return;
            var mouse = Mouse.current;
            float targetX = mouse == null ? 0 : Mathf.Clamp(mouse.position.ReadValue().x / Mathf.Max(1, Screen.width) * 2 - 1, -1, 1);
            pointerX = Mathf.Lerp(pointerX, targetX, 1 - Mathf.Exp(-8 * Time.unscaledDeltaTime));
            if (viewport != screen.size)
            {
                viewport = screen.size;
                travelScale = Mathf.Min(1, Mathf.Min(screen.height / 660, screen.width / 1050));
                float figureScale = Mathf.Min(screen.height / 720, screen.width / 1195);
                golemLayer.localScale = golemScale * figureScale;
                float viewWidth = menuCamera.orthographicSize * 2 * screen.width / screen.height;
                float widthScale = Mathf.Max(1, (viewWidth + 1.5f) / (skyWidth * sceneryScale.x));
                scenery.transform.localScale = new Vector3(sceneryScale.x * widthScale, sceneryScale.y, sceneryScale.z);
                navigation.localScale = navigationScale * travelScale;
                float detailScale = Mathf.Min(1, Mathf.Min(screen.height / 500, screen.width / 630));
                details.localScale = detailsScale * detailScale;
                details.anchorMin = details.anchorMax = new Vector2(Mathf.Max(detailsAnchor.x, 272 * detailScale / screen.width), detailsAnchor.y);
            }
            float worldOffset = pointerX * menuCamera.orthographicSize * 2 / screen.height * travelScale;
            for (int i = 0; i < sceneryLayers.Length; i++)
                sceneryLayers[i].target.localPosition = layerOrigins[i] + Vector3.right * (worldOffset * sceneryLayers[i].distance);
            golemLayer.anchoredPosition = golemOrigin + Vector2.right * (pointerX * golemTravel * travelScale);
        }

        internal void CloseDetails()
        {
            if (!details.gameObject.activeSelf) return;
            if (showingSettings) StudyPauseMenu.SaveVolume(volume.value);
            details.gameObject.SetActive(false);
            navigation.gameObject.SetActive(true);
            if (EventSystem.current)
                EventSystem.current.SetSelectedGameObject(previousSelection ? previousSelection : play.gameObject);
        }

        private void OnDestroy()
        {
            if (showingSettings && volume && details && details.gameObject.activeSelf)
                StudyPauseMenu.SaveVolume(volume.value);
        }

        private void OpenDetails(string title, string text, bool settings = false)
        {
            previousSelection = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            showingSettings = settings;
            heading.text = title;
            navigation.gameObject.SetActive(false);
            scroll.gameObject.SetActive(!settings);
            volume.gameObject.SetActive(settings);
            volumeLabel.gameObject.SetActive(settings);
            details.gameObject.SetActive(true);
            if (settings)
            {
                volume.SetValueWithoutNotify(AudioListener.volume);
                UpdateVolumeLabel(volume.value);
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

        private void UpdateVolumeLabel(float value) => volumeLabel.text = $"전체 음량   {Mathf.RoundToInt(value * 100)}%";

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
