using System;
using System.Collections.Generic;
using System.Text;

namespace Improv.Npc.Generation
{
    // =====================================================================
    // 한 판에 만들어진 사람들과 그들의 과거 (계획서 T-8). 일반 데이터 클래스라 한 파일에 모은다.
    // 진실(SharedPast.truth)은 월드 데이터다(P-2). NPC의 LLM에는 각자의 판본(PastVersion)만 넘긴다(P-3).
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
        public List<PastEvent> truth = new(); // 실제로 일어난 일. 한 줄에 한 일
        public PastVersion versionA, versionB;
        public bool fromLlm;                 // LLM이 실패해 코드가 대신 쓴 것이면 false

        public PastVersion VersionOf(CastMember m) => m == a ? versionA : m == b ? versionB : null;
        public CastMember Other(CastMember m) => m == a ? b : m == b ? a : null;
    }

    /// <summary>
    /// 과거의 한 줄 = 한 사람이 한 일 하나. 육하원칙 칸 (IDEAS I-11, 사용자 결정 2026-11-25).
    /// 긴 서술 대신 "규격이 통일된 짧은 줄"로 쪼개 두면, LLM이 주어를 헷갈리거나 사실을 지어내는 일이 줄어든다.
    /// (C로 치면 자유 문자열 대신 struct 배열. 출력은 PastFormat이 한 가지 모양으로만 찍는다.)
    /// </summary>
    [Serializable]
    public class PastEvent
    {
        public string when, where, who, what, how, why;
    }

    /// <summary>한 사람이 기억하는 그 일. 성격·믿음·충격으로 왜곡될 수 있다.</summary>
    [Serializable]
    public class PastVersion
    {
        public string name;
        public List<string> people = new();      // 두 당사자 말고 나오는 사람. "지호 = 내 아들, 그때 17살"
        public List<PastEvent> events = new();   // 1인칭 기억. 자기는 "나"
        public string feeling;                   // 지금 상대에게 품은 마음
        public int affinity = -1;                // 그 마음의 숫자, 묵은 감정 0~100 (I-12). -1이면 LLM이 안 줌
    }

    /// <summary>
    /// 과거를 글로 찍는 유일한 곳. NPC 프롬프트, 프리셋, 로그가 모두 같은 규격을 쓴다.
    /// 규격: [때 | 곳] 누가 → 무엇을. 어떻게: …. 왜: ….   (빈 칸은 뺀다)
    /// </summary>
    public static class PastFormat
    {
        /// <summary>NPC 프롬프트에서 과거 묶음 앞에 붙이는 형식 설명.</summary>
        public const string Legend = "한 줄에 한 일. 형식: [때 | 곳] 누가 → 무엇을. 어떻게: …. 왜: ….";

        public static string Line(PastEvent e)
        {
            var sb = new StringBuilder();
            bool hasWhen = !string.IsNullOrWhiteSpace(e.when), hasWhere = !string.IsNullOrWhiteSpace(e.where);
            if (hasWhen || hasWhere)
                sb.Append('[').Append(hasWhen ? e.when.Trim() : "").Append(hasWhen && hasWhere ? " | " : "").Append(hasWhere ? e.where.Trim() : "").Append("] ");
            sb.Append(Clean(e.who)).Append(" → ").Append(Clean(e.what));
            if (!string.IsNullOrWhiteSpace(e.how)) sb.Append(". 어떻게: ").Append(Clean(e.how));
            if (!string.IsNullOrWhiteSpace(e.why)) sb.Append(". 왜: ").Append(Clean(e.why));
            return sb.ToString();
        }

        /// <summary>한 NPC가 품은 기억 한 묶음. 시트의 pasts 한 칸이 된다.</summary>
        public static string Memory(string otherName, bool heavy, PastVersion v)
        {
            var sb = new StringBuilder($"[{otherName}와(과)의 과거] {(heavy ? "짙음" : "가벼움")}");
            foreach (var person in v.people)
                if (!string.IsNullOrWhiteSpace(person)) sb.Append("\n- 사람: ").Append(person.Trim());
            foreach (var e in v.events) sb.Append("\n- ").Append(Line(e));
            if (!string.IsNullOrWhiteSpace(v.feeling)) sb.Append("\n- 지금 마음: ").Append(Clean(v.feeling));
            return sb.ToString();
        }

        /// <summary>진실 한 묶음. 프리셋과 로그용(월드 데이터, NPC에게는 안 넘긴다).</summary>
        public static string Truth(SharedPast p)
        {
            var sb = new StringBuilder($"[{p.a.name} - {p.b.name}] {p.kind}{(p.heavy ? ", 짙음" : "")}");
            foreach (var e in p.truth) sb.Append("\n- ").Append(Line(e));
            return sb.ToString();
        }

        // LLM이 줄 끝에 마침표를 붙여 ". 왜:" 앞에 ".."가 생기지 않게.
        static string Clean(string s) => (s ?? "").Trim().TrimEnd('.', '。');
    }
}
