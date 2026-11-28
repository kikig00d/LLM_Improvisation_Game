using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Improv.UI
{
    /// <summary>
    /// 허용 문법 사전으로 문장을 읽는다 (R-032b, R-032d). 그래프 RAG의 "그래프"가 여기서 쓰인다:
    /// 단어 → 속성, 행동 → 칸이라는 작은 그래프를 따라가서
    ///   1) 문장 속 단어가 행동의 칸에 맞는지(뜻이 가까운지) 재고,
    ///   2) 들은 단어에서 한 칸 따라간 조각(단어 뜻)을 글로 만들어 LLM 프롬프트에 붙인다.
    /// 그래프 DB 없이 사전의 Dictionary 두 개로 충분하다. C로 치면 인접 리스트 두 개.
    /// 먼 문장도 막지 않는다. 듣는 쪽에 "이상한 말" 메모와 가까운 뜻 후보를 건넬 뿐이다.
    /// </summary>
    public static class GrammarReader
    {
        // =====================================================================
        // #1. 읽은 결과
        // =====================================================================

        public class Fill
        {
            public string Word;
            public string Role;
            public string[] Needs;
            public bool Marked;   // 조사로 칸을 밝혔나. 조사 없는 단어는 맞는 칸에만 넉넉히 넣어 준다
            public bool Fits;
        }

        public class Reading
        {
            public string Verb;
            public readonly List<Fill> Fills = new();
            public readonly List<string> Hints = new();   // 가까운 뜻 후보
            public bool Odd => Fills.Any(f => !f.Fits);
            /// <summary>0이면 가깝고 1이면 멀다. 칸에 든 단어 중 안 맞는 몫.</summary>
            public float Distance => Fills.Count == 0 ? 0f : Fills.Count(f => !f.Fits) / (float)Fills.Count;
        }

        // =====================================================================
        // #2. 칸 맞추기 (R-032b)
        // =====================================================================

        /// <summary>문장(단어·조사 목록)을 읽는다. 칸이 적힌 행동이 없으면 null.</summary>
        public static Reading Read(WordDictionary d, IReadOnlyList<string> rawTokens)
        {
            var tokens = WordDictionary.CleanTokens(rawTokens);

            // shortcut: 마지막 행동 하나만 본다. 한 문장에 행동이 여럿 이어지는 말이 흔해지면 행동마다 앞 단어를 끊어 읽는다.
            int v = -1;
            for (int i = tokens.Count - 1; i >= 0; i--) if (d.SlotsOf(tokens[i]) != null) { v = i; break; }
            if (v < 0) return null;

            var reading = new Reading { Verb = tokens[v] };
            var slots = d.SlotsOf(reading.Verb);
            var bare = new List<string>();

            for (int i = 0; i < v; i++)
            {
                string t = tokens[i];
                if (WordDictionary.IsParticle(t) || WordDictionary.IsPunctuation(t)) continue;
                string particle = i + 1 < v && WordDictionary.IsParticle(tokens[i + 1]) ? tokens[i + 1] : null;
                if (particle == null) { bare.Add(t); continue; }

                var slot = slots.FirstOrDefault(s => s.Role == d.RoleOf(particle));
                if (slot == null) continue;   // 누가, 어디처럼 칸이 없는 역할은 재지 않는다
                reading.Fills.Add(new Fill { Word = t, Role = slot.Role, Needs = slot.Needs, Marked = true, Fits = HasAll(d, t, slot.Needs) });
            }

            // 조사 없는 단어는 빈 칸 중 맞는 곳에만 넣는다. 왈도체("칼 문 열다")도 뜻이 통하게.
            foreach (var slot in slots)
            {
                if (reading.Fills.Any(f => f.Role == slot.Role)) continue;
                string w = bare.FirstOrDefault(b => HasAll(d, b, slot.Needs));
                if (w == null) continue;
                bare.Remove(w);
                reading.Fills.Add(new Fill { Word = w, Role = slot.Role, Needs = slot.Needs, Fits = true });
            }

            if (reading.Odd) AddHints(d, reading, bare);
            return reading;
        }

        static bool HasAll(WordDictionary d, string word, string[] needs)
        {
            var attrs = d.AttributesOf(word);
            return needs.All(n => attrs.Contains(n));
        }

        // 가까운 뜻 후보 셋: 자리를 바꾼 뜻, 맞는 다른 행동, 그 칸에 맞는 비슷한 말
        static void AddHints(WordDictionary d, Reading r, List<string> leftover)
        {
            foreach (var bad in r.Fills.Where(f => !f.Fits).ToList())
            {
                // 1) 자리 바꾸기: 다른 칸의 단어(또는 칸을 못 찾은 단어)와 바꾸면 둘 다 맞나
                foreach (var other in r.Fills)
                {
                    if (other == bad || !HasAll(d, other.Word, bad.Needs) || !HasAll(d, bad.Word, other.Needs)) continue;
                    r.Hints.Add($"자리를 바꾸면 '{Sentence(d, (other.Word, bad.Role), (bad.Word, other.Role), r.Verb)}'");
                }
                foreach (string w in leftover)
                    if (HasAll(d, w, bad.Needs)) r.Hints.Add($"'{Sentence(d, (w, bad.Role), (null, null), r.Verb)}'");

                // 2) 지금 단어들이 다 맞는 다른 행동
                var marked = r.Fills.Where(f => f.Marked).ToList();
                var verbs = d.Verbs.Where(vb => vb != r.Verb && marked.All(f =>
                {
                    var s = d.SlotsOf(vb).FirstOrDefault(x => x.Role == f.Role);
                    return s != null && HasAll(d, f.Word, s.Needs);
                })).Take(3).ToList();
                if (verbs.Count > 0) r.Hints.Add("맞는 다른 행동: " + string.Join(", ", verbs));

                // 3) 그 칸에 맞는 말. 안 맞은 단어와 속성이 많이 겹치는 것부터(자카드 유사도)
                var near = d.WordsWithAttributes.Where(w => w != bad.Word && HasAll(d, w, bad.Needs))
                    .OrderByDescending(w => Similarity(d, w, bad.Word)).Take(3).ToList();
                if (near.Count > 0) r.Hints.Add($"{bad.Role} 칸에 맞는 말: " + string.Join(", ", near));
            }
        }

        /// <summary>두 단어 속성의 자카드 유사도(겹친 수 / 합친 수). 속성이 단어의 좌표 노릇을 한다.</summary>
        public static float Similarity(WordDictionary d, string a, string b)
        {
            var x = d.AttributesOf(a); var y = d.AttributesOf(b);
            int union = x.Union(y).Count();
            return union == 0 ? 0f : x.Intersect(y).Count() / (float)union;
        }

        static string Sentence(WordDictionary d, (string word, string role) a, (string word, string role) b, string verb)
        {
            var tokens = new List<string>();
            foreach (var (word, role) in new[] { a, b })
            {
                if (word == null) continue;
                tokens.Add(word);
                string p = d.Particles.FirstOrDefault(x => d.RoleOf(x) == role);
                if (p != null) tokens.Add(p);
            }
            tokens.Add(verb);
            return WordDictionary.JoinSentence(tokens);
        }

        // =====================================================================
        // #3. 프롬프트에 붙일 글 (R-032d)
        // =====================================================================

        /// <summary>들은 단어에서 한 칸 따라간 조각. "문(가구, 여닫음, 가로막음) / 열다(무엇을=여닫음, 무엇으로=도구)"</summary>
        public static string DescribeWords(WordDictionary d, IReadOnlyList<string> tokens)
        {
            var parts = new List<string>();
            foreach (string t in tokens.Distinct())
            {
                var attrs = d.AttributesOf(t);
                var slots = d.SlotsOf(t);
                if (attrs.Length > 0) parts.Add($"{t}({string.Join(", ", attrs)})");
                else if (slots != null) parts.Add($"{t}({string.Join(", ", slots.Select(s => s.Role + "=" + string.Join("+", s.Needs)))})");
            }
            return string.Join(" / ", parts);
        }

        /// <summary>들은 말 한 줄에 붙일 메모. 뜻풀이와, 이상하면 이상한 까닭과 가까운 뜻 후보. 붙일 게 없으면 "".</summary>
        public static string Annotate(WordDictionary d, string heardText)
        {
            var tokens = d.SplitSentence(heardText);
            var sb = new StringBuilder();
            string words = DescribeWords(d, tokens);
            if (words.Length > 0) sb.Append("단어 뜻: ").Append(words);

            var r = Read(d, tokens);
            if (r != null && r.Odd)
            {
                var bad = r.Fills.Where(f => !f.Fits).Select(f => $"'{f.Word}'은(는) {r.Verb}의 {f.Role} 칸({string.Join("+", f.Needs)})에 안 맞음");
                if (sb.Length > 0) sb.Append(". ");
                sb.Append("이상한 말: ").Append(string.Join(", ", bad));
                if (r.Hints.Count > 0) sb.Append(". 가까운 뜻 후보: ").Append(string.Join(" / ", r.Hints.Distinct()));
            }
            return sb.ToString();
        }

        // =====================================================================
        // #4. 자체 점검 (M15 완료 기준의 예문)
        // =====================================================================

        /// <summary>예문으로 거리를 잰다. 틀린 줄이 있으면 false. script eval로 부른다.</summary>
        public static bool SelfCheck(WordDictionary d, out string report)
        {
            var sb = new StringBuilder();
            bool ok = true;
            void Expect(string text, bool odd)
            {
                var tokens = d.SplitSentence(text);
                var r = Read(d, tokens);
                bool got = r != null && r.Odd;
                if (got != odd) ok = false;
                sb.AppendLine($"{(got == odd ? "통과" : "실패")} '{text}' → [{string.Join(" ", tokens)}] 거리 {r?.Distance ?? 0:0.00} | {Annotate(d, text)}");
            }
            void Join(string[] tokens, string want)
            {
                string got = WordDictionary.JoinSentence(tokens);
                if (got != want) ok = false;
                sb.AppendLine($"{(got == want ? "통과" : "실패")} 잇기 [{string.Join(" ", tokens)}] → '{got}' (기대 '{want}')");
            }

            Expect("칼로 문을 열다", false);
            Expect("칼 문 열다", false);
            Expect("문으로 칼을 열다", true);
            Expect("너를 열다", true);
            Expect("상자로 문을 막다", false);
            Expect("나는 어젯밤 창고에 가다", false);
            Join(new[] { "칼", "~으로", "문", "~를", "열다", "?" }, "칼로 문을 열다?");
            Join(new[] { "~은", "나", "~을", "~이", "~는", "열쇠", "~이", "없다" }, "나를 열쇠가 없다");
            Join(new[] { "상자", "~으로", "문", "~이", "막다" }, "상자로 문이 막다");
            Join(new[] { "서미라", "!", "!", "!", "?" }, "서미라!");
            report = sb.ToString();
            return ok;
        }
    }
}
