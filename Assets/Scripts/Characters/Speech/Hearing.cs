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
        public bool CanHear(SpeechEvent speech)
        {
            Vector2 ear = EarPosition;
            if (Vector2.Distance(ear, speech.Origin) > RangeFor(speech.Volume)) return false;

            // R-014b: 입에서 귀까지 선을 그어 벽이나 닫힌 문이 있으면 안 들린다.
            int count = Physics2D.Linecast(speech.Origin, ear, solidOnly, hitBuffer);
            for (int i = 0; i < count; i++)
            {
                if (!BlocksSound(hitBuffer[i].collider)) continue;
                return false;
            }
            return true;
        }

        // 상자 같은 운반 오브젝트와 캐릭터의 몸은 소리를 막지 않는다. 남는 것은 벽, 바닥, 닫힌 문 같은 구조물이다.
        static bool BlocksSound(Collider2D c)
        {
            if (c.GetComponentInParent<Carryable>() != null) return false;
            if (c.GetComponentInParent<CharacterMotor>() != null) return false;
            return true;
        }

        internal void Receive(SpeechEvent speech) => Heard?.Invoke(speech);
    }
}
