using UnityEngine;

namespace BossTemplate
{
    // 상체와 하체를 같은 캔버스 좌표로 합성하여 이동과 사격을 독립적으로 재생합니다.
    public sealed class PlayerSpriteAnimation : MonoBehaviour
    {
        [SerializeField] private Sprite[] idle, run, jump, death, upperBody, idleLegs, runLegs, jumpLegs;
        [SerializeField] private SpriteRenderer torso, legs;
        [SerializeField] private Sprite firingUpperBody;
        [SerializeField] private Sprite crouchIdle, crouchFire;
        private bool crouching;
        private SpriteRenderer body;
        private float axis, vertical, firedUntil, recoilUntil, stateTime;
        private bool grounded = true, dead;
        private int facing = 1;

        private void Awake() { body = GetComponent<SpriteRenderer>(); }
        public void ResetState()
        {
            if (!body) body = GetComponent<SpriteRenderer>();
            firedUntil = recoilUntil = 0; stateTime = Time.time;
            dead = false; grounded = true; axis = vertical = 0; facing = 1;
            crouching = false;
            body.enabled = true; torso.enabled = legs.enabled = false;
            body.sprite = idle[0];
        }
        public void SetState(float movement, bool onGround, float velocity, int direction, bool isDead)
        {
            if (dead != isDead || grounded != onGround || (axis == 0) != (movement == 0)) stateTime = Time.time;
            axis = movement; grounded = onGround; vertical = velocity; facing = direction; dead = isDead;
        }
        public void Fire() { firedUntil = Time.time + .22f; recoilUntil = Time.time + .07f; }
        public void SetCrouching(bool value) { crouching = value; }
        private Sprite Frame(Sprite[] frames, float fps, bool loop = true)
        {
            int index = Mathf.FloorToInt((Time.time - stateTime) * fps);
            return frames[loop ? index % frames.Length : Mathf.Min(index, frames.Length - 1)];
        }
        private void LateUpdate()
        {
            bool shooting = !dead && Time.time < firedUntil;
            if (crouching && grounded && !dead)
            {
                body.enabled = true; torso.enabled = legs.enabled = false;
                body.flipX = facing < 0;
                body.sprite = Time.time < recoilUntil ? crouchFire : crouchIdle;
                return;
            }
            body.enabled = !shooting;
            torso.enabled = legs.enabled = shooting;
            body.flipX = torso.flipX = legs.flipX = facing < 0;
            torso.color = legs.color = body.color;
            if (shooting)
            {
                bool firing = firingUpperBody && Time.time < recoilUntil;
                torso.sprite = firing ? firingUpperBody : Frame(upperBody, 12);
                // 원본 발사 상체는 왼쪽을 향하며 머리 위치가 기본 상체보다 (18, 5) 픽셀 떨어져 있습니다.
                torso.flipX = firing ? facing > 0 : facing < 0;
                legs.sprite = !grounded ? jumpLegs[vertical > 0 ? 1 : 3]
                    : Frame(Mathf.Abs(axis) > .01f ? runLegs : idleLegs, 12);
                torso.transform.localPosition = firing
                    ? new Vector3(facing * (18f / 41 - .06f), 5f / 41 + .02f, 0) : Vector3.zero;
            }
            else body.sprite = dead ? Frame(death, 10, false)
                : !grounded ? jump[vertical > 0 ? 1 : 3]
                : Frame(Mathf.Abs(axis) > .01f ? run : idle, 12);
        }
    }
}
