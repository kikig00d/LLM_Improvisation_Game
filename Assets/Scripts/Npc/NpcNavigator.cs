using System;
using Improv.Characters;
using Improv.World;
using UnityEngine;

namespace Improv.Npc
{
    /// <summary>
    /// NPC의 길 찾기 조종자 (계획서 T-6). 목표 지점까지 플레이어와 같은 몸(CharacterMotor)을 SetMoveInput으로 움직인다.
    /// "층 + x좌표" 세계라 길 찾기가 단순하다:
    /// - 같은 층이면 x만 맞추면 된다.
    /// - 다른 층이면 그 두 층을 잇는 계단 입구까지 걸어가서 위/아래 입력으로 오른다.
    /// - 닫힌 문에 막히면 손(CharacterInteractor)으로 연다. 사람 손과 같은 거리·같은 판정이다.
    /// - 목표가 다른 공간(떨어진 개인실, R-030)이면 먼저 그 길의 문(RoomPortal)까지 가서 손으로 쓴다.
    /// - 다른 것(상자 등)에 오래 막히면 포기하고 실패를 알린다. 실패도 NPC가 겪는 일이다("길이 막혀 못 갔다").
    /// 어디로 갈지는 정하지 않는다. NpcActions가 LLM의 선택에 따라 GoTo를 부른다.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor), typeof(CharacterInteractor))]
    public class NpcNavigator : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [Tooltip("계단 입구에 이만큼 가까워지면 오르기 시작한다 (계단 입구 허용 폭보다 작게)")]
        [SerializeField] float stairsEntryTolerance = 0.3f;
        [Tooltip("이 시간(게임 초) 동안 거의 못 움직이면 막힌 것으로 본다")]
        [SerializeField] float blockedAfter = 0.4f;
        [Tooltip("막힌 채 이 시간이 지나면 포기한다")]
        [SerializeField] float giveUpAfter = 3f;
        [Tooltip("막혔을 때 이 거리 안의 닫힌 문은 열어 본다")]
        [SerializeField] float doorProbeDistance = 1.0f;
        [Tooltip("떨어진 공간의 문(RoomPortal)에 이만큼 가까워지면 쓴다 (손이 닿는 거리보다 작게)")]
        [SerializeField] float portalUseDistance = 0.6f;

        // =====================================================================
        // #2. 상태
        // =====================================================================

        public enum State { Idle, Moving, Arrived, Failed }

        CharacterMotor motor;
        CharacterInteractor interactor;

        Func<Vector2?> goal;        // 목표 위치를 매 프레임 묻는 함수. 사람처럼 움직이는 목표도 따라간다. null을 주면 목표를 놓친 것
        float stopDistance;         // 목표와 이 거리 안이면 도착
        Func<Vector2, bool> arrivedWhen;   // 있으면, 목표 지점에 닿기 전이라도 이 판정이 참이면 도착 (방에 들어섰다 등). C의 함수 포인터
        float climbDirection;       // 계단을 오르는 중이면 +1(위) / -1(아래)
        float blockedTime;

        public State Status { get; private set; } = State.Idle;
        public string FailReason { get; private set; }
        public bool IsMoving => Status == State.Moving;

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            interactor = GetComponent<CharacterInteractor>();
        }

        void OnDisable() => Stop();

        void Update()
        {
            if (Status != State.Moving) return;

            // 계단 위에서는 끝날 때까지 같은 방향으로 계속 누른다.
            if (motor.IsClimbing) { motor.SetMoveInput(new Vector2(0f, climbDirection)); return; }
            climbDirection = 0f;

            Vector2? maybeGoal = goal?.Invoke();
            if (!maybeGoal.HasValue) { Fail("대상을 놓쳤다"); return; }
            Vector2 target = maybeGoal.Value;
            Vector2 feet = motor.FeetPosition;
            if (arrivedWhen != null && arrivedWhen(feet)) { Arrive(); return; }   // 계단을 다 오른 뒤에만 본다(위에서 return)

            // 다른 공간이면 목표 대신 그 길의 문으로 간다. 문을 지나면 다음 프레임에 다시 판단한다.
            RoomPortal portal = RoomPortal.NextOnWay(RoomPortal.SpaceOf(feet), RoomPortal.SpaceOf(target));
            if (portal != null) target = portal.Position;

            float myFloor = FloorOf(feet.y), targetFloor = FloorOf(target.y);
            Vector2 waypoint = target;
            float tolerance = portal != null ? portalUseDistance : stopDistance;   // 문으로 갈 때는 문 앞까지 붙는다
            if (!Mathf.Approximately(myFloor, targetFloor))
            {
                // 다른 층: 두 층을 잇는 계단을 찾아 입구로 간다.
                Stairs stairs = FindStairs(myFloor, targetFloor);
                if (stairs == null) { Fail("그 층으로 가는 길이 없다"); return; }
                bool up = targetFloor > myFloor;
                if (up ? stairs.IsNearBottom(feet) : stairs.IsNearTop(feet))
                {
                    climbDirection = up ? 1f : -1f;
                    motor.SetMoveInput(new Vector2(0f, climbDirection));
                    blockedTime = 0f;
                    return;
                }
                waypoint = up ? stairs.Bottom : stairs.Top;
                tolerance = stairsEntryTolerance;
            }

            float dx = waypoint.x - feet.x;
            if (portal != null && Mathf.Approximately(myFloor, targetFloor) && Mathf.Abs(dx) <= portalUseDistance)
            {
                // 문 앞에 왔다. 사람과 같은 손으로 연다(거리·벽 판정 그대로).
                motor.SetMoveInput(Vector2.zero);
                var result = interactor.TryInteract(portal);
                if (result != InteractionResult.Done && result != InteractionResult.Busy) Fail("문으로 들어가지 못했다");
                return;
            }
            if (Mathf.Abs(dx) <= tolerance)
            {
                if (Mathf.Approximately(myFloor, targetFloor) && portal == null) { Arrive(); return; }
                motor.SetMoveInput(Vector2.zero);   // 계단 입구에 닿았다. 다음 프레임에 오르기 시작한다
                return;
            }

            motor.SetMoveInput(new Vector2(Mathf.Sign(dx), 0f));
            CheckBlocked(feet, Mathf.Sign(dx));
        }

        // =====================================================================
        // #4. 바깥에서 부르는 것
        // =====================================================================

        /// <summary>움직이지 않는 지점으로 간다.</summary>
        public void GoTo(Vector2 point, float stop) => GoTo(() => point, stop);

        /// <summary>
        /// 목표 위치를 매번 묻는 함수로 간다. 사람을 따라갈 때 쓴다. 함수가 null을 돌려주면 실패한다.
        /// arrived를 주면 목표 지점은 "걸어갈 방향"일 뿐이고, 그 판정이 참이 되는 순간 도착이다 (방 출입).
        /// </summary>
        public void GoTo(Func<Vector2?> goalGetter, float stop, Func<Vector2, bool> arrived = null)
        {
            goal = goalGetter;
            arrivedWhen = arrived;
            stopDistance = Mathf.Max(0.05f, stop);
            blockedTime = 0f;
            FailReason = null;
            Status = State.Moving;
        }

        public void Stop()
        {
            if (Status == State.Moving) Status = State.Idle;
            if (motor != null && !motor.IsClimbing) motor.SetMoveInput(Vector2.zero);
        }

        // =====================================================================
        // #5. 도우미
        // =====================================================================

        void Arrive()
        {
            motor.SetMoveInput(Vector2.zero);
            Status = State.Arrived;
        }

        void Fail(string reason)
        {
            motor.SetMoveInput(Vector2.zero);
            FailReason = reason;
            Status = State.Failed;
        }

        void CheckBlocked(Vector2 feet, float direction)
        {
            if (Mathf.Abs(motor.Velocity.x) > 0.05f) { blockedTime = 0f; return; }
            blockedTime += Time.deltaTime;
            if (blockedTime < blockedAfter) return;

            // 앞에 닫힌 문이 있으면 연다. 사람도 문 앞에서 손잡이를 돌린다.
            foreach (var door in FindObjectsByType<Door>(FindObjectsSortMode.None))
            {
                if (door.IsOpen) continue;
                Vector2 p = door.ClosestPoint(feet);
                bool ahead = Mathf.Sign(p.x - feet.x) == direction || Mathf.Abs(p.x - feet.x) < 0.1f;
                if (ahead && Mathf.Abs(p.x - feet.x) <= doorProbeDistance && Mathf.Abs(FloorOf(door.transform.position.y) - FloorOf(feet.y)) < 0.5f)
                {
                    var result = interactor.TryInteract(door);
                    if (result == InteractionResult.Done) { blockedTime = 0f; return; }
                }
            }
            if (blockedTime >= giveUpAfter) Fail("길이 막혀 못 갔다");
        }

        /// <summary>그 높이가 몇 층 바닥인가. 계단 끝점들의 높이가 곧 층 바닥 높이다. 가장 가까운 바닥 높이를 돌려준다.</summary>
        public static float FloorOf(float y)
        {
            float best = y, bestGap = float.MaxValue;
            foreach (var s in Stairs.All)
            {
                foreach (float level in new[] { s.Bottom.y, s.Top.y })
                {
                    float gap = Mathf.Abs(level - y);
                    if (gap < bestGap) { bestGap = gap; best = level; }
                }
            }
            return best;
        }

        static Stairs FindStairs(float fromFloor, float toFloor)
        {
            foreach (var s in Stairs.All)
            {
                float b = FloorOf(s.Bottom.y), t = FloorOf(s.Top.y);
                if ((Mathf.Approximately(b, fromFloor) && Mathf.Approximately(t, toFloor)) ||
                    (Mathf.Approximately(t, fromFloor) && Mathf.Approximately(b, toFloor))) return s;
            }
            return null;
        }
    }
}
