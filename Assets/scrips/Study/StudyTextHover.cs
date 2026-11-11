using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BossTemplate
{
    internal sealed class StudyTextHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const float Duration = .22f;
        private static readonly AnimationCurve ScaleCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        private Button button;
        private (RectTransform rect, Vector3 restScale)[] labels;
        private bool pointerOver;
        private float elapsed = Duration;
        private float scale = 1, startScale = 1, targetScale = 1;

        internal static void Attach(Button button)
        {
            if (button.TryGetComponent<StudyTextHover>(out _)) return;
            var text = button.GetComponentsInChildren<Text>(true);
            if (text.Length == 0) return;

            var hover = button.gameObject.AddComponent<StudyTextHover>();
            hover.button = button;
            hover.labels = Array.ConvertAll(text, label => (label.rectTransform, label.rectTransform.localScale));
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (eventData is ExtendedPointerEventData pointer && pointer.pointerType != UIPointerType.MouseOrPen) return;
            pointerOver = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (eventData is ExtendedPointerEventData pointer && pointer.pointerType != UIPointerType.MouseOrPen) return;
            pointerOver = false;
        }

        private void AnimateTo(float target)
        {
            if (target == targetScale) return;
            startScale = scale;
            targetScale = target;
            elapsed = 0;
        }

        private void Update()
        {
            AnimateTo(pointerOver && button.isActiveAndEnabled && button.IsInteractable() ? 1.07f : 1);
            if (elapsed >= Duration) return;
            elapsed = Mathf.Min(Duration, elapsed + Time.unscaledDeltaTime);
            scale = Mathf.Lerp(startScale, targetScale, ScaleCurve.Evaluate(elapsed / Duration));
            ApplyScale();
        }

        private void OnDisable()
        {
            pointerOver = false;
            scale = targetScale = 1;
            elapsed = Duration;
            ApplyScale();
        }

        private void ApplyScale()
        {
            if (labels == null) return;
            foreach (var (rect, restScale) in labels)
                if (rect) rect.localScale = restScale * scale;
        }
    }
}
