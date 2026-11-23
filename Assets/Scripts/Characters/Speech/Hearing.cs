using System;
using System.Collections.Generic;
using Improv.World;
using UnityEngine;

namespace Improv.Characters.Speech
{
    /// <summary>
    /// 캐릭터의 귀. 플레이어와 NPC가 같은 규칙으로 듣는다 (P-1, R-014a, R-014b).
    /// 플레이어의 대화 기록도, NPC가 LLM에 넘기는 내용도 모두 이 귀를 통과한 말뿐이다 (P-3).
    /// </summary>
    [RequireComponent(typeof(CharacterMotor))]
    public class Hearing : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [SerializeField] float normalRange = 6f;        // 보통 목소리가 들리는 거리 (유닛). O-006 [가정]
        [SerializeField] float whisperMultiplier = 0.3f; // O-006 [미정]. M4에서는 쓰지 않는다
        [SerializeField] float shoutMultiplier = 2.5f;   // O-006 [미정]. M4에서는 쓰지 않는다
        [SerializeField] float earHeight = 1.5f;         // 발에서 귀까지

        [Header("통과율 (R-014b, O-007 [가정])")]
        [Tooltip("SoundObstacle이 없는 구조물(벽 등)의 통과율")]
        [SerializeField, Range(0f, 1f)] float wallTransmission = 0.3f;
        [SerializeField, Range(0f, 1f)] float closedDoorTransmission = 0.5f;
        [Tooltip("층 하나를 지날 때마다 곱하는 통과율")]
        [SerializeField, Range(0f, 1f)] float ceilingTransmission = 0.6f;   // 층 높이가 3이라 0.5 아래면 바로 위층도 못 듣는다
        [SerializeField] float floorHeight = 3f;          // 한 층의 높이. 몇 층을 지나는지 셀 때 쓴다

        // =====================================================================
        // #2. 상태
        // =====================================================================

        public static readonly List<Hearing> All = new();

        /// <summary>말을 들었을 때 알림. 플레이어는 대화 기록에, NPC는 두뇌에 연결한다.</summary>
        public event Action<SpeechEvent> Heard;

        CharacterMotor motor;
        readonly List<RaycastHit2D> hitBuffer = new();
        ContactFilter2D solidOnly;

        public Vector2 EarPosition => motor.FeetPosition + Vector2.up * earHeight;

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            // 트리거(문 클릭 영역 등)는 소리를 막지 않는다. CharacterInteractor와 같은 방식.
            solidOnly = ContactFilter2D.noFilter;
            solidOnly.useTriggers = false;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        // =====================================================================
        // #4. 판정
        // =====================================================================

        public float RangeFor(SpeechVolume volume) => volume switch
        {
            SpeechVolume.Whisper => normalRange * whisperMultiplier,
            SpeechVolume.Shout => normalRange * shoutMultiplier,
            _ => normalRange
        };

        /// <summary>이 말이 내 귀에 닿는지. 아무것도 바꾸지 않는 미리보기다.</summary>
        public bool CanHear(SpeechEvent speech) =>
            Vector2.Distance(EarPosition, speech.Origin) <= EffectiveRange(speech);

        /// <summary>
        /// 장애물을 지나며 줄어든 뒤의 들리는 거리 (R-014b).
        /// 예: 보통 6유닛, 벽 하나(30%)와 천장 하나(40%)를 지나면 6 × 0.3 × 0.4 = 0.72유닛.
        /// </summary>
        public float EffectiveRange(SpeechEvent speech)
        {
            Vector2 ear = EarPosition;
            float range = RangeFor(speech.Volume);

            // 천장: 지나는 층 수만큼 곱한다. 입과 귀 모두 발에서 같은 높이라 층 번호를 그대로 비교할 수 있다.
            int floorsCrossed = Mathf.Abs(FloorIndex(speech.Origin.y) - FloorIndex(ear.y));
            range *= Mathf.Pow(ceilingTransmission, floorsCrossed);

            // 벽과 문: 입에서 귀까지 선을 그어 걸리는 장애물마다 곱한다.
            int count = Physics2D.Linecast(speech.Origin, ear, solidOnly, hitBuffer);
            for (int i = 0; i < count; i++)
                range *= TransmissionOf(hitBuffer[i].collider);
            return range;
        }

        int FloorIndex(float y) => Mathf.FloorToInt(y / floorHeight);

        float TransmissionOf(Collider2D c)
        {
            // 상자 같은 운반 오브젝트와 캐릭터의 몸은 소리를 막지 않는다.
            if (c.GetComponentInParent<Carryable>() != null) return 1f;
            if (c.GetComponentInParent<CharacterMotor>() != null) return 1f;

            var obstacle = c.GetComponentInParent<SoundObstacle>();
            if (obstacle != null) return obstacle.Transmission;

            // 문은 닫혀 있을 때만 막는 충돌체가 켜져 있으므로, 여기까지 왔다면 닫힌 문이다.
            if (c.GetComponentInParent<Door>() != null) return closedDoorTransmission;
            return wallTransmission;
        }

        internal void Receive(SpeechEvent speech) => Heard?.Invoke(speech);
    }
}
