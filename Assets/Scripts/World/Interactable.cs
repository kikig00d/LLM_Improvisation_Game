using Improv.Characters;
using UnityEngine;

namespace Improv.World
{
    /// <summary>
    /// 캐릭터가 상호작용할 수 있는 오브젝트의 공통 부모. 문, 운반 가능 오브젝트 등이 이걸 상속한다.
    /// abstract 클래스라 직접 붙일 수 없고, 상속한 클래스만 붙일 수 있다.
    /// C로 치면 함수 포인터 Interact를 반드시 채워야 하는 구조체 틀과 비슷하다.
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        // 클릭 판정과 거리 계산에 쓰는 충돌체. 자식 클래스가 Awake에서 정한다.
        public Collider2D InteractionCollider { get; protected set; }

        // 캐릭터의 기준점에서 가장 가까운 이 오브젝트 위의 점. 거리 판정에 쓴다.
        public Vector2 ClosestPoint(Vector2 from) =>
            InteractionCollider != null ? InteractionCollider.ClosestPoint(from) : (Vector2)transform.position;

        // 지금 이 캐릭터가 사용할 수 있는지 "묻기만" 한다. 아무것도 바꾸지 않는다.
        // 커서와 강조 표시가 매 프레임 부르므로 부작용이 없어야 한다. 거리와 가로막힘은 CharacterInteractor가 따로 본다.
        // virtual: 기본 동작이 있고, 필요한 클래스만 override로 바꾼다. (abstract는 반드시 바꿔야 함)
        public virtual InteractionResult CanInteract(CharacterInteractor actor) => InteractionResult.Done;

        // 캐릭터가 이 오브젝트를 사용할 때 무슨 일이 일어나는지. 상속한 클래스가 반드시 구현한다.
        public abstract InteractionResult Interact(CharacterInteractor actor);
    }

    /// <summary>
    /// 상호작용 결과. 실패한 이유를 구분해 두면 UI 메시지, NPC 판단, 테스트에 그대로 쓸 수 있다.
    /// </summary>
    public enum InteractionResult
    {
        Done,           // 성공
        OutOfRange,     // 너무 멀다 (O-001 상호작용 거리)
        Blocked,        // 사이에 벽, 닫힌 문, 난간이 있다 (R-007a)
        HandsFull,      // 이미 무언가를 들고 있다 (R-006)
        NothingHeld,    // 내려놓을 게 없다
        NoSpace,        // 내려놓을 자리가 다른 것과 겹친다 (E-001)
        WrongFloor,     // 다른 층을 클릭했다
        Busy,           // 연출(트윈)이 끝나지 않았다
        NotEdible,      // 들고 있는 것이 음식이 아니다 (R-020)
    }
}
