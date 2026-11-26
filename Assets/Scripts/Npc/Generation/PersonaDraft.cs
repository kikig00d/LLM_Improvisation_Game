using System;
using System.Collections.Generic;
using UnityEngine;

namespace Improv.Npc.Generation
{
    // =====================================================================
    // 캐릭터 메이킹 양식에 적은 것 (IDEAS I-11, GDD R-029).
    // 빈칸 시험지의 답안지다. 다 쓰면 NPC와 같은 재료(CastMember)로 바뀐다.
    // 지금은 플레이어만 쓰지만, 나중에 플레이어가 커스텀 NPC를 만들 때도 같은 양식을 쓴다 (사용자 2026-11-26).
    // [Serializable]이라 JsonUtility로 그대로 LLM에 보내고 받을 수 있다 ("LLM이 빈칸 채워 줘").
    // =====================================================================

    /// <summary>문장 틀 하나를 고르고 빈칸을 채운 것. t = 틀 번호(-1이면 안 고름), b = 빈칸에 친 말.</summary>
    [Serializable]
    public class TemplatePick
    {
        public int t = -1;
        public string[] b = new string[0];

        public bool IsEmpty => t < 0;

        /// <summary>채운 문장. 틀이 없거나 빈칸이 하나라도 비면 null.</summary>
        public string Text(List<string> templates) =>
            t >= 0 && t < templates.Count ? MakingTemplates.Fill(templates[t], b) : null;

        public void Choose(int template, List<string> templates)
        {
            t = template;
            b = new string[t >= 0 && t < templates.Count ? MakingTemplates.BlankLabels(templates[t]).Count : 0];
            for (int i = 0; i < b.Length; i++) b[i] = "";
        }
    }

    /// <summary>개인 서사 한 줄. 과거와 같은 규격(PastEvent)으로 바뀐다. 주어는 언제나 "나".</summary>
    [Serializable]
    public class PersonalLine
    {
        public string when = "", where = "";
        public TemplatePick what = new(), how = new(), why = new();

        public bool IsEmpty => string.IsNullOrWhiteSpace(when) && string.IsNullOrWhiteSpace(where) && what.IsEmpty && how.IsEmpty && why.IsEmpty;
    }

    [Serializable]
    public class PersonaDraft
    {
        public string name = "";
        public int age;                       // 0이면 빈칸
        public string gender = "";            // "남자", "여자", 빈칸
        public string childhood = "";         // CharacterTables.childhoods의 title
        public string adulthood = "";         // CharacterTables.adulthoods의 title (= 직업)
        public string family = "";            // MakingTemplates.familyPlaces 한 줄 ({남|여} 그대로)
        public string arrival = "";
        public string[] traits = new string[0];
        public string[] beliefs = new string[0];
        public PersonalLine[] personal = new PersonalLine[0];
        public string bond = "";              // ⑧ 산장의 누군가와의 인연. CharacterTables.lightPasts의 title, 빈칸이면 생성기가 고른다
        // ⑨ 능력치와 기능 (R-031c). [NonSerialized]: JSON으로 LLM에 보내지 않는다(빈칸 채우기는 글 칸만). set이 꺼져 있으면 시작할 때 주사위로.
        [NonSerialized] public Improv.Characters.AbilityScores abilities = new();

        public const string Male = "남자", Female = "여자";
        public bool IsFemale => gender == Female;

        /// <summary>배열 길이를 표에 맞춘다. JSON에서 읽거나 새로 만든 뒤 부른다 (C의 calloc 뒤 초기화처럼).</summary>
        public void EnsureShape(CharacterTables tables, MakingTemplates templates)
        {
            traits = Resize(traits, tables.traitsPerCharacter);
            beliefs = Resize(beliefs, tables.beliefsPerCharacter);
            var lines = new PersonalLine[templates.personalLines];
            for (int i = 0; i < lines.Length; i++) lines[i] = personal != null && i < personal.Length && personal[i] != null ? personal[i] : new PersonalLine();
            personal = lines;
            foreach (var line in personal)
            {
                line.when ??= ""; line.where ??= "";
                line.what ??= new TemplatePick(); line.how ??= new TemplatePick(); line.why ??= new TemplatePick();
            }
            abilities ??= new Improv.Characters.AbilityScores();
            abilities.EnsureShape();
            name ??= ""; gender ??= ""; childhood ??= ""; adulthood ??= ""; family ??= ""; arrival ??= ""; bond ??= "";
        }

        static string[] Resize(string[] src, int n)
        {
            var dst = new string[n];
            for (int i = 0; i < n; i++) dst[i] = src != null && i < src.Length && src[i] != null ? src[i] : "";
            return dst;
        }

        // =====================================================================
        // #1. 재료(CastMember)로 바꾸기
        // =====================================================================

        /// <summary>NPC와 같은 재료로 바꾼다 (P-1: 플레이어와 NPC가 같은 시트를 쓴다). 빈칸은 미리 채워 둬야 한다.</summary>
        public CastMember ToMember(CharacterTables tables, MakingTemplates templates, int id)
        {
            var m = new CastMember
            {
                id = id, name = name.Trim(), female = IsFemale, age = age,
                childhood = tables.childhoods.Find(c => c.title == childhood),
                adulthood = tables.adulthoods.Find(c => c.title == adulthood),
                arrival = arrival,
                family = MakingTemplates.Gendered(family, IsFemale),
                bond = bond,
                isPlayer = true,
                attention = 0.3f,
                abilities = abilities.Clone(),
            };
            foreach (string t in traits)
            {
                var trait = tables.traits.Find(x => x.name == t);
                if (trait != null && !m.traits.Contains(trait)) m.traits.Add(trait);
            }
            foreach (string b in beliefs) if (!string.IsNullOrWhiteSpace(b) && !m.beliefs.Contains(b)) m.beliefs.Add(b);
            foreach (var line in personal)
            {
                var e = ToEvent(line, templates);
                if (e != null) m.personal.Add(e);
            }
            return m;
        }

        /// <summary>개인 서사 한 줄을 규격 줄로. '무엇을'이 덜 채워졌으면 그 줄은 버린다.</summary>
        public static PastEvent ToEvent(PersonalLine line, MakingTemplates templates)
        {
            string what = line.what.Text(templates.whatTemplates);
            if (string.IsNullOrWhiteSpace(what)) return null;
            return new PastEvent
            {
                when = line.when?.Trim() ?? "", where = line.where?.Trim() ?? "", who = "나", what = what,
                how = line.how.Text(templates.howTemplates) ?? "", why = line.why.Text(templates.whyTemplates) ?? "",
            };
        }

        // =====================================================================
        // #2. 문장으로 찍기 (관계도 창의 '나' 칸, 로그)
        // =====================================================================

        public static List<string> Sentences(CastMember m)
        {
            var list = new List<string>
            {
                $"나는 {m.name}, {m.age}살 {(m.female ? Female : Male)}이다.",
                $"어린 시절: {m.childhood?.title}",
                $"지금 나는 {m.adulthood?.title}이다.",
            };
            if (!string.IsNullOrWhiteSpace(m.family)) list.Add($"집에서의 자리: {m.family}");
            list.Add($"산장에 온 까닭: {m.arrival}");
            var traitNames = new List<string>();
            foreach (var t in m.traits) traitNames.Add(t.name);
            list.Add($"성격: {string.Join(", ", traitNames)}");
            list.Add($"믿는 것: {string.Join(" / ", m.beliefs)}");
            return list;
        }
    }
}
