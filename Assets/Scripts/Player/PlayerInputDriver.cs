using Improv.Characters;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Improv.Player
{
    /// <summary>
    /// 플레이어의 키보드 입력을 읽어 CharacterMotor에 넘긴다. 입력만 담당하고 이동 계산은 하지 않는다.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor))]
    public class PlayerInputDriver : MonoBehaviour
    {
        // ==========================================
        // #1. 설정값
        // ==========================================

        // 프로젝트 전역 입력 액션(Assets/InputSystem_Actions)에서 찾을 액션 이름. "맵/액션" 형식
        // WASD와 방향키가 이미 Move에 묶여 있다.
        [SerializeField] string moveActionName = "Player/Move";

        // ==========================================
        // #2. 상태
        // ==========================================

        CharacterMotor motor;
        InputAction moveAction;

        // ==========================================
        // #3. 유니티 생명주기
        // ==========================================

        void Awake()
        {
            motor = GetComponent<CharacterMotor>();

            // InputSystem.actions: Unity 6부터 생긴 "프로젝트 전역 액션".
            // 예전의 Input.GetKey("w") 대신, 키 배치를 에셋에 모아 두고 이름으로 꺼내 쓴다.
            // 키를 바꾸고 싶으면 코드가 아니라 InputSystem_Actions 에셋을 고치면 된다.
            moveAction = InputSystem.actions?.FindAction(moveActionName);
            if (moveAction == null)
            {
                Debug.LogError($"[PlayerInputDriver] 입력 액션 '{moveActionName}'을 찾지 못했습니다. " +
                               "Project Settings > Input System Package의 Project-wide Actions를 확인하세요.", this);
                enabled = false;
            }
        }

        // Update: 매 프레임. 입력은 여기서 읽고, 물리 이동은 Motor의 FixedUpdate가 한다.
        void Update()
        {
            motor.SetMoveInput(moveAction.ReadValue<Vector2>());
        }

        // 컴포넌트가 꺼지면 캐릭터가 마지막 입력으로 계속 걷지 않게 멈춘다.
        void OnDisable()
        {
            if (motor != null) motor.SetMoveInput(Vector2.zero);
        }
    }
}
