using UnityEngine;

namespace Improv.Npc
{
    /// <summary>
    /// NPC 캐릭터 시트 (GDD 3장 2번). 겉으로 드러난 모습과 숨긴 비밀을 나눠 적는다.
    /// MVP에서는 손으로 쓴 시트를 쓴다. 자동 생성은 나중 (GDD 2장).
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
    }
}
