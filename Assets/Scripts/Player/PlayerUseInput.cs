using Improv.Characters;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Improv.Player
{
    /// <summary>
    /// 들고 있는 것 쓰기 (지금은 먹기만). F 키 (Player/Use).
    /// 왼쪽 클릭은 "대상 줍기·내려놓기"라서, 손에 든 것을 쓰는 동작은 키를 따로 둔다.
    /// </summary>
    [RequireComponent(typeof(CharacterVitals))]
    public class PlayerUseInput : MonoBehaviour
    {
        [SerializeField] string useActionName = "Player/Use";

        CharacterVitals vitals;
        InputAction useAction;

        void Awake()
        {
            vitals = GetComponent<CharacterVitals>();
            useAction = InputSystem.actions?.FindAction(useActionName);
            if (useAction == null)
            {
                Debug.LogError($"[PlayerUseInput] 입력 액션 {useActionName}을 찾지 못했습니다.", this);
                enabled = false;
            }
        }

        void OnEnable() => useAction?.Enable();

        void Update()
        {
            if (!useAction.WasPressedThisFrame()) return;
            var result = vitals.TryEatHeld();
            if (result != Improv.World.InteractionResult.Done) Debug.Log($"[PlayerUseInput] 먹기: {result}", this);
        }
    }
}
