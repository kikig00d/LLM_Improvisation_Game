using System;
using Improv.World;
using UnityEngine;

namespace Improv.Characters
{
    /// <summary>
    /// 캐릭터가 지금 어느 방에 있는지 추적한다. 플레이어와 NPC 모두에 붙인다. (설계 원칙 P-1)
    /// 방이 바뀌면 이벤트를 보내고, 로그를 켜 두면 Console에도 남긴다.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor))]
    public class RoomPresence : MonoBehaviour
    {
        // ==========================================
        // #1. 설정값
        // ==========================================

        [SerializeField] float checkInterval = 0.2f;   // 몇 초마다 방을 확인할지. 매 프레임 할 필요는 없다
        [SerializeField] bool logRoomChanges = true;   // 방이 바뀔 때 Console에 남길지 (디버그용)

        // 발 위치는 바닥선과 정확히 겹쳐서 아래층 방에 걸칠 수 있다. 살짝 위를 기준으로 판정한다.
        const float FeetProbeHeight = 0.1f;

        // ==========================================
        // #2. 상태와 이벤트
        // ==========================================

        CharacterMotor motor;
        float timeUntilNextCheck;

        public Room CurrentRoom { get; private set; }

        // 방이 바뀌면 (이전 방, 새 방)을 알린다. C의 함수 포인터 목록과 비슷하다.
        // 나중에 시야, 기억, UI가 여기에 구독한다.
        public event Action<Room, Room> RoomChanged;

        // ==========================================
        // #3. 유니티 생명주기
        // ==========================================

        void Awake() => motor = GetComponent<CharacterMotor>();

        // Start: 첫 프레임 직전에 한 번. 모든 Room이 OnEnable로 목록에 등록된 뒤라서 안전하게 찾을 수 있다.
        void Start() => Refresh();

        void Update()
        {
            // Time.deltaTime: 지난 프레임부터 흐른 시간(초). 프레임 수가 아니라 시간 기준으로 센다.
            timeUntilNextCheck -= Time.deltaTime;
            if (timeUntilNextCheck > 0f) return;

            timeUntilNextCheck = checkInterval;
            Refresh();
        }

        // ==========================================
        // #4. 방 판정
        // ==========================================

        void Refresh()
        {
            var probe = motor.FeetPosition + Vector2.up * FeetProbeHeight;
            var room = Room.FindAt(probe);
            if (room == CurrentRoom) return;

            var previous = CurrentRoom;
            CurrentRoom = room;

            if (logRoomChanges)
            {
                // ?. 는 null이면 멈추고 null을 반환, ?? 는 왼쪽이 null이면 오른쪽 값을 쓴다.
                Debug.Log($"[{name}] {previous?.DisplayName ?? "바깥"} → {room?.DisplayName ?? "바깥"}", this);
            }

            RoomChanged?.Invoke(previous, room);
        }
    }
}
