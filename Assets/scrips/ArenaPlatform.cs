using UnityEngine;

namespace BossTemplate
{
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class ArenaPlatform : MonoBehaviour
    {
        private SpriteRenderer surface;
        public Bounds Bounds
        {
            get
            {
                if (!surface) surface = GetComponent<SpriteRenderer>();
                return surface.bounds;
            }
        }

        public bool TryLand(Vector2 previous, Vector2 next, Vector2 halfSize, out float centerY)
        {
            Bounds bounds = Bounds;
            centerY = bounds.max.y + halfSize.y;
            float fromFeet = previous.y - halfSize.y;
            float toFeet = next.y - halfSize.y;
            // 아래에서 통과하고, 내려오며 윗면을 가로지를 때만 착지합니다.
            if (!isActiveAndEnabled || next.y >= previous.y || fromFeet < bounds.max.y - .01f || toFeet > bounds.max.y)
                return false;
            float crossing = Mathf.Clamp01((fromFeet - bounds.max.y) / (fromFeet - toFeet));
            float crossingX = Mathf.Lerp(previous.x, next.x, crossing);
            return crossingX + halfSize.x > bounds.min.x && crossingX - halfSize.x < bounds.max.x
                && next.x + halfSize.x > bounds.min.x && next.x - halfSize.x < bounds.max.x;
        }
    }
}
