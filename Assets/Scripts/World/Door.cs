using DG.Tweening;
using Improv.Characters;
using UnityEngine;

namespace Improv.World
{
    /// <summary>
    /// 열고 닫는 문. (기획서 R-005)
    /// 닫혀 있으면 막는 충돌체(blocker)가 켜져서 지나갈 수 없고, 열면 꺼진다.
    /// 열림 여부는 월드 데이터(IsOpen)이고, 그림이 얇아지는 연출은 그 결과를 보여줄 뿐이다. (설계 원칙 P-2)
    /// </summary>
    public class Door : Interactable
    {
        // ==========================================
        // #1. 설정값
        // ==========================================

        [SerializeField] Collider2D clickArea;     // 클릭을 받는 영역 (트리거). 열려 있어도 클릭할 수 있게 따로 둔다
        [SerializeField] Collider2D blocker;       // 닫혔을 때 길을 막는 충돌체
        [SerializeField] SpriteRenderer visual;    // 문짝 그림
        [SerializeField] bool startsOpen = false;

        [SerializeField] float openWidthRatio = 0.25f;  // 열렸을 때 문짝 폭 비율. 옆에서 본 열린 문을 얇게 표현 [가정]
        [SerializeField] float openAlpha = 0.6f;        // 열렸을 때 투명도
        [SerializeField] float tweenDuration = 0.25f;   // 여닫는 연출 시간 (초)

        // ==========================================
        // #2. 상태
        // ==========================================

        public bool IsOpen { get; private set; }

        float closedScaleX;   // 닫혔을 때의 원래 폭. 시작할 때 기억해 둔다

        // ==========================================
        // #3. 유니티 생명주기
        // ==========================================

        void Awake()
        {
            if (clickArea == null || blocker == null || visual == null)
            {
                Debug.LogError($"[Door] '{name}'의 clickArea, blocker, visual 중 비어 있는 것이 있습니다.", this);
                enabled = false;
                return;
            }

            InteractionCollider = clickArea;
            closedScaleX = visual.transform.localScale.x;
            SetOpen(startsOpen, animate: false);
        }

        // 오브젝트가 사라질 때 진행 중인 트윈도 정리한다. 안 하면 사라진 오브젝트를 건드리는 경고가 난다.
        void OnDestroy() => visual.transform.DOKill();

        // ==========================================
        // #4. 상호작용
        // ==========================================

        public override InteractionResult Interact(CharacterInteractor actor)
        {
            SetOpen(!IsOpen, animate: true);
            return InteractionResult.Done;
        }

        public void SetOpen(bool open, bool animate)
        {
            IsOpen = open;

            // 판정은 즉시 바뀐다. 연출이 끝나기를 기다리지 않는다.
            blocker.enabled = !open;

            // 연출: DOTween으로 폭과 투명도를 부드럽게 바꾼다.
            // DOKill은 이전에 돌던 트윈을 멈춘다. 빠르게 두 번 클릭해도 꼬이지 않게.
            var t = visual.transform;
            t.DOKill();
            visual.DOKill();

            float targetScaleX = open ? closedScaleX * openWidthRatio : closedScaleX;
            var color = visual.color;
            color.a = open ? openAlpha : 1f;

            if (animate)
            {
                // DOScaleX: localScale.x를 목표값까지 tweenDuration초 동안 바꾼다.
                // SetEase: 변화 곡선. OutQuad는 처음에 빠르고 끝에서 느려진다.
                t.DOScaleX(targetScaleX, tweenDuration).SetEase(Ease.OutQuad);
                visual.DOColor(color, tweenDuration);
            }
            else
            {
                t.localScale = new Vector3(targetScaleX, t.localScale.y, t.localScale.z);
                visual.color = color;
            }
        }
    }
}
