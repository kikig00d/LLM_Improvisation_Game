using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace Improv.World
{
    /// <summary>
    /// 마우스를 올렸을 때 오브젝트 테두리에 빛을 띄운다.
    ///  - Available: 지금 상호작용할 수 있음 → 하얗게, 은은히 깜빡임
    ///  - Unavailable: 상호작용 대상이지만 지금은 안 됨 (멀다, 가로막힘 등) → 흐린 회색
    /// 원리: 같은 스프라이트를 단색으로 칠한 사본 8장을 사방으로 살짝 밀어 뒤에 깔면 테두리처럼 보인다.
    /// 판정은 하지 않는다. 무엇을 보여줄지는 PlayerHoverFeedback이 정한다. (설계 원칙 P-2)
    /// </summary>
    public class HoverHighlight : MonoBehaviour
    {
        public enum State { None, Available, Unavailable }

        // ==========================================
        // #1. 설정값
        // ==========================================

        [SerializeField] Material silhouetteMaterial;               // Improv/SpriteSilhouette 셰이더를 쓰는 머티리얼
        [SerializeField] float outlineWidth = 0.05f;                 // 테두리 두께 (월드 유닛)
        [SerializeField] Color availableColor = Color.white;
        [SerializeField] Color unavailableColor = new(0.6f, 0.6f, 0.6f, 0.45f);
        [SerializeField] float pulseMinAlpha = 0.55f;                // 깜빡일 때 가장 흐린 투명도
        [SerializeField] float pulseDuration = 0.6f;                 // 한 번 밝아졌다 흐려지는 시간 (초)

        // 사방 8방향. 대각선은 1/√2 ≈ 0.707을 곱해 같은 두께로 보이게 한다.
        static readonly Vector2[] Directions =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
            new(0.707f, 0.707f), new(-0.707f, 0.707f), new(0.707f, -0.707f), new(-0.707f, -0.707f),
        };

        // ==========================================
        // #2. 상태
        // ==========================================

        readonly List<SpriteRenderer> outlines = new();
        State current = State.None;
        float pulse = 1f;   // DOTween이 0.55 ~ 1 사이로 움직이는 값
        Tween pulseTween;

        public State Current => current;

        // ==========================================
        // #3. 바깥에 제공하는 기능
        // ==========================================

        public void SetState(State state)
        {
            if (state == current) return;
            current = state;

            if (state != State.None && outlines.Count == 0) BuildOutlines();

            pulseTween?.Kill();
            pulse = 1f;
            if (state == State.Available)
            {
                // DOTween.To: 아무 float 값이나 트윈할 수 있다. getter와 setter를 람다로 넘긴다.
                // SetLoops(-1, Yoyo): 무한 반복, 갔다가 되돌아오기.
                pulseTween = DOTween.To(() => pulse, v => pulse = v, pulseMinAlpha, pulseDuration)
                    .SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine);
            }
            ApplyColor();
        }

        // ==========================================
        // #4. 유니티 생명주기
        // ==========================================

        void LateUpdate()
        {
            if (current == State.Available) ApplyColor();   // 깜빡임 반영
        }

        void OnDisable() => SetState(State.None);
        void OnDestroy() => pulseTween?.Kill();

        // ==========================================
        // #5. 테두리 만들기
        // ==========================================

        // 처음 강조될 때 한 번만 만든다. 모든 오브젝트에 미리 만들어 두면 낭비라서.
        void BuildOutlines()
        {
            if (silhouetteMaterial == null)
            {
                Debug.LogError($"[HoverHighlight] '{name}'의 silhouetteMaterial이 비어 있습니다.", this);
                return;
            }

            // 지금 있는 그림들만 대상으로 한다. 목록을 먼저 복사해 두고 그 위에 사본을 붙인다.
            var sources = new List<SpriteRenderer>(GetComponentsInChildren<SpriteRenderer>());
            foreach (var source in sources)
            {
                foreach (var dir in Directions)
                {
                    var go = new GameObject("Outline");
                    go.transform.SetParent(source.transform, false);

                    // 부모가 늘어나 있으면(문처럼 scale 0.25 × 2.2) 로컬 거리도 늘어난다.
                    // 월드에서 같은 두께가 되도록 부모 크기로 나눠 준다.
                    var s = source.transform.lossyScale;
                    go.transform.localPosition = new Vector3(dir.x * outlineWidth / s.x, dir.y * outlineWidth / s.y, 0f);

                    var r = go.AddComponent<SpriteRenderer>();
                    r.sprite = source.sprite;
                    r.flipX = source.flipX;
                    r.flipY = source.flipY;
                    r.sharedMaterial = silhouetteMaterial;
                    r.sortingLayerID = source.sortingLayerID;
                    r.sortingOrder = source.sortingOrder - 1;   // 원래 그림 바로 뒤
                    outlines.Add(r);
                }
            }
        }

        void ApplyColor()
        {
            var color = current switch
            {
                State.Available => new Color(availableColor.r, availableColor.g, availableColor.b, availableColor.a * pulse),
                State.Unavailable => unavailableColor,
                _ => Color.clear,
            };
            bool visible = current != State.None;
            foreach (var r in outlines)
            {
                if (r == null) continue;
                r.enabled = visible;
                r.color = color;
            }
        }
    }
}
