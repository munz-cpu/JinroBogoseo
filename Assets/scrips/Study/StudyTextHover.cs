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

        private RectTransform label;
        private Vector3 restScale;
        private float elapsed = Duration;
        private float scale = 1, startScale = 1, targetScale = 1;

        internal static void Attach(Transform root)
        {
            if (!root) return;
            foreach (var text in root.GetComponentsInChildren<Text>(true))
            {
                if (text.TryGetComponent<StudyTextHover>(out _)) continue;
                var hover = text.gameObject.AddComponent<StudyTextHover>();
                hover.label = text.rectTransform;
                hover.restScale = hover.label.localScale;
                text.raycastTarget = true;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (eventData is ExtendedPointerEventData pointer && pointer.pointerType != UIPointerType.MouseOrPen) return;
            AnimateTo(1.07f);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (eventData is ExtendedPointerEventData pointer && pointer.pointerType != UIPointerType.MouseOrPen) return;
            AnimateTo(1);
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
            if (elapsed >= Duration) return;
            elapsed = Mathf.Min(Duration, elapsed + Time.unscaledDeltaTime);
            scale = Mathf.Lerp(startScale, targetScale, ScaleCurve.Evaluate(elapsed / Duration));
            label.localScale = restScale * scale;
        }

        private void OnDisable()
        {
            if (label) label.localScale = restScale;
            scale = targetScale = 1;
            elapsed = Duration;
        }
    }
}
