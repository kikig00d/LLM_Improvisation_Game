using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Improv.Npc.Generation
{
    /// <summary>
    /// 캐릭터 메이킹(IDEAS I-11, GDD R-029)의 "보기"와 "문장 틀".
    /// 고등학교 서술형 시험처럼, 플레이어는 정해진 문장 틀을 고르고 그 안의 짧은 빈칸만 채운다 (사용자 2026-11-26: 설정 과잉을 막는다).
    /// 이름, 배경, 특성, 믿음은 NPC와 같은 표(CharacterTables)를 쓰고, 여기에는 메이킹에만 필요한 것을 둔다.
    /// 문장 틀의 빈칸은 {꼬리표}로 적는다: "{누구}와 {무엇} 때문에 다툼" → 빈칸 둘.
    /// 성별에 따라 갈리는 말은 {남자말|여자말}로 적는다: "두 아이의 {아버지|어머니}".
    /// </summary>
    [CreateAssetMenu(fileName = "MakingTemplates", menuName = "Improv/Making Templates")]
    public class MakingTemplates : ScriptableObject
    {
        // =====================================================================
        // #1. 보기 목록
        // =====================================================================

        [Tooltip("④ 집에서의 자리. {남자말|여자말}로 성별을 가른다")]
        public List<string> familyPlaces = new();
        [Tooltip("개인 서사 줄의 '때'")]
        public List<string> times = new();
        [Tooltip("개인 서사의 '무엇을' 틀. 끝은 '~함/~음'")]
        public List<string> whatTemplates = new();
        [Tooltip("개인 서사의 '어떻게' 틀")]
        public List<string> howTemplates = new();
        [Tooltip("개인 서사의 '왜' 틀")]
        public List<string> whyTemplates = new();
        [Tooltip("주사위가 '곳' 칸에 넣을 말")]
        public List<string> places = new();
        [Tooltip("주사위가 틀의 빈칸에 넣을 말. '꼬리표: 말, 말, 말' 꼴")]
        public List<string> blankWords = new();

        // =====================================================================
        // #2. 길이 제한 (GDD O-016 [가정])
        // =====================================================================

        public int nameMaxLength = 6;
        public int blankMaxLength = 8;       // 문장 틀 빈칸 하나에 칠 수 있는 글자 수
        public int placeMaxLength = 10;      // 개인 서사의 '곳'
        public int personalLines = 3;        // 개인 서사 줄 수

        [Tooltip("'LLM이 채워 줘'에 보여 줄 보기 (PersonaDraft JSON 한 개). 모양만 참고하게 한다")]
        [TextArea(4, 12)] public string llmExample = "";

        // =====================================================================
        // #3. 문장 틀 도우미
        // =====================================================================

        static readonly Regex Blank = new(@"\{([^{}|]+)\}");          // {누구}
        static readonly Regex GenderWord = new(@"\{([^{}|]*)\|([^{}|]*)\}");   // {아버지|어머니}

        /// <summary>{남자말|여자말}을 성별에 맞게 고른다.</summary>
        public static string Gendered(string text, bool female) =>
            string.IsNullOrEmpty(text) ? "" : GenderWord.Replace(text, m => female ? m.Groups[2].Value : m.Groups[1].Value);

        /// <summary>틀 안 빈칸의 꼬리표들. "{누구}와 {무엇} 때문에 다툼" → [누구, 무엇]</summary>
        public static List<string> BlankLabels(string template)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(template)) return list;
            foreach (Match m in Blank.Matches(template)) list.Add(m.Groups[1].Value);
            return list;
        }

        /// <summary>틀을 문장 조각들로 쪼갠다. 짝수 자리는 글자, 홀수 자리는 빈칸 꼬리표. UI가 글자와 입력칸을 번갈아 놓는다.</summary>
        public static List<string> Split(string template)
        {
            var parts = new List<string>();
            int last = 0;
            foreach (Match m in Blank.Matches(template ?? ""))
            {
                parts.Add(template.Substring(last, m.Index - last));
                parts.Add(m.Groups[1].Value);
                last = m.Index + m.Length;
            }
            parts.Add((template ?? "").Substring(last));
            return parts;
        }

        /// <summary>빈칸을 채운 문장. 빈칸이 하나라도 비면 null (덜 쓴 문장은 쓰지 않는다).</summary>
        public static string Fill(string template, string[] blanks)
        {
            var labels = BlankLabels(template);
            if (labels.Count > 0 && (blanks == null || blanks.Length < labels.Count)) return null;
            int i = 0;
            bool missing = false;
            string result = Blank.Replace(template, _ =>
            {
                string v = blanks[i++]?.Trim() ?? "";
                if (v.Length == 0) missing = true;
                return v;
            });
            return missing ? null : result;
        }

        /// <summary>꼬리표에 맞는 주사위 말 목록. 없으면 빈 목록.</summary>
        public List<string> WordsFor(string label)
        {
            var list = new List<string>();
            foreach (string line in blankWords)
            {
                int colon = line.IndexOf(':');
                if (colon <= 0 || line.Substring(0, colon).Trim() != label) continue;
                foreach (string w in line.Substring(colon + 1).Split(','))
                    if (w.Trim().Length > 0) list.Add(w.Trim());
            }
            return list;
        }

        /// <summary>UI용 미리보기: 빈 칸은 ___로.</summary>
        public static string Preview(string template)
        {
            var sb = new StringBuilder();
            var parts = Split(template);
            for (int i = 0; i < parts.Count; i++) sb.Append(i % 2 == 0 ? parts[i] : "___");
            return sb.ToString();
        }
    }
}
