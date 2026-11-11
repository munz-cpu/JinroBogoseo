using System;
using UnityEngine;

namespace BossTemplate
{
    internal sealed class StudySlideIn
    {
        private const float Duration = .48f;
        private const float Stagger = .085f;
        private const float DurationPerRow = .07f;
        private readonly RectTransform viewport, group;
        private readonly (RectTransform rect, Vector2 origin)[] items;
        private readonly float rightEdge, endTime;
        private float elapsed = float.PositiveInfinity;

        internal StudySlideIn(RectTransform viewport, RectTransform group, RectTransform trailingItem = null)
        {
            this.viewport = viewport;
            this.group = group;
            int childCount = group.childCount;
            items = new (RectTransform, Vector2)[childCount + (trailingItem ? 1 : 0)];
            for (int i = 0; i < childCount; i++)
            {
                var item = (RectTransform)group.GetChild(i);
                items[i] = (item, item.anchoredPosition);
            }
            Array.Sort(items, 0, childCount, System.Collections.Generic.Comparer<(RectTransform rect, Vector2 origin)>
                .Create((a, b) => b.rect.localPosition.y.CompareTo(a.rect.localPosition.y)));
            if (trailingItem) items[childCount] = (trailingItem, trailingItem.anchoredPosition);
            foreach (var (item, _) in items)
            {
                Vector3 right = item.TransformPoint(new Vector3(item.rect.xMax, 0, 0));
                rightEdge = Mathf.Max(rightEdge, group.InverseTransformPoint(right).x);
            }
            endTime = Duration + (items.Length - 1) * (Stagger + DurationPerRow);
        }

        internal void Restart() => elapsed = 0;

        internal void Advance()
        {
            if (elapsed >= endTime)
            {
                if (elapsed == endTime) Finish();
                return;
            }

            Rect screen = viewport.rect;
            if (screen.width <= 0) return;
            Vector3 left = viewport.TransformPoint(new Vector3(screen.xMin, screen.center.y, 0));
            float startOffset = group.InverseTransformPoint(left).x - rightEdge - 24;
            Vector3 travel = group.TransformVector(Vector3.right * startOffset);
            for (int i = 0; i < items.Length; i++)
            {
                var (item, origin) = items[i];
                float duration = Duration + i * DurationPerRow;
                float remaining = 1 - Mathf.Clamp01((elapsed - i * Stagger) / duration);
                Vector2 offset = item.parent.InverseTransformVector(travel);
                item.anchoredPosition = origin + offset * (remaining * remaining * remaining);
            }
            elapsed = Mathf.Min(endTime, elapsed + Time.unscaledDeltaTime);
        }

        internal void Finish()
        {
            foreach (var (item, origin) in items) item.anchoredPosition = origin;
            elapsed = float.PositiveInfinity;
        }
    }
}
