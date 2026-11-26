using Improv.Characters.Sight;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Improv.Player
{
    /// <summary>
    /// 플레이어의 시선 조종자. 마우스 커서를 바라보게 하고(R-003), 오른쪽 버튼을 누르는 동안 멀리 본다(R-004).
    /// 눈(Vision)은 몸의 능력이고, 이 스크립트는 "어디를 볼지"만 넘긴다. NPC는 같은 눈을 다른 조종자가 쓴다 (P-1).
    /// 카메라는 플레이어를 따라간다 (R-030: 산장이 화면보다 길어졌다). 떨어진 공간으로 옮겨지면 미끄러지지 않고 바로 건너뛴다.
    /// </summary>
    [RequireComponent(typeof(Vision))]
    public class PlayerGazeInput : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [SerializeField] string pointActionName = "UI/Point";     // 마우스 커서 위치 (화면 좌표)
        [SerializeField] string zoomActionName = "Player/Zoom";   // 오른쪽 버튼 (누르는 동안만, Q4 결정)
        [SerializeField] Camera worldCamera;                      // 비우면 Main Camera

        [Header("멀리 볼 때 카메라 (R-004, O-003 [가정])")]
        [SerializeField] float zoomCameraShift = 3f;     // 시선 쪽으로 카메라를 밀어내는 거리 (유닛)
        [SerializeField] float cameraSmoothTime = 0.15f; // 카메라가 따라가는 데 걸리는 시간 (초)
        [Tooltip("카메라와 이만큼 멀어지면 따라가지 않고 바로 옮긴다 (문으로 다른 공간에 갔을 때)")]
        [SerializeField] float cameraSnapDistance = 8f;

        // =====================================================================
        // #2. 상태
        // =====================================================================

        Vision vision;
        InputAction pointAction, zoomAction;
        Vector3 cameraOffset;        // 플레이어 발에서 카메라까지 (씬에 놓인 자리로 정한다)
        Vector3 cameraVelocity;      // SmoothDamp가 쓰는 속도 기억 칸

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Awake()
        {
            vision = GetComponent<Vision>();
            if (worldCamera == null) worldCamera = Camera.main;
            pointAction = InputSystem.actions?.FindAction(pointActionName);
            zoomAction = InputSystem.actions?.FindAction(zoomActionName);
            if (pointAction == null || zoomAction == null || worldCamera == null)
            {
                Debug.LogError($"[PlayerGazeInput] 입력 액션({pointActionName}, {zoomActionName}) 또는 카메라를 찾지 못했습니다.", this);
                enabled = false;
                return;
            }
            cameraOffset = worldCamera.transform.position - transform.position;
        }

        void Update()
        {
            Vector2 screen = pointAction.ReadValue<Vector2>();
            vision.LookAt(worldCamera.ScreenToWorldPoint(screen));
            vision.SetZoomed(zoomAction.IsPressed());
        }

        void LateUpdate()
        {
            // 멀리 볼 때는 카메라를 시선 쪽으로 밀어 화면에 앞쪽이 더 보이게 한다. 놓으면 제자리로 돌아온다.
            Vector3 target = transform.position + cameraOffset;
            if (vision.IsZoomed) target += (Vector3)(vision.GazeDirection * zoomCameraShift);
            if (Vector2.Distance(worldCamera.transform.position, target) > cameraSnapDistance)
            {
                worldCamera.transform.position = target;   // 문으로 순간 이동: 산장을 가로질러 미끄러지지 않게
                cameraVelocity = Vector3.zero;
                return;
            }
            // SmoothDamp: 목표까지 부드럽게 따라가는 함수. 스프링처럼 처음엔 빠르고 가까워질수록 느려진다.
            worldCamera.transform.position = Vector3.SmoothDamp(worldCamera.transform.position, target, ref cameraVelocity, cameraSmoothTime);
        }

        void OnDisable()
        {
            if (vision != null) vision.SetZoomed(false);
        }
    }
}
