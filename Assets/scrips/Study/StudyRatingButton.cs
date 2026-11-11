using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BossTemplate
{
    internal sealed class StudyRatingButton : Button
    {
        [SerializeField] private Image star;

        internal event Action PreviewChanged;
        internal bool Hovered { get; private set; }
        internal bool Focused { get; private set; }

        internal void SetStar(float opacity, float scale)
        {
            star.color = new Color(1, 1, 1, opacity);
            star.rectTransform.localScale = Vector3.one * scale;
        }

        public override void OnPointerEnter(PointerEventData eventData)
        {
            base.OnPointerEnter(eventData);
            Hovered = true;
            PreviewChanged?.Invoke();
        }

        public override void OnPointerExit(PointerEventData eventData)
        {
            base.OnPointerExit(eventData);
            Hovered = false;
            PreviewChanged?.Invoke();
        }

        public override void OnSelect(BaseEventData eventData)
        {
            base.OnSelect(eventData);
            Focused = eventData is not PointerEventData;
            PreviewChanged?.Invoke();
        }

        public override void OnDeselect(BaseEventData eventData)
        {
            base.OnDeselect(eventData);
            Focused = false;
            PreviewChanged?.Invoke();
        }

        protected override void OnDisable()
        {
            Hovered = Focused = false;
            base.OnDisable();
        }
    }
}
