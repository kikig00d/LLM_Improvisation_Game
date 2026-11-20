using System.Collections.Generic;
using Improv.World;
using UnityEngine;

namespace Improv.Characters
{
    /// <summary>
    /// 캐릭터의 손. 오브젝트 사용, 들기, 내려놓기를 담당한다.
    /// 플레이어와 NPC가 같은 규칙을 쓴다. 플레이어는 PlayerClickInteraction이, 나중에 NPC는 AI가 부른다. (설계 원칙 P-1)
    /// </summary>
    [RequireComponent(typeof(CharacterMotor))]
    public class CharacterInteractor : MonoBehaviour
    {
        // ==========================================
        // #1. 설정값
        // ==========================================

        [SerializeField] float interactRange = 1.5f;     // 손이 닿는 거리 (유닛). 기획서 O-001 [가정]
        [SerializeField] float reachHeight = 0.85f;      // 발에서 손(몸 중심)까지의 높이. 거리 계산의 기준점
        [SerializeField] float floorBandHeight = 3f;     // 한 층의 높이. 이 범위 밖을 클릭하면 다른 층으로 본다
        [SerializeField] Transform carryAnchor;          // 들고 있는 오브젝트가 붙을 위치 (몸 앞)
        [SerializeField] bool logResults = true;         // 결과를 Console에 남길지 (디버그용)

        // ==========================================
        // #2. 상태
        // ==========================================

        CharacterMotor motor;
        Collider2D[] ownColliders;

        // 사이에 막는 것이 있는지 검사할 때 결과를 담을 그릇. 매번 새로 만들지 않고 재사용해서 쓰레기(GC)를 줄인다.
        readonly List<RaycastHit2D> hitBuffer = new();
        readonly List<Collider2D> overlapBuffer = new();
        ContactFilter2D solidOnly;

        public Carryable Held { get; private set; }
        public Vector2 ReachOrigin => motor.FeetPosition + Vector2.up * reachHeight;

        // ==========================================
        // #3. 유니티 생명주기
        // ==========================================

        void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            ownColliders = GetComponentsInChildren<Collider2D>();

            // 모든 레이어를 검사하되, 트리거(문 클릭 영역 같은 것)는 막는 것으로 치지 않는다.
            // ContactFilter2D는 struct(값 타입)라 대입하면 복사본이 생긴다. 원본 noFilter는 바뀌지 않는다.
            solidOnly = ContactFilter2D.noFilter;
            solidOnly.useTriggers = false;

            if (carryAnchor == null)
            {
                Debug.LogError($"[CharacterInteractor] '{name}'의 carryAnchor가 비어 있습니다.", this);
                enabled = false;
            }
        }

        // ==========================================
        // #4. 바깥에 제공하는 행동
        // ==========================================

        // 대상을 사용한다. 대상이 무엇을 할지는 대상이 정한다 (문은 여닫고, 상자는 들린다).
        public InteractionResult TryInteract(Interactable target)
        {
            var result = CheckReach(target.ClosestPoint(ReachOrigin), target);
            if (result == InteractionResult.Done) result = target.Interact(this);
            Log($"{target.name} 상호작용", result);
            return result;
        }

        // Carryable.Interact가 부른다. 거리 검사는 TryInteract에서 이미 끝났다.
        public InteractionResult TryPickUp(Carryable item)
        {
            if (Held != null) return InteractionResult.HandsFull;
            if (item.Holder != null || item.IsTweening) return InteractionResult.Busy;

            Held = item;
            item.AttachTo(this, carryAnchor);
            return InteractionResult.Done;
        }

        // 들고 있는 것을 클릭한 위치에 내려놓는다. 높이는 지금 서 있는 층의 바닥으로 맞춘다. (R-007)
        public InteractionResult TryDropAt(Vector2 clickPoint)
        {
            var result = CheckDrop(clickPoint, out var floorPosition);
            if (result == InteractionResult.Done)
            {
                var item = Held;
                Held = null;
                item.PlaceAt(floorPosition);
            }
            Log("내려놓기", result);
            return result;
        }

        // ==========================================
        // #5. 판정
        // ==========================================

        InteractionResult CheckDrop(Vector2 clickPoint, out Vector2 floorPosition)
        {
            floorPosition = default;
            if (Held == null) return InteractionResult.NothingHeld;
            if (Held.IsTweening) return InteractionResult.Busy;
            if (motor.IsClimbing) return InteractionResult.WrongFloor;   // 계단 위에서는 놓을 바닥이 없다

            // 지금 층의 바닥에서 한 층 높이 안쪽을 클릭했는가
            float feetY = motor.FeetPosition.y;
            if (clickPoint.y < feetY - 0.5f || clickPoint.y > feetY + floorBandHeight)
                return InteractionResult.WrongFloor;

            floorPosition = new Vector2(clickPoint.x, feetY);
            var center = floorPosition + Held.FootprintOffset;

            var reach = CheckReach(center, null);
            if (reach != InteractionResult.Done) return reach;

            // E-001: 놓을 자리가 벽, 다른 오브젝트, 캐릭터와 겹치면 놓지 않는다.
            // 크기를 살짝 줄여서 검사한다. 딱 붙여 놓는 것까지 막지 않게.
            int count = Physics2D.OverlapBox(center, Held.FootprintSize * 0.95f, 0f, solidOnly, overlapBuffer);
            return count > 0 ? InteractionResult.NoSpace : InteractionResult.Done;
        }

        // 거리와 가로막힘을 검사한다. target은 검사에서 뺄 대상 오브젝트 (내려놓기처럼 대상이 없으면 null).
        InteractionResult CheckReach(Vector2 point, Interactable target)
        {
            if (Vector2.Distance(ReachOrigin, point) > interactRange) return InteractionResult.OutOfRange;

            // R-007a: 손에서 대상까지 선을 그어, 중간에 단단한 충돌체가 있으면 막힌 것이다.
            int count = Physics2D.Linecast(ReachOrigin, point, solidOnly, hitBuffer);
            for (int i = 0; i < count; i++)
            {
                var hit = hitBuffer[i].collider;
                if (IsOwnCollider(hit)) continue;
                // 대상 자신의 충돌체(문이라면 문을 막는 blocker까지)는 장애물로 치지 않는다.
                if (target != null && hit.GetComponentInParent<Interactable>() == target) continue;
                return InteractionResult.Blocked;
            }
            return InteractionResult.Done;
        }

        bool IsOwnCollider(Collider2D c)
        {
            foreach (var own in ownColliders)
                if (own == c) return true;
            return false;
        }

        void Log(string action, InteractionResult result)
        {
            if (logResults) Debug.Log($"[{name}] {action}: {result}", this);
        }
    }
}
