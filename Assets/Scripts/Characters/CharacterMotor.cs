using Improv.World;
using UnityEngine;

namespace Improv.Characters
{
    /// <summary>
    /// 캐릭터의 몸. 걷기와 계단 오르내리기를 담당한다.
    /// 누가 조종하는지는 모른다. 플레이어는 PlayerInputDriver가, 나중에 NPC는 AI가
    /// SetMoveInput()으로 같은 몸을 움직인다. (단일 책임: 몸과 조종자를 분리)
    ///
    /// 이동 모델: 물리 중력을 쓰지 않는 "층 + x좌표" 방식.
    ///  - 걸을 때: 높이(y)를 고정하고 좌우로만 움직인다. 벽 충돌은 물리 엔진이 처리.
    ///  - 계단에서: 물리를 잠시 끄고 계단 경로를 따라 직접 위치를 옮긴다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]  // 이 컴포넌트를 붙이면 Rigidbody2D도 자동으로 붙는다
    public class CharacterMotor : MonoBehaviour
    {
        // ==========================================
        // #1. 설정값 (Inspector에서 조정)
        // ==========================================

        [SerializeField] float moveSpeed = 3f;           // 걷는 속도 (유닛/초). 기획서 O-001 [가정]
        [SerializeField] float climbSpeed = 2f;          // 계단 오르는 속도 (유닛/초) [가정]
        [SerializeField] float verticalInputDeadzone = 0.5f;  // 위아래 입력이 이 값을 넘어야 계단에 오른다

        // ==========================================
        // #2. 내부 상태
        // ==========================================

        Rigidbody2D body;
        Vector2 moveInput;        // 조종자가 넣어준 이동 입력. x: 좌우, y: 위아래 (-1 ~ 1)

        Stairs currentStairs;     // 지금 오르고 있는 계단. null이면 걷는 중
        float climbProgress;      // 계단 위의 위치. 0 = 아래 끝, 1 = 위 끝
        Vector2 lastStepPosition; // 지난 물리 틱의 위치. 실제 속도 측정용

        // ==========================================
        // #3. 바깥에 제공하는 기능
        // ==========================================

        public bool IsClimbing => currentStairs != null;

        // 지난 물리 틱 동안 실제로 움직인 속도 (유닛/초). 벽에 막혀 있으면 0에 가깝다.
        // 애니메이션, 나중에 NPC의 "누가 뛰어갔다" 같은 인지에 쓴다.
        public Vector2 Velocity { get; private set; }

        // 조종자가 원하는 방향. 벽에 막혀 실제로 못 움직여도 이 값은 남는다.
        public Vector2 MoveInput => moveInput;

        // 캐릭터의 발 위치. 이 프로젝트에서 캐릭터 오브젝트의 기준점(pivot)은 발이다.
        public Vector2 FeetPosition => body.position;

        // 조종자가 매 프레임 호출한다. 실제 이동은 FixedUpdate에서 일어난다.
        public void SetMoveInput(Vector2 input) => moveInput = Vector2.ClampMagnitude(input, 1f);

        /// <summary>
        /// 발을 그 자리로 옮긴다. 떨어진 공간의 문(RoomPortal)이 부른다 (R-030). 계단 위에서는 쓰지 않는다.
        /// body.position은 물리 엔진을 거치지 않는 순간 이동이라 세로 잠금(FreezePositionY)에 걸리지 않는다.
        /// </summary>
        public void Teleport(Vector2 feet)
        {
            body.position = feet;
            transform.position = feet;   // 보간(Interpolate)이 옛 자리에서 미끄러져 오지 않게 그림도 바로 옮긴다
            body.linearVelocity = Vector2.zero;
            lastStepPosition = feet;
        }

        // ==========================================
        // #4. 유니티 생명주기
        // ==========================================

        void Awake()
        {
            body = GetComponent<Rigidbody2D>();

            // 이 이동 방식의 전제 조건을 코드에서 직접 정한다.
            // Inspector에서 누가 실수로 바꿔도 플레이를 시작하면 여기서 바로잡힌다.
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = 0f;                              // 중력 없음. 높이는 층이 정한다
            body.interpolation = RigidbodyInterpolation2D.Interpolate;  // 물리 틱 사이를 부드럽게 보간
            ApplyWalkingConstraints();
            lastStepPosition = body.position;
        }

        // FixedUpdate: 물리 엔진의 고정 간격(기본 0.02초)마다 불린다.
        // Rigidbody를 움직이는 코드는 Update가 아니라 여기에 둔다.
        void FixedUpdate()
        {
            // 지난 틱의 이동 결과로 실제 속도를 잰다. 물리 위치는 틱마다만 바뀌므로 틱 단위로 재야 정확하다.
            Velocity = (body.position - lastStepPosition) / Time.fixedDeltaTime;
            lastStepPosition = body.position;

            if (IsClimbing)
            {
                Climb(moveInput.y);
            }
            else if (!TryStartClimb(moveInput.y))
            {
                Walk(moveInput.x);
            }
        }

        // ==========================================
        // #5. 걷기
        // ==========================================

        void Walk(float horizontal)
        {
            // Unity 6에서 Rigidbody2D.velocity는 linearVelocity로 이름이 바뀌었다.
            body.linearVelocity = new Vector2(horizontal * moveSpeed, 0f);
        }

        void ApplyWalkingConstraints()
        {
            // 회전과 세로 이동을 잠근다. 걸을 때는 벽에 부딪혀도 위아래로 밀리지 않는다.
            // | 는 비트 OR. C에서 플래그를 합치는 것과 같다.
            body.constraints = RigidbodyConstraints2D.FreezeRotation | RigidbodyConstraints2D.FreezePositionY;
        }

        // ==========================================
        // #6. 계단
        // ==========================================

        // 위 입력이면 근처 계단의 아래 끝에서, 아래 입력이면 위 끝에서 오르기 시작한다.
        bool TryStartClimb(float vertical)
        {
            if (Mathf.Abs(vertical) < verticalInputDeadzone) return false;

            foreach (var stairs in Stairs.All)
            {
                if (vertical > 0f && stairs.IsNearBottom(FeetPosition))
                {
                    StartClimb(stairs, 0f);
                    return true;
                }
                if (vertical < 0f && stairs.IsNearTop(FeetPosition))
                {
                    StartClimb(stairs, 1f);
                    return true;
                }
            }
            return false;
        }

        void StartClimb(Stairs stairs, float startProgress)
        {
            currentStairs = stairs;
            climbProgress = startProgress;

            // Kinematic: 물리 엔진이 밀거나 막지 않고, 코드가 주는 위치로만 움직인다.
            // 계단 경로가 위층 난간 충돌체를 지나가도 걸리지 않게 하려는 것.
            body.linearVelocity = Vector2.zero;
            body.bodyType = RigidbodyType2D.Kinematic;

            // 걸을 때 건 세로 잠금(FreezePositionY)을 푼다.
            // 잠금은 Kinematic의 MovePosition에도 적용되므로, 풀지 않으면 x만 계단을 따라가고 y는 멈춘다.
            // (실제로 이 버그가 있었다: 옆으로 걷다가 도착 순간 위층으로 순간이동)
            body.constraints = RigidbodyConstraints2D.FreezeRotation;

            // 계단 끝점으로 붙인다. 입구 범위 안에서 조금 떨어져 있었다면 살짝 순간이동한다. [가정]
            body.MovePosition(stairs.PointAt(climbProgress));
        }

        void Climb(float vertical)
        {
            // 위 입력이면 위로, 아래 입력이면 아래로, 손을 떼면 그 자리에 멈춘다.
            // 진행도 변화량 = 속도 / 계단 길이 × 경과 시간. 계단이 길어도 실제 속도는 같다.
            climbProgress += vertical * climbSpeed / currentStairs.Length * Time.fixedDeltaTime;

            if (climbProgress <= 0f)
            {
                FinishClimb(0f);
            }
            else if (climbProgress >= 1f)
            {
                FinishClimb(1f);
            }
            else
            {
                body.MovePosition(currentStairs.PointAt(climbProgress));
            }
        }

        void FinishClimb(float endProgress)
        {
            Vector2 endPosition = currentStairs.PointAt(endProgress);
            currentStairs = null;

            // 다시 물리 엔진에 맡긴다. 도착한 층의 높이에서 y가 잠긴다.
            body.bodyType = RigidbodyType2D.Dynamic;
            body.position = endPosition;
            body.linearVelocity = Vector2.zero;
            ApplyWalkingConstraints();
        }
    }
}
