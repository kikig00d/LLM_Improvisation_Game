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

        [Header("숨긴 것")]
        [TextArea(3, 8)] public string secret = "들키기 싫은 비밀";

        [Header("말투")]
        [TextArea(3, 8)] public string speechStyle = "아주 짧고 투박한 문장.";
    }
}
