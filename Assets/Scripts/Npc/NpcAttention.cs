using Improv.Llm;
using UnityEngine;

namespace Improv.Npc
{
    /// <summary>
    /// NPC의 집중도 (T-5, 계획서 Q-7). 0~1의 값 하나로, "LLM이 이 NPC의 판단에 공을 얼마나 들이나"를 정한다.
    /// - 소 사건(여럿이 모임, 창고에 식량이 없음 등)을 이 캐릭터가 얼마나 신경 쓰는지는 LLM이 답할 때 직접 고른다(attention).
    ///   대충 넘기는 성격이면 낮게(뇌 빼고 판단), 편집증적이면 높게(깊이 생각) 고른다.
    /// - 아무 일 없으면 캐릭터 시트의 평소 집중도로 서서히 돌아간다 (구상 노트: attention = baseline + (attention - baseline) * decay).
    /// - 말은 집중도와 상관없이 그대로 들린다(사용자 결정). 집중도는 듣기가 아니라 "생각하는 노력"만 바꾼다.
    /// - 들은 말에 내 이름이 있으면 집중도가 저절로 오른다. 사람이 제 이름에 귀가 쫑긋하는 것과 같은 지각이고,
    ///   대답할지는 여전히 LLM이 고른다 (R-016b).
    /// </summary>
    public class NpcAttention : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값 (O-011 [가정])
        // =====================================================================

        [Tooltip("이 값보다 낮으면 '낮음': 생각 끔, 최근 말 몇 줄만 보고 짧게 판단한다.")]
        [SerializeField, Range(0f, 1f)] float lowBelow = 0.35f;
        [Tooltip("이 값 이상이면 '높음': LLM의 생각(reasoning)을 켜고 공들여 판단한다. 몇 초 더 걸린다.")]
        [SerializeField, Range(0f, 1f)] float highFrom = 0.7f;
        [Tooltip("평소 집중도와의 차이가 절반으로 줄어드는 게임 시간 (초).")]
        [SerializeField] float halfLifeSeconds = 60f;
        [Tooltip("들은 말에 내 이름이 있으면 집중도가 적어도 이 값까지 오른다.")]
        [SerializeField, Range(0f, 1f)] float nameCallLevel = 0.6f;

        [Header("단계별 노력")]
        [Tooltip("'낮음'일 때 LLM에 넘기는 최근 기억 줄 수")]
        [SerializeField] int lowHistoryLines = 6;
        [SerializeField] int lowMaxTokens = 120;
        [SerializeField] int normalMaxTokens = 160;
        [Tooltip("생각을 켜면 생각 글자도 토큰 상한에 들어간다. 모자라면 대사가 비어 실패한다(E-005). T-5 시험에서 800은 모자랐다.")]
        [SerializeField] int highMaxTokens = 2000;
        [SerializeField] string highReasoningEffort = "low";

        // =====================================================================
        // #2. 상태
        // =====================================================================

        public enum Level { Low, Normal, High }

        float value;
        float baseline = 0.3f;

        public float Value => value;
        public float Baseline => baseline;
        public Level Current => value < lowBelow ? Level.Low : value >= highFrom ? Level.High : Level.Normal;

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Update()
        {
            // 지수 감쇠: 매 프레임 "평소와의 차이"에 같은 비율을 곱해 줄인다. 반감기마다 차이가 절반이 된다.
            // Time.deltaTime은 게임 시간이라 멈추면 그대로, 빨리 감으면 빨리 식는다 (GameClock).
            float keep = Mathf.Pow(0.5f, Time.deltaTime / Mathf.Max(0.01f, halfLifeSeconds));
            value = baseline + (value - baseline) * keep;
        }

        // =====================================================================
        // #4. 바꾸기
        // =====================================================================

        /// <summary>두뇌가 시작할 때 캐릭터 시트의 평소 집중도로 맞춘다.</summary>
        public void Init(float baselineAttention)
        {
            baseline = Mathf.Clamp01(baselineAttention);
            value = baseline;
        }

        /// <summary>LLM이 고른 집중도. 이 사건을 얼마나 신경 쓰는가.</summary>
        public void Set(float attention) => value = Mathf.Clamp01(attention);

        /// <summary>내 이름이 들렸다. 적어도 nameCallLevel까지 올린다 (이미 더 높으면 그대로).</summary>
        public void OnNameCalled() => value = Mathf.Max(value, nameCallLevel);

        // =====================================================================
        // #5. 집중도 → 노력
        // =====================================================================

        /// <summary>지금 집중도로 LLM에 넘길 최근 기억 줄 수. 낮으면 앞일을 깊이 떠올리지 않는다.</summary>
        public int HistoryLines(int full) => Current == Level.Low ? Mathf.Min(full, lowHistoryLines) : full;

        /// <summary>지금 집중도로 이번 요청의 생각 단계와 답 길이.</summary>
        public LlmRequestOptions RequestOptions() => Current switch
        {
            Level.Low => new LlmRequestOptions { ReasoningEffort = "none", MaxTokens = lowMaxTokens },
            Level.High => new LlmRequestOptions { ReasoningEffort = highReasoningEffort, MaxTokens = highMaxTokens },
            _ => new LlmRequestOptions { ReasoningEffort = "none", MaxTokens = normalMaxTokens },
        };

        public string LevelName => Current switch { Level.Low => "낮음", Level.High => "높음", _ => "보통" };
    }
}
