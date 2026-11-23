using System;
using System.Collections.Generic;
using System.Text;

namespace Improv.UI
{
    /// <summary>
    /// 허용 단어 사전 (R-015). Assets/Data/Dialogue/Words.txt를 읽어 자동완성 후보를 찾는다.
    /// 플레이어는 이 목록에 있는 단어만 말할 수 있다.
    /// </summary>
    public class WordDictionary
    {
        readonly List<string> words = new();
        readonly HashSet<string> lookup = new();   // 있는지 빨리 확인하는 용도. C의 해시 테이블

        public int Count => words.Count;

        public static WordDictionary Parse(string text)
        {
            var dictionary = new WordDictionary();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;   // 빈 줄과 설명 줄은 건너뛴다
                if (dictionary.lookup.Add(line)) dictionary.words.Add(line); // 중복은 한 번만
            }
            return dictionary;
        }

        public bool Contains(string word) => lookup.Contains(word);

        /// <summary>입력한 글자로 시작하는 단어를 최대 max개 찾는다. 똑같은 단어가 있으면 맨 앞에 둔다.</summary>
        public List<string> FindCandidates(string typed, int max)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(typed)) return result;

            if (lookup.Contains(typed)) result.Add(typed);
            foreach (string word in words)
            {
                if (result.Count >= max) break;
                if (word != typed && HangulMatcher.Matches(word, typed)) result.Add(word);
            }
            return result;
        }

        /// <summary>단어들을 문장으로 잇는다. 부호(? ! ...)는 앞 단어에 붙인다.</summary>
        public static string JoinSentence(IReadOnlyList<string> sentenceWords)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < sentenceWords.Count; i++)
            {
                string w = sentenceWords[i];
                if (i > 0 && !IsPunctuation(w)) sb.Append(' ');
                sb.Append(w);
            }
            return sb.ToString();
        }

        public static bool IsPunctuation(string word)
        {
            foreach (char c in word)
                if (Array.IndexOf(new[] { '?', '!', '.', '…' }, c) < 0) return false;
            return word.Length > 0;
        }
    }
}
