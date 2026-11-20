using System.Collections.Generic;
using Improv.Characters;
using Improv.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Improv.Player
{
    /// <summary>
    /// 플레이어의 왼쪽 클릭을 해석해 CharacterInteractor에 넘긴다. (기획서 4장 조작, R-005~R-007)
    ///  - 클릭한 곳에 상호작용 대상이 있으면: 그것을 사용한다.
    ///  - 없고 무언가를 들고 있으면: 그 자리에 내려놓는다.
    /// </summary>
    [RequireComponent(typeof(CharacterInteractor))]
    public class PlayerClickInteraction : MonoBehaviour
    {
        // ==========================================
        // #1. 설정값
        // ==========================================

        [SerializeField] string interactActionName = "Player/Interact";  // 왼쪽 클릭이 묶인 액션
        [SerializeField] string pointActionName = "UI/Point";             // 마우스 커서 위치 (화면 픽셀 좌표)

        // ==========================================
        // #2. 상태
        // ==========================================

        CharacterInteractor interactor;
        InputAction interactAction;
        InputAction pointAction;
        Camera cam;
        readonly List<Collider2D> pickBuffer = new();
        ContactFilter2D everything;

        // ==========================================
        // #3. 유니티 생명주기
        // ==========================================

        void Awake()
        {
            interactor = GetComponent<CharacterInteractor>();
            interactAction = InputSystem.actions?.FindAction(interactActionName);
            pointAction = InputSystem.actions?.FindAction(pointActionName);
            cam = Camera.main;

            // 클릭 판정은 트리거(문 클릭 영역)도 잡아야 한다.
            everything = ContactFilter2D.noFilter;

            if (interactAction == null || pointAction == null || cam == null)
            {
                Debug.LogError($"[PlayerClickInteraction] 입력 액션 또는 메인 카메라를 찾지 못했습니다.", this);
                enabled = false;
            }
        }

        void OnEnable()
        {
            // UI 맵의 액션은 꺼져 있을 수 있어 명시적으로 켠다. 이미 켜져 있으면 아무 일도 없다.
            interactAction?.Enable();
            pointAction?.Enable();
        }

        void Update()
        {
            // WasPressedThisFrame: 이번 프레임에 눌린 순간에만 true. 누르고 있는 동안 계속 true가 아니다.
            if (!interactAction.WasPressedThisFrame()) return;

            // 화면 픽셀 좌표 → 월드 좌표. 2D라 z는 무시한다.
            Vector2 screen = pointAction.ReadValue<Vector2>();
            Vector2 world = cam.ScreenToWorldPoint(screen);
            HandleClick(world);
        }

        // ==========================================
        // #4. 클릭 처리 (테스트에서도 직접 부를 수 있게 public)
        // ==========================================

        public InteractionResult HandleClick(Vector2 worldPoint)
        {
            var target = FindInteractableAt(worldPoint);
            if (target != null) return interactor.TryInteract(target);
            if (interactor.Held != null) return interactor.TryDropAt(worldPoint);
            return InteractionResult.NothingHeld;
        }

        Interactable FindInteractableAt(Vector2 worldPoint)
        {
            int count = Physics2D.OverlapPoint(worldPoint, everything, pickBuffer);
            for (int i = 0; i < count; i++)
            {
                // 충돌체가 자식 오브젝트에 있어도 부모의 Interactable을 찾는다.
                var interactable = pickBuffer[i].GetComponentInParent<Interactable>();
                if (interactable != null && interactable.enabled) return interactable;
            }
            return null;
        }
    }
}
