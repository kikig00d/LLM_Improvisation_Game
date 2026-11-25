using System.Collections.Generic;
using UnityEngine;

namespace Improv.Npc
{
    /// <summary>
    /// NPC 캐릭터 시트 (GDD 3장 2번). 겉으로 드러난 모습과 숨긴 비밀을 나눠 적는다.
    /// 손으로 쓴 시트(에셋)도 되고, 매 판 시작할 때 CastGenerator가 코드로 만든 시트도 된다 (계획서 T-8).
    /// </summary>
    [CreateAssetMenu(fileName = "NpcSheet", menuName = "Improv/NPC Character Sheet")]
    public class NpcCharacterSheet : ScriptableObject
    {
        [Header("겉으로 드러난 것")]
        public string displayName = "이름";
        [TextArea(3, 8)] public string publicProfile = "나이, 직업, 성격";

        [Header("숨긴 것 (비워도 된다. 비면 프롬프트에 넣지 않는다)")]
        [TextArea(3, 8)] public string secret = "";

        [Header("집중 (T-5)")]
        [Tooltip("아무 일 없을 때 돌아가는 평소 집중도 (0~1). 무심한 사람은 낮고, 예민하거나 책임감 강한 사람은 높다. LLM이 사건마다 올리거나 내려도 시간이 지나면 이 값으로 돌아온다.")]
        [Range(0f, 1f)] public float baselineAttention = 0.3f;

        [Header("말투")]
        [TextArea(3, 8)] public string speechStyle = "아주 짧고 투박한 문장.";

        // 아래는 자동 생성(T-8, CastGenerator)이 채운다. 손으로 쓴 시트는 비워 두면 프롬프트에 안 들어간다.
        [Header("믿는 것 (도그마, IDEAS I-9). 같은 일을 이 렌즈로 해석한다")]
        public List<string> beliefs = new();

        [Header("아는 사람과의 과거 (I-11). 이 캐릭터가 기억하는 판본이다. 진실과 다를 수 있다")]
        [TextArea(2, 6)] public List<string> pasts = new();
    }
}
