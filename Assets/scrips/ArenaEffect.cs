using UnityEngine;
namespace BossTemplate
{
    public sealed class ArenaEffect : MonoBehaviour
    {
        private float expires;
        private SpriteRenderer surface;
        private Sprite defaultSprite;
        private Vector2 velocity;
        private Vector3 initialScale;
        private Color initialColor;
        private float started, duration, growth;
        private bool animated;
        private MeshRenderer meshRenderer;
        private MeshFilter meshFilter;
        private MaterialPropertyBlock tint;
        private void Cache()
        {
            if (surface) return;
            surface = GetComponent<SpriteRenderer>(); defaultSprite = surface.sprite;
            meshRenderer = GetComponentInChildren<MeshRenderer>(true); meshFilter = GetComponentInChildren<MeshFilter>(true);
            tint = new MaterialPropertyBlock();
        }
        public void Show(Vector2 position, Vector2 size, Color color, float lifetime)
        {
            Cache(); animated = false;
            surface.sprite = defaultSprite;
            surface.enabled = true;
            if (meshRenderer) meshRenderer.enabled = false;
            transform.rotation = Quaternion.identity;
            transform.position = position; transform.localScale = new Vector3(size.x,size.y,1);
            surface.color = color;
            expires = Time.time + lifetime; gameObject.SetActive(true);
        }
        public void ShowAnimated(Mesh mesh, Vector2 position, Vector2 size, Color color,
            float lifetime, Vector2 speed, float endScale, float angle)
        {
            Show(position, size, color, lifetime);
            surface.enabled = false; meshFilter.sharedMesh = mesh; meshRenderer.enabled = true; animated = true;
            tint.SetColor("_BaseColor", color); meshRenderer.SetPropertyBlock(tint);
            velocity = speed; initialScale = transform.localScale; initialColor = color;
            started = Time.time; duration = lifetime; growth = endScale;
            transform.rotation = Quaternion.Euler(0, 0, angle);
        }
        private void Update()
        {
            if (Time.time >= expires) { gameObject.SetActive(false); return; }
            if (!animated) return;
            float t = Mathf.Clamp01((Time.time - started) / duration);
            transform.position += (Vector3)(velocity * Time.deltaTime);
            transform.localScale = initialScale * Mathf.Lerp(1, growth, t);
            tint.SetColor("_BaseColor", new Color(initialColor.r, initialColor.g, initialColor.b, initialColor.a * (1 - t)));
            meshRenderer.SetPropertyBlock(tint);
        }
    }
}

