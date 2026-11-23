namespace Improv.UI
{
    /// <summary>
    /// 한글 자동완성용 앞부분 비교. 한글은 입력하는 도중에 글자가 바뀐다 (ㅊ → 차 → 창).
    /// 그래서 "창고"를 찾을 때 "차"까지만 쳐도, 초성만 "ㅊㄱ"로 쳐도 후보에 나오게 한다.
    /// 한글 음절은 유니코드에서 가(0xAC00)부터 규칙적으로 늘어선다: (초성 × 21 + 중성) × 28 + 종성.
    /// C에서 2차원 배열 인덱스를 1차원으로 펴는 계산과 같은 원리다.
    /// </summary>
    public static class HangulMatcher
    {
        // =====================================================================
        // #1. 한글 표
        // =====================================================================

        const int SyllableBase = 0xAC00;  // '가'
        const int SyllableLast = 0xD7A3;  // '힣'
        const int JungCount = 21;
        const int JongCount = 28;

        static readonly char[] Choseong =
            { 'ㄱ','ㄲ','ㄴ','ㄷ','ㄸ','ㄹ','ㅁ','ㅂ','ㅃ','ㅅ','ㅆ','ㅇ','ㅈ','ㅉ','ㅊ','ㅋ','ㅌ','ㅍ','ㅎ' };

        // 종성 표. 0번은 "받침 없음". 홑받침만 다음 글자의 초성과 비교한다.
        static readonly char[] Jongseong =
            { '\0','ㄱ','ㄲ','ㄳ','ㄴ','ㄵ','ㄶ','ㄷ','ㄹ','ㄺ','ㄻ','ㄼ','ㄽ','ㄾ','ㄿ','ㅀ','ㅁ','ㅂ','ㅄ','ㅅ','ㅆ','ㅇ','ㅈ','ㅊ','ㅋ','ㅌ','ㅍ','ㅎ' };

        static bool IsSyllable(char c) => c >= SyllableBase && c <= SyllableLast;
        static bool IsConsonantJamo(char c) => c >= 'ㄱ' && c <= 'ㅎ';
        static int Cho(char c) => (c - SyllableBase) / (JungCount * JongCount);
        static int Jung(char c) => (c - SyllableBase) % (JungCount * JongCount) / JongCount;
        static int Jong(char c) => (c - SyllableBase) % JongCount;

        // =====================================================================
        // #2. 비교
        // =====================================================================

        /// <summary>typed가 word의 앞부분으로 읽힐 수 있는지.</summary>
        public static bool Matches(string word, string typed)
        {
            if (string.IsNullOrEmpty(typed)) return false;
            if (IsAllConsonants(typed)) return ChoseongPrefix(word, typed);
            if (typed.Length > word.Length) return false;

            // 마지막 글자 앞까지는 똑같아야 한다.
            int last = typed.Length - 1;
            for (int i = 0; i < last; i++)
                if (word[i] != typed[i]) return false;

            return LastCharMatches(word, last, typed[last]);
        }

        // 아직 입력 중일 수 있는 마지막 글자 비교.
        static bool LastCharMatches(string word, int index, char t)
        {
            char w = word[index];
            if (t == w) return true;

            // "ㅊ"만 친 상태: 초성이 같으면 된다.
            if (IsConsonantJamo(t)) return IsSyllable(w) && Choseong[Cho(w)] == t;

            if (!IsSyllable(t) || !IsSyllable(w)) return false;
            if (Cho(t) != Cho(w) || Jung(t) != Jung(w)) return false;

            // "차"까지 친 상태: 받침이 무엇이 오든 된다 (차 → 창).
            if (Jong(t) == 0) return true;

            // "상"까지 친 상태인데 단어는 "사오": 받침 ㅇ이 다음 글자의 초성이 될 수 있다.
            return Jong(w) == 0 && index + 1 < word.Length && IsSyllable(word[index + 1])
                   && Choseong[Cho(word[index + 1])] == Jongseong[Jong(t)];
        }

        static bool IsAllConsonants(string s)
        {
            foreach (char c in s)
                if (!IsConsonantJamo(c)) return false;
            return true;
        }

        // "ㅊㄱ" → "창고"처럼 초성만으로 앞부분을 비교한다.
        static bool ChoseongPrefix(string word, string typed)
        {
            if (typed.Length > word.Length) return false;
            for (int i = 0; i < typed.Length; i++)
            {
                char w = word[i];
                char cho = IsSyllable(w) ? Choseong[Cho(w)] : w;
                if (cho != typed[i]) return false;
            }
            return true;
        }
    }
}
