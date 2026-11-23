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

        [Header("거리 노이즈 (R-014c, O-009 [가정])")]
        [Tooltip("들리는 거리의 몇 배 지점부터 말이 뭉개지기 시작하나. 0.6이면 60% 지점부터")]
        [SerializeField, Range(0f, 1f)] float noiseStartRatio = 0.6f;
        [Tooltip("들리는 거리 끝에서 글자 하나가 뭉개질 확률")]
        [SerializeField, Range(0f, 1f)] float maxNoise = 0.7f;
        [Tooltip("뭉개진 글자 대신 보일 기호")]
        [SerializeField] string noiseSymbol = "○";

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

        // =====================================================================
        // #5. 거리 노이즈 (R-014c)
        // =====================================================================

        /// <summary>
        /// 0이면 또렷하고, 클수록 글자가 많이 뭉개진다.
        /// 들리는 거리(장애물로 줄어든 뒤)에 대한 비율로 정하므로, 벽 너머에서는 가까워도 뭉개진다.
        /// 예: 들리는 거리 6, 시작 60%, 최대 70% → 3.6까지 또렷, 4.8에서 35%, 6에서 70%.
        /// </summary>
        public float NoiseLevel(float distance, float effectiveRange)
        {
            if (effectiveRange <= 0f) return 1f;
            float ratio = distance / effectiveRange;
            if (ratio <= noiseStartRatio) return 0f;
            // InverseLerp: start~1 사이에서 지금 어디쯤인지를 0~1로. C로 쓰면 (ratio - start) / (1 - start).
            return Mathf.InverseLerp(noiseStartRatio, 1f, ratio) * maxNoise;
        }

        /// <summary>
        /// 이 말이 내 귀에 어떻게 들리는지. 못 들으면 null, 멀면 글자가 뭉개진 사본을 돌려준다.
        /// 아무것도 바꾸지 않는 미리보기라서 테스트에서도 그대로 부를 수 있다.
        /// </summary>
        public SpeechEvent Perceive(SpeechEvent speech)
        {
            float distance = Vector2.Distance(EarPosition, speech.Origin);
            float range = EffectiveRange(speech);
            if (distance > range) return null;

            float noise = NoiseLevel(distance, range);
            if (noise <= 0f) return speech;

            // 같은 말을 같은 귀가 들으면 늘 같은 자리가 뭉개지도록 씨앗을 고정한다 (다시 불러도 결과가 같다).
            // 귀가 다르면 씨앗이 달라서, 두 사람이 같은 거리에서 들어도 서로 다른 글자를 놓친다.
            int seed = speech.GetHashCode() ^ GetInstanceID();
            return speech.WithText(Muffle(speech.Text, noise, new System.Random(seed)));
        }

        /// <summary>
        /// 글자마다 noise 확률로 기호로 바꾼다. 띄어쓰기와 문장부호(? ! …)는 남긴다.
        /// 말투와 길이, 묻는 말인지는 들리지만 내용 일부를 놓치는 느낌을 내려는 것이다.
        /// </summary>
        string Muffle(string text, float noise, System.Random rng)
        {
            var sb = new System.Text.StringBuilder(text.Length);
            foreach (char c in text)
            {
                bool keep = char.IsWhiteSpace(c) || char.IsPunctuation(c) || rng.NextDouble() >= noise;
                if (keep) sb.Append(c);
                else sb.Append(noiseSymbol);
            }
            return sb.ToString();
        }

        /// <summary>SpeechSystem이 부른다. 들리면 (뭉개졌을 수도 있는) 들린 그대로를 알린다.</summary>
        internal void TryReceive(SpeechEvent speech)
        {
            SpeechEvent heard = Perceive(speech);
            if (heard == null) return;
            if (heard != speech)
                Debug.Log($"[노이즈] {name}에게는 이렇게 들림: {heard.Speaker.DisplayName}: {heard.Text}", this);
            Heard?.Invoke(heard);
        }
    }
}
