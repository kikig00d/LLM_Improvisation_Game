using System.Collections.Generic;
using UnityEngine;

namespace Improv.Npc.Generation
{
    /// <summary>
    /// 미리 써 둔 한 판의 사람들 (계획서 T-8). 시험할 때마다 과거를 새로 쓰면 로딩이 길어서(약 50초),
    /// CastGenerator에서 "프리셋 쓰기"를 켜면 이것을 그대로 끼우고 바로 시작한다.
    /// 생성한 판이 마음에 들면 CastGenerator의 ⋮ 메뉴 "만든 판을 프리셋으로 저장"으로 같은 형식으로 남길 수 있다.
    /// </summary>
    [CreateAssetMenu(fileName = "CastPreset", menuName = "Improv/Cast Preset")]
    public class CastPreset : ScriptableObject
    {
        [Tooltip("CastGenerator의 npcs 목록과 같은 순서로 끼운다")]
        public List<NpcCharacterSheet> sheets = new();

        [Tooltip("겹치는 과거의 진실 (월드 데이터, P-2). NPC의 LLM에는 넘기지 않는다. 나중에 관계도 창과 사건의 정답이 쓴다")]
        [TextArea(3, 10)] public List<string> truths = new();
    }
}
