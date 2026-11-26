using System;
using System.Collections.Generic;
using UnityEngine;

namespace Improv.Npc.Generation
{
    /// <summary>
    /// 캐릭터 시트를 자동으로 채울 때 고르는 목록 (계획서 T-8, IDEAS I-9·I-10·I-11).
    /// 림월드의 배경처럼 시기별(유년기, 성년기) 배경을 붙이고, 성격 특성과 믿는 것(도그마)을 고른다.
    /// 내용은 Claude 초안이다. Inspector에서 고치거나 줄을 더해 다듬는다.
    /// C로 치면 "고를 수 있는 값들의 표"를 파일에 저장해 둔 것이다. 코드는 표에서 무작위로 뽑기만 한다.
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterTables", menuName = "Improv/Character Tables")]
    public class CharacterTables : ScriptableObject
    {
        public List<NameEntry> names = new();
        public Vector2Int ageRange = new(22, 65);
        public List<Background> childhoods = new();
        public List<Background> adulthoods = new();
        public List<string> arrivalReasons = new();
        public List<Trait> traits = new();
        [Tooltip("한 사람에게 붙이는 특성 수")]
        public int traitsPerCharacter = 3;
        public List<string> beliefs = new();
        [Tooltip("한 사람에게 붙이는 믿음 수")]
        public int beliefsPerCharacter = 2;
        public List<PastKind> heavyPasts = new();   // 김전일식 극단적 갈등 (GDD 3장 4번)
        public List<PastKind> lightPasts = new();   // 가벼운 인연
        [Tooltip("능력치·기능 규칙 (R-031). 배경의 보정과 기능 이름은 이 규칙의 목록을 따른다")]
        public Improv.Characters.AbilityRules abilityRules;
    }

    // 아래는 표의 한 줄들. MonoBehaviour가 아닌 일반 클래스라 한 파일에 둔다.
    // [Serializable]: Inspector에 칸으로 보이고 에셋에 저장된다 (C의 struct 배열을 파일에 쓰는 것과 같다).

    [Serializable]
    public class NameEntry
    {
        public string name;
        public bool female;
    }

    [Serializable]
    public class Background
    {
        public string title;                    // 성년기는 이것이 곧 직업이다
        [TextArea(1, 3)] public string story;   // 그 시기에 무엇을 겪었나. LLM이 캐릭터를 연기할 바탕
        public string speechHint;               // 말투에 묻어나는 것 (비워도 된다)
        [Tooltip("한 판에 한 사람만 가질 수 있다 (산장 관리인 등)")]
        public bool unique;
        [Tooltip("비어 있지 않으면 산장에 온 이유가 이것으로 정해진다")]
        public string fixedArrival;
        [Tooltip("이 배경이 주는 능력치 보정 (R-031b). 예) 운동선수: 근력 +3, 체력 +2")]
        public List<Improv.Characters.AbilityBonus> abilityBonus = new();
        [Tooltip("이 배경이 잘하는 기능. 생성기와 주사위가 기능 점수를 여기부터 쓴다")]
        public List<string> skills = new();
    }

    [Serializable]
    public class Trait
    {
        public string name;
        [TextArea(1, 2)] public string description;
        [Tooltip("평소 집중도(T-5)에 더하는 값. 예민하면 +, 무심하면 -")]
        public float attention;
        [Tooltip("같이 붙을 수 없는 특성 이름")]
        public List<string> conflicts = new();
    }

    [Serializable]
    public class PastKind
    {
        public string title;
        [TextArea(1, 3)] public string hint;   // LLM에게 줄 사건의 뼈대
        [Tooltip("두 사람의 나이 차가 이보다 크면 고르지 않는다 (동창 등). 0이면 상관없음")]
        public int maxAgeGap;
        [Tooltip("둘 다 학생(10~18살)이던 때의 일이다 (동창 등)")]
        public bool youth;
    }
}
