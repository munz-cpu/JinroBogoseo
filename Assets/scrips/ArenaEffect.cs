using UnityEngine;
namespace BossTemplate
{
    public sealed class ArenaEffect : MonoBehaviour
    {
        private float expires;
        public void Show(Vector2 position, Vector2 size, Color color, float lifetime)
        {
            transform.position = position; transform.localScale = new Vector3(size.x,size.y,1);
            GetComponent<SpriteRenderer>().color = color;
            expires = Time.time + lifetime; gameObject.SetActive(true);
        }
        private void Update() { if (Time.time >= expires) gameObject.SetActive(false); }
    }
}
