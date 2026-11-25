using System;
using System.Collections.Generic;

namespace Improv.Npc.Generation
{
    // =====================================================================
    // 한 판에 만들어진 사람들과 그들의 과거 (계획서 T-8). 일반 데이터 클래스라 한 파일에 모은다.
    // 진실(PastTruth)은 월드 데이터다(P-2). NPC의 LLM에는 각자의 판본(PastVersion)만 넘긴다(P-3).
    // 나중에 관계도 창(T-9)과 사건의 정답이 이 진실을 쓴다.
    // =====================================================================

    /// <summary>코드가 표에서 뽑아 채운 한 사람. 시트(NpcCharacterSheet)를 만들기 전의 재료다.</summary>
    public class CastMember
    {
        public NpcDialogueBrain brain;
        public int id;                       // CharacterIdentity.Id. 사람은 이름이 아니라 id로 구분한다 (F-17)
        public string name;
        public bool female;
        public int age;
        public Background childhood;
        public Background adulthood;
        public string arrival;
        public readonly List<Trait> traits = new();
        public readonly List<string> beliefs = new();
        public float attention;
    }

    /// <summary>두 사람이 함께 겪은 과거 하나. 진실 하나와 당사자 둘의 판본.</summary>
    public class SharedPast
    {
        public string kind;
        public bool heavy;                   // 김전일식 짙은 갈등이면 true, 가벼운 인연이면 false
        public CastMember a, b;
        public PastTruth truth;
        public PastVersion versionA, versionB;
        public bool fromLlm;                 // LLM이 실패해 코드가 대신 쓴 것이면 false

        public PastVersion VersionOf(CastMember m) => m == a ? versionA : m == b ? versionB : null;
        public CastMember Other(CastMember m) => m == a ? b : m == b ? a : null;
    }

    /// <summary>실제로 일어난 일. 육하원칙 (IDEAS I-11).</summary>
    [Serializable]
    public class PastTruth
    {
        public string who, when, where, what, how, why;

        public override string ToString() => $"누가: {who} / 언제: {when} / 어디서: {where} / 무엇을: {what} / 어떻게: {how} / 왜: {why}";
    }

    /// <summary>한 사람이 기억하는 그 일. 성격·믿음·충격으로 왜곡될 수 있다.</summary>
    [Serializable]
    public class PastVersion
    {
        public string name;
        public string memory;    // 1인칭 기억 2~3문장
        public string feeling;   // 지금 상대에게 품은 마음
    }
}
