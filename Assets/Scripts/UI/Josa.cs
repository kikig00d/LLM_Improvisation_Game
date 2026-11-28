namespace Improv.UI
{
    /// <summary>
    /// 받침에 따라 모양이 바뀌는 조사 (R-032a). 은/는, 이/가, 을/를, 과/와, 으로/로.
    /// 플레이어나 NPC가 어느 쪽을 골라도 앞 단어에 맞게 고쳐 붙인다.
    /// 한글 음절의 받침 번호는 (글자 - '가') % 28이다. 0이면 받침 없음, 8이면 ㄹ (HangulMatcher 참고).
    /// </summary>
    public static class Josa
    {
        // 짝: [받침 있을 때, 받침 없을 때]
        static readonly string[][] Pairs =
        {
            new[] { "은", "는" }, new[] { "이", "가" }, new[] { "을", "를" }, new[] { "과", "와" }, new[] { "으로", "로" },
        };

        /// <summary>word 뒤에 붙일 조사 모양. 짝이 없는 조사(에, 에서)는 그대로.</summary>
        public static string Fit(string word, string josa)
        {
            foreach (var pair in Pairs)
            {
                if (josa != pair[0] && josa != pair[1]) continue;
                int jong = Jong(word);
                if (jong < 0) return josa;                          // 한글이 아니면 고치지 않는다
                if (pair[1] == "로") return jong == 0 || jong == 8 ? "로" : "으로";   // ㄹ 받침은 "칼로"
                return jong == 0 ? pair[1] : pair[0];
            }
            return josa;
        }

        static int Jong(string word)
        {
            if (string.IsNullOrEmpty(word)) return -1;
            char c = word[word.Length - 1];
            if (c < 0xAC00 || c > 0xD7A3) return -1;
            return (c - 0xAC00) % 28;
        }
    }
}
