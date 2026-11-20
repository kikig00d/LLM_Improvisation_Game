using UnityEngine;

namespace Improv.Characters
{
    /// <summary>
    /// 캐릭터의 실제 움직임을 보고 애니메이션과 좌우 방향을 정한다.
    /// 그림(외형 프리팹)을 바꿔도 이 스크립트와 이동 로직은 그대로 쓸 수 있게, 상태 이름만 맞추면 된다.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor))]
    public class CharacterAnimator : MonoBehaviour
    {
        // ==========================================
        // #1. 설정값
        // ==========================================

        [SerializeField] Animator animator;          // 외형 프리팹 안의 Animator
        [SerializeField] Transform visualRoot;       // 좌우 반전할 외형의 뿌리 오브젝트
        [SerializeField] bool artFacesRight = true;  // 원본 그림이 오른쪽을 보고 있는가
        [SerializeField] string idleState = "Idle";
        [SerializeField] string walkState = "Walk";
        [SerializeField] float moveThreshold = 0.3f;    // 이보다 느리면 멈춘 것으로 본다 (유닛/초). 벽에 붙었을 때의 미세한 떨림을 무시할 만큼
        [SerializeField] float crossFade = 0.1f;        // 애니메이션 전환에 섞는 시간 (초)

        // ==========================================
        // #2. 상태
        // ==========================================

        CharacterMotor motor;
        int currentState;
        int idleHash, walkHash;
        float baseScaleX;

        // ==========================================
        // #3. 유니티 생명주기
        // ==========================================

        void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            if (animator == null || visualRoot == null)
            {
                Debug.LogError($"[CharacterAnimator] '{name}'의 animator 또는 visualRoot가 비어 있습니다.", this);
                enabled = false;
                return;
            }

            // 상태 이름을 매번 문자열로 찾지 않고, 정수 해시로 미리 바꿔 둔다. 문자열 비교보다 빠르다.
            idleHash = Animator.StringToHash(idleState);
            walkHash = Animator.StringToHash(walkState);
            baseScaleX = Mathf.Abs(visualRoot.localScale.x);
        }

        // LateUpdate: 모든 Update가 끝난 뒤에 불린다.
        void LateUpdate()
        {
            // 몸이 물리 틱마다 잰 실제 속도를 쓴다. 걷기든 계단이든 같은 방식으로 처리된다.
            // (화면 프레임마다 위치 차이로 재면, 물리 틱 사이 프레임에서 0이 나와 Idle과 Walk가 깜빡인다)
            Vector2 velocity = motor.Velocity;

            bool moving = velocity.magnitude > moveThreshold;
            Play(moving ? walkHash : idleHash);

            // 바라보는 방향:
            //  - 걸을 때는 "가려는 방향(입력)"을 본다. 벽을 밀 때 물리 반동으로 생기는 반대 방향 속도에 속지 않게.
            //  - 계단에서는 입력이 위/아래뿐이라, 실제로 움직이는 좌우 방향을 본다.
            //  - 둘 다 없으면 마지막 방향을 유지한다.
            float facingX = motor.IsClimbing ? velocity.x : motor.MoveInput.x;
            float threshold = motor.IsClimbing ? moveThreshold : 0.1f;
            if (Mathf.Abs(facingX) > threshold)
            {
                bool faceRight = facingX > 0f;
                float sign = (faceRight == artFacesRight) ? 1f : -1f;
                var s = visualRoot.localScale;
                visualRoot.localScale = new Vector3(baseScaleX * sign, s.y, s.z);
            }
        }

        void Play(int stateHash)
        {
            if (stateHash == currentState) return;
            currentState = stateHash;
            animator.CrossFade(stateHash, crossFade);
        }
    }
}
