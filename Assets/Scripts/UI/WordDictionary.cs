using System;
using System.Collections.Generic;
using System.Text;

namespace Improv.UI
{
    /// <summary>
    /// 허용 문법 사전 (R-015, R-032). Assets/Data/Dialogue/Words.txt를 읽어 자동완성 후보를 찾고,
    /// 단어마다 붙은 속성(종류·여닫음·가로막음)과 행동의 칸(무엇을=여닫음)을 들고 있다.
    /// 플레이어도 NPC도 이 목록에 있는 단어만 말할 수 있다.
    /// </summary>
    public class WordDictionary
    {
        // =====================================================================
        // #1. 자료
        // =====================================================================

        /// <summary>행동 단어의 칸 하나 (R-032). 예: 열다의 "무엇을" 칸에는 여닫음이 있어야 한다.</summary>
        public class Slot
        {
            public string Role;      // 무엇을, 무엇으로 …
            public string[] Needs;   // 이 칸에 올 단어가 모두 가져야 할 속성
        }

        readonly List<string> words = new();
        readonly HashSet<string> lookup = new();   // 있는지 빨리 확인하는 용도. C의 해시 테이블

        // 단어 → 속성 (R-032). 속성이 LLM의 넓은 의미 대신 이 게임 안의 좌표가 된다. 칼 → 도구, 여닫음
        readonly Dictionary<string, string[]> attributes = new();
        // 행동 → 칸들. 열다 → [무엇을=여닫음, 무엇으로=도구]
        readonly Dictionary<string, List<Slot>> slots = new();

        // 조사 (R-032a). 사전에는 "~을: 무엇을"처럼 물결을 붙여 적는다.
        // 지시어 "이"와 조사 "~이"를 헷갈리지 않으려고 문장 안에서도 물결을 단 채로 다닌다.
        readonly List<string> particles = new();                 // "~은", "~을" …
        readonly Dictionary<string, string> particleRoles = new(); // "~을" → "무엇을"

        // 사람 이름 (F-17). 파일에 적지 않고 게임 중에 지금 있는 캐릭터들의 이름으로 채운다.
        // 이름은 바뀔 수 있어서(개명) 고정 단어로 두지 않는다. 동명이인은 한 단어로 합쳐진다.
        readonly List<string> names = new();

        public const char ParticleMark = '~';

        public int Count => words.Count + names.Count;
        public IReadOnlyList<string> Names => names;
        public IReadOnlyList<string> Words => words;
        public IReadOnlyList<string> Particles => particles;
        public IEnumerable<string> Verbs => slots.Keys;

        public static bool IsParticle(string token) => token.Length > 1 && token[0] == ParticleMark;

        // =====================================================================
        // #2. 읽기
        // =====================================================================

        /// <summary>이름 단어를 통째로 바꾼다. 대화창이 캐릭터 명단이 바뀔 때마다 부른다.</summary>
        public void SetNames(IEnumerable<string> newNames)
        {
            names.Clear();
            foreach (string n in newNames)
                if (!string.IsNullOrWhiteSpace(n) && !names.Contains(n) && !lookup.Contains(n)) names.Add(n);
        }

        /// <summary>
        /// 사전 글을 읽는다. 한 줄에 단어 하나이고, 콜론 뒤는 덧붙임이다:
        ///   문: 가구, 여닫음, 가로막음          ← 속성
        ///   열다: 무엇을=여닫음 | 무엇으로=도구  ← 행동의 칸(속성이 여럿이면 +로 잇는다)
        ///   ~을: 무엇을                         ← 조사와 그 역할
        /// 콜론이 없는 줄은 예전처럼 그냥 단어다.
        /// </summary>
        public static WordDictionary Parse(string text)
        {
            var d = new WordDictionary();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;   // 빈 줄과 설명 줄은 건너뛴다

                string word = line, rest = "";
                int colon = line.IndexOf(':');
                if (colon > 0)
                {
                    word = line.Substring(0, colon).Trim();
                    rest = line.Substring(colon + 1).Trim();
                }

                if (IsParticle(word))
                {
                    if (!d.particleRoles.ContainsKey(word)) { d.particles.Add(word); d.particleRoles[word] = rest; }
                    continue;
                }
                if (d.lookup.Add(word)) d.words.Add(word);   // 중복은 한 번만
                if (rest.Length == 0) continue;

                if (rest.Contains("="))
                {
                    var list = new List<Slot>();
                    foreach (string part in rest.Split('|'))
                    {
                        string[] kv = part.Split('=');
                        if (kv.Length != 2) continue;
                        list.Add(new Slot { Role = kv[0].Trim(), Needs = SplitTrim(kv[1], '+') });
                    }
                    d.slots[word] = list;
                }
                else d.attributes[word] = SplitTrim(rest, ',');
            }
            return d;
        }

        static string[] SplitTrim(string s, char sep)
        {
            var result = new List<string>();
            foreach (string p in s.Split(sep)) if (p.Trim().Length > 0) result.Add(p.Trim());
            return result.ToArray();
        }

        public bool Contains(string word) => lookup.Contains(word) || names.Contains(word) || particleRoles.ContainsKey(word);

        public string[] AttributesOf(string word) => attributes.TryGetValue(word, out var a) ? a : Array.Empty<string>();
        public IReadOnlyList<Slot> SlotsOf(string word) => slots.TryGetValue(word, out var s) ? s : null;
        public string RoleOf(string particle) => particleRoles.TryGetValue(particle, out var r) ? r : "";

        /// <summary>사물 단어(속성이 있는 단어) 목록. 비슷한 말을 찾을 때 쓴다.</summary>
        public IEnumerable<string> WordsWithAttributes => attributes.Keys;

        // =====================================================================
        // #3. 자동완성
        // =====================================================================

        /// <summary>입력한 글자로 시작하는 단어를 최대 max개 찾는다. 똑같은 단어가 있으면 맨 앞에 둔다.</summary>
        public List<string> FindCandidates(string typed, int max)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(typed)) return result;

            if (lookup.Contains(typed) || names.Contains(typed)) result.Add(typed);
            // 조사는 짧아서 앞부분만 맞아도 너무 자주 끼어든다. 친 글자로 시작하는 조사만, 똑같은 단어 바로 뒤에 둔다.
            foreach (string p in particles)
            {
                if (result.Count >= max) break;
                if (p.Substring(1).StartsWith(typed)) result.Add(p);
            }
            // 이름을 먼저 본다. 이름은 몇 개 안 되고, 부르려고 칠 때가 많다.
            foreach (string word in names)
            {
                if (result.Count >= max) break;
                if (word != typed && HangulMatcher.Matches(word, typed)) result.Add(word);
            }
            foreach (string word in words)
            {
                if (result.Count >= max) break;
                if (word != typed && HangulMatcher.Matches(word, typed)) result.Add(word);
            }
            return result;
        }

        // =====================================================================
        // #4. 문장으로 잇기 (R-032a)
        // =====================================================================

        /// <summary>
        /// 조사 노이즈를 버린다: 맨 앞의 조사, 부호나 조사 바로 뒤의 조사.
        /// 남은 조사는 앞 단어가 하나씩만 갖는다. NPC가 "나 ~을 ~이 ~는"처럼 몰아 내뱉는 것을 거른다.
        /// 부호도 이어진 것은 첫 하나만 둔다. M15 시험에서 NPC가 "서미라 ! ! ! …"로 칸을 다 채웠다(세기는 !!처럼 한 단어로 고른다).
        /// </summary>
        public static List<string> CleanTokens(IReadOnlyList<string> tokens)
        {
            var result = new List<string>();
            foreach (string t in tokens)
            {
                if (IsPunctuation(t) && result.Count > 0 && IsPunctuation(result[result.Count - 1])) continue;
                if (IsParticle(t))
                {
                    if (result.Count == 0) continue;
                    string prev = result[result.Count - 1];
                    if (IsParticle(prev) || IsPunctuation(prev)) continue;
                }
                result.Add(t);
            }
            return result;
        }

        /// <summary>단어들을 문장으로 잇는다. 부호(? ! ...)와 조사는 앞 단어에 붙인다.</summary>
        public static string JoinSentence(IReadOnlyList<string> sentenceWords)
        {
            var sb = new StringBuilder();
            string prev = "";
            foreach (string w in CleanTokens(sentenceWords))
            {
                if (IsParticle(w)) { sb.Append(Josa.Fit(prev, w.Substring(1))); continue; }
                if (sb.Length > 0 && !IsPunctuation(w)) sb.Append(' ');
                sb.Append(w);
                prev = w;
            }
            return sb.ToString();
        }

        public static bool IsPunctuation(string word)
        {
            foreach (char c in word)
                if (Array.IndexOf(new[] { '?', '!', '.', '…' }, c) < 0) return false;
            return word.Length > 0;
        }

        /// <summary>
        /// 이어 붙인 문장을 다시 단어와 조사로 나눈다. 들은 말을 읽을 때 쓴다(듣는 쪽은 글자만 받으므로, P-1).
        /// "칼로 문을 열다?" → 칼 ~로 문 ~을 열다 ?. 사전에 없거나 노이즈(○)가 섞인 조각은 그대로 둔다.
        /// </summary>
        public List<string> SplitSentence(string text)
        {
            var result = new List<string>();
            foreach (string raw in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string piece = raw;
                string tail = "";
                // 끝의 부호를 떼어 둔다
                int end = piece.Length;
                while (end > 0 && IsPunctuation(piece[end - 1].ToString())) end--;
                if (end < piece.Length) { tail = piece.Substring(end); piece = piece.Substring(0, end); }

                if (piece.Length > 0)
                {
                    if (Contains(piece)) result.Add(piece);
                    else if (!TrySplitParticle(piece, result)) result.Add(piece);
                }
                if (tail.Length > 0) result.Add(tail);
            }
            return result;
        }

        // 가장 긴 조사부터 떼어 본다(에서 > 에). 앞부분이 사전 단어여야 인정한다.
        bool TrySplitParticle(string piece, List<string> result)
        {
            string best = null;
            foreach (string p in particles)
            {
                string surface = p.Substring(1);
                if (piece.Length <= surface.Length || !piece.EndsWith(surface)) continue;
                string head = piece.Substring(0, piece.Length - surface.Length);
                if (!lookup.Contains(head) && !names.Contains(head)) continue;
                if (best == null || surface.Length > best.Length - 1) best = p;
            }
            if (best == null) return false;
            result.Add(piece.Substring(0, piece.Length - (best.Length - 1)));
            result.Add(best);
            return true;
        }
    }
}
