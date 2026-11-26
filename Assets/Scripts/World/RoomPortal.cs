using Improv.Characters;
using UnityEngine;

namespace Improv.World
{
    /// <summary>
    /// 따로 떨어진 공간(개인실, 스태프 룸)으로 드나드는 문 (GDD R-030).
    /// 복도 쪽 문과 방 안쪽 문이 한 짝(other)이다. 누르면 반대쪽 문 앞으로 몸을 옮긴다 (포켓몬의 실내 이동처럼).
    /// 떨어진 공간은 월드의 먼 곳에 실제로 있다. 그래서 눈·귀·길 찾기는 "공간이 다르면 이 문을 거친다"는 규칙으로 잇는다:
    /// - 귀(Hearing): 입 → 안쪽 문 + 바깥쪽 문 → 귀, 그리고 닫힌 문 통과율.
    /// - 눈(Vision): 공간이 다르면 보이지 않는다.
    /// - NPC 길 찾기(NpcNavigator): 다른 공간이 목표면 이 문까지 걸어가서 쓴다.
    /// </summary>
    public class RoomPortal : Interactable
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [SerializeField] RoomPortal other;           // 짝 문
        [SerializeField] Collider2D clickArea;       // 클릭을 받는 영역 (트리거)
        [Tooltip("이 문으로 나왔을 때 설 자리 (문 기준). 문 안쪽으로 조금 비켜 선다")]
        [SerializeField] Vector2 arriveOffset = new(0.8f, 0f);

        // =====================================================================
        // #2. 바깥에 제공하는 정보
        // =====================================================================

        public RoomPortal Other => other;
        public Vector2 Position => transform.position;
        public Vector2 ArrivalPoint => (Vector2)transform.position + arriveOffset;

        /// <summary>그 점이 속한 떨어진 공간. 큰 공간(복도, 1층)이면 null.</summary>
        public static Room SpaceOf(Vector2 point)
        {
            var room = Room.FindAt(point);
            return room != null && room.Detached ? room : null;
        }

        /// <summary>떨어진 공간 안쪽의 문. 이 문의 짝이 바깥(복도) 문이다. 없으면 null.</summary>
        public static RoomPortal InnerOf(Room space)
        {
            if (space == null) return null;
            foreach (var p in FindObjectsByType<RoomPortal>(FindObjectsSortMode.None))
                if (SpaceOf(p.Position) == space) return p;
            return null;
        }

        /// <summary>from 공간에서 to 공간으로 가려면 먼저 지날 문. 같은 공간이면 null.</summary>
        public static RoomPortal NextOnWay(Room from, Room to)
        {
            if (from == to) return null;
            if (from != null) return InnerOf(from);   // 먼저 지금 방에서 나간다
            return InnerOf(to)?.other;                // 큰 공간에서는 그 방의 바깥쪽 문으로 들어간다
        }

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Awake()
        {
            if (other == null || clickArea == null)
            {
                Debug.LogError($"[RoomPortal] '{name}'의 other 또는 clickArea가 비어 있습니다.", this);
                enabled = false;
                return;
            }
            InteractionCollider = clickArea;
        }

        // =====================================================================
        // #4. 상호작용
        // =====================================================================

        public override InteractionResult CanInteract(CharacterInteractor actor)
        {
            var motor = actor.GetComponent<CharacterMotor>();
            return motor != null && !motor.IsClimbing ? InteractionResult.Done : InteractionResult.Busy;
        }

        public override InteractionResult Interact(CharacterInteractor actor)
        {
            var result = CanInteract(actor);
            if (result != InteractionResult.Done) return result;
            actor.GetComponent<CharacterMotor>().Teleport(other.ArrivalPoint);   // 들고 있던 것은 손(CarryAnchor)의 자식이라 함께 온다
            return InteractionResult.Done;
        }

        void OnDrawGizmos()
        {
            if (other == null) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, other.transform.position);
            Gizmos.DrawWireSphere(ArrivalPoint, 0.15f);
        }
    }
}
