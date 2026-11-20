using DG.Tweening;
using Improv.Characters;
using UnityEngine;

namespace Improv.World
{
    /// <summary>
    /// 들고 옮길 수 있는 오브젝트. 상자, 나중에는 시체 등. (기획서 R-006, R-007, E-002)
    /// 바닥에 있을 때는 충돌체가 켜져서 캐릭터 이동을 막고, 들려 있을 때는 꺼진다.
    /// 오브젝트의 기준점(pivot)은 바닥에 닿는 아래쪽 가운데다. 캐릭터의 발 기준점과 같은 규칙.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class Carryable : Interactable
    {
        // ==========================================
        // #1. 설정값
        // ==========================================

        [SerializeField] string displayName = "상자";   // 로그, 나중에 NPC 기억과 LLM 문맥에 쓰일 이름
        [SerializeField] float tweenDuration = 0.25f;  // 들고 내려놓는 연출 시간 (초)
        [SerializeField] float jumpHeight = 0.3f;      // 연출 중 살짝 들어 올리는 높이 (유닛)
        [SerializeField] int heldSortingBoost = 200;   // 들려 있을 때 정렬 순서를 이만큼 올려 캐릭터 그림보다 앞에 그린다

        // ==========================================
        // #2. 상태
        // ==========================================

        BoxCollider2D box;
        SpriteRenderer[] renderers;

        public string DisplayName => displayName;
        public CharacterInteractor Holder { get; private set; }   // 들고 있는 캐릭터. 없으면 null
        public bool IsTweening { get; private set; }

        // 바닥에 놓였을 때 차지하는 크기. 내려놓을 자리 검사(E-001)에 쓴다.
        public Vector2 FootprintSize => Vector2.Scale(box.size, transform.lossyScale);
        public Vector2 FootprintOffset => Vector2.Scale(box.offset, transform.lossyScale);

        // ==========================================
        // #3. 유니티 생명주기
        // ==========================================

        void Awake()
        {
            box = GetComponent<BoxCollider2D>();
            renderers = GetComponentsInChildren<SpriteRenderer>();
            InteractionCollider = box;

            // 움직이는 충돌체에는 Rigidbody2D를 붙이는 게 원칙이다.
            // Kinematic이라 물리에 밀리지 않고, 코드가 옮길 때만 움직인다.
            var body = GetComponent<Rigidbody2D>();
            if (body == null) body = gameObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
        }

        void OnDestroy() => transform.DOKill();

        // ==========================================
        // #4. 상호작용: 클릭하면 줍기를 시도한다
        // ==========================================

        public override InteractionResult Interact(CharacterInteractor actor) => actor.TryPickUp(this);

        // CharacterInteractor가 부른다. 직접 부르지 않는다.
        internal void AttachTo(CharacterInteractor holder, Transform anchor)
        {
            Holder = holder;
            box.enabled = false;   // 들고 있는 동안은 아무것도 막지 않는다
            ShiftSorting(heldSortingBoost);

            // SetParent(anchor, true): 화면상 위치를 유지한 채 부모만 바꾼다.
            // 그 뒤 로컬 좌표 (0,0)으로 뛰어오르듯 이동시키면 손으로 끌어올리는 연출이 된다.
            transform.SetParent(anchor, true);
            transform.DOKill();   // 반드시 새 트윈을 만들기 "전에" 이전 트윈을 정지한다
            PlayTween(transform.DOLocalJump(Vector3.zero, jumpHeight, 1, tweenDuration), enableColliderAtEnd: false);
        }

        internal void PlaceAt(Vector2 floorPosition)
        {
            Holder = null;
            transform.SetParent(null, true);
            ShiftSorting(-heldSortingBoost);

            // 충돌체는 연출이 끝난 뒤 켠다. 날아가는 도중 캐릭터를 밀지 않게.
            transform.DOKill();
            PlayTween(transform.DOJump(floorPosition, jumpHeight, 1, tweenDuration), enableColliderAtEnd: true);
        }

        void ShiftSorting(int delta)
        {
            foreach (var r in renderers) r.sortingOrder += delta;
        }

        // 주의: 여기서 DOKill을 부르면 인자로 막 만들어 넘긴 트윈까지 죽는다. (실제로 있었던 버그)
        // C#은 인자를 먼저 계산한 뒤 함수에 들어오므로, 정지는 호출하는 쪽에서 트윈을 만들기 전에 한다.
        void PlayTween(Tween tween, bool enableColliderAtEnd)
        {
            IsTweening = true;

            // OnComplete: 트윈이 끝나면 실행할 코드. () => { ... }는 이름 없는 함수(람다)다.
            tween.SetEase(Ease.OutQuad).OnComplete(() =>
            {
                IsTweening = false;
                if (enableColliderAtEnd) box.enabled = true;
            });
        }
    }
}
