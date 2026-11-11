using UnityEngine;
using UnityEngine.UI;

namespace BossTemplate
{
    [DisallowMultipleComponent]
    internal sealed class RoundedUi : BaseMeshEffect
    {
        [SerializeField, Min(0)] private float radius = 12;
        [SerializeField, Min(0)] private float borderWidth = 1.25f;

        private const int CornerSegments = 8;
        private const int ContourVertices = 4 * (CornerSegments + 1);

        internal static void Apply(Image image, float radius, float borderWidth)
        {
            var shape = image.gameObject.AddComponent<RoundedUi>();
            shape.radius = radius;
            shape.borderWidth = borderWidth;
            image.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || mesh.currentVertCount == 0) return;
            Rect rect = graphic.GetPixelAdjustedRect();
            mesh.Clear();
            float halfShortSide = Mathf.Min(rect.width, rect.height) * .5f;
            if (halfShortSide <= 0) return;

            float corner = Mathf.Min(radius, halfShortSide);
            float fringe = Mathf.Min(.65f, halfShortSide * .25f);
            float stroke = Mathf.Min(borderWidth, halfShortSide - fringe * 2);
            Color fill = graphic.color;
            Color edge = fill;
            if (stroke > 0)
            {
                edge = fill.grayscale < .5f
                    ? Color.Lerp(fill, Color.white, .24f)
                    : Color.Lerp(fill, Color.black, .25f);
                edge.a = fill.a;
            }
            Color transparentEdge = edge;
            transparentEdge.a = 0;
            AddContour(mesh, rect, corner, 0, transparentEdge);
            AddContour(mesh, rect, corner, fringe, edge);
            if (stroke > 0)
            {
                AddContour(mesh, rect, corner, fringe + stroke, edge);
                AddContour(mesh, rect, corner, fringe * 2 + stroke, fill);
            }

            int center = mesh.currentVertCount;
            mesh.AddVert(rect.center, fill, Vector2.zero);
            int inner = center - ContourVertices;
            for (int i = 0; i < ContourVertices; i++)
                mesh.AddTriangle(center, inner + (i + 1) % ContourVertices, inner + i);
        }

        private static void AddContour(VertexHelper mesh, Rect rect, float radius, float inset, Color color)
        {
            int start = mesh.currentVertCount;
            float cornerRadius = Mathf.Max(0, radius - inset);
            float offset = inset + cornerRadius;
            for (int corner = 0; corner < 4; corner++)
            {
                float x = corner is 0 or 3 ? rect.xMax - offset : rect.xMin + offset;
                float y = corner < 2 ? rect.yMax - offset : rect.yMin + offset;
                var center = new Vector2(x, y);
                for (int segment = 0; segment <= CornerSegments; segment++)
                {
                    float angle = (corner + (float)segment / CornerSegments) * Mathf.PI * .5f;
                    var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    mesh.AddVert(center + direction * cornerRadius, color, Vector2.zero);
                }
            }

            if (start == 0) return;
            int outer = start - ContourVertices;
            for (int i = 0; i < ContourVertices; i++)
            {
                int next = (i + 1) % ContourVertices;
                mesh.AddTriangle(outer + i, start + i, outer + next);
                mesh.AddTriangle(outer + next, start + i, start + next);
            }
        }
    }
}
