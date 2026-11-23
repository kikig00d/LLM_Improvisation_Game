using Improv.UI;
using Improv.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Improv.Player
{
    /// <summary>
    /// 시간 속도 단축키 (R-017a). P: 멈춤/풀기, + / -: 속도 올리기/내리기.
    /// 시계 화면(ClockView)의 버튼도 같은 GameClock 함수를 부른다.
    /// 대화창에 글자를 치는 동안은 단축키를 듣지 않는다. 한글 자판에서 P 자리는 'ㅔ'라서, 치다가 멈춰 버리기 때문이다.
    /// 그때는 화면 구석의 속도 버튼을 누르면 된다.
    /// </summary>
    public class GameSpeedInput : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [SerializeField] string pauseActionName = "Game/Pause";
        [SerializeField] string speedUpActionName = "Game/SpeedUp";
        [SerializeField] string speedDownActionName = "Game/SpeedDown";
        [SerializeField] DialogueWindow dialogueWindow;   // 비우면 씬에서 찾는다

        // =====================================================================
        // #2. 상태
        // =====================================================================

        InputAction pauseAction, upAction, downAction;

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Awake()
        {
            pauseAction = InputSystem.actions?.FindAction(pauseActionName);
            upAction = InputSystem.actions?.FindAction(speedUpActionName);
            downAction = InputSystem.actions?.FindAction(speedDownActionName);
            if (dialogueWindow == null) dialogueWindow = FindFirstObjectByType<DialogueWindow>();
            if (pauseAction == null || upAction == null || downAction == null)
            {
                Debug.LogError("[GameSpeedInput] Game 맵의 Pause/SpeedUp/SpeedDown 액션을 찾지 못했습니다.", this);
                enabled = false;
            }
        }

        void OnEnable()
        {
            pauseAction?.Enable();
            upAction?.Enable();
            downAction?.Enable();
        }

        void Update()
        {
            var clock = GameClock.Instance;
            if (clock == null) return;
            if (dialogueWindow != null && dialogueWindow.IsOpen) return;   // 글자 입력 중 (위 설명)

            if (pauseAction.WasPressedThisFrame()) clock.TogglePause();
            if (upAction.WasPressedThisFrame()) clock.StepUp();
            if (downAction.WasPressedThisFrame()) clock.StepDown();
        }
    }
}
