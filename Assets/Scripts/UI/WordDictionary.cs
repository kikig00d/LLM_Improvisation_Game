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

        // 사람 이름 (F-17). 파일에 적지 않고 게임 중에 지금 있는 캐릭터들의 이름으로 채운다.
        // 이름은 바뀔 수 있어서(개명) 고정 단어로 두지 않는다. 동명이인은 한 단어로 합쳐진다.
        readonly List<string> names = new();

        public int Count => words.Count + names.Count;
        public IReadOnlyList<string> Names => names;

        /// <summary>이름 단어를 통째로 바꾼다. 대화창이 캐릭터 명단이 바뀔 때마다 부른다.</summary>
        public void SetNames(IEnumerable<string> newNames)
        {
            names.Clear();
            foreach (string n in newNames)
                if (!string.IsNullOrWhiteSpace(n) && !names.Contains(n) && !lookup.Contains(n)) names.Add(n);
        }

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

        public bool Contains(string word) => lookup.Contains(word) || names.Contains(word);

        /// <summary>입력한 글자로 시작하는 단어를 최대 max개 찾는다. 똑같은 단어가 있으면 맨 앞에 둔다.</summary>
        public List<string> FindCandidates(string typed, int max)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(typed)) return result;

            if (Contains(typed)) result.Add(typed);
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
