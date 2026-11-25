using System.Collections.Generic;

namespace Improv.Player
{
    /// <summary>
    /// 플레이어가 관계도 창(T-9, GDD R-028)에 추리해 적은 것. A가 B를 어떻게 여기는지에 대한 "짐작"이다.
    /// NPC의 진짜 마음(RelationBook)과는 따로다. 플레이어는 본 것과 들은 것만 안다(P-3).
    /// 숫자 대신 느낌 표식을 쓴다 (사용자 2026-11-25: 사람마다 친구로 여기는 호감의 문턱이 달라서, 숫자보다 단계가 맞다).
    /// 사람은 id로 적는다(F-17). 이름은 그릴 때 그때그때 찾는다.
    /// </summary>
    public class PlayerRelationNotes
    {
        public enum Mark { Unknown, Hate, Wary, Indifferent, Like, Dear }

        /// <summary>표식의 한국어 이름. enum 순서와 같다 (C의 문자열 배열 + enum 인덱스와 같은 방식).</summary>
        public static readonly string[] MarkNames = { "모름", "미움", "꺼림", "무덤덤", "호감", "친애" };

        public class Note
        {
            public Mark mark;
            public string memo = "";
            public bool IsEmpty => mark == Mark.Unknown && string.IsNullOrWhiteSpace(memo);
        }

        public const int MaxMemoLength = 40;   // GDD O-015 [가정]

        // (from, to)는 튜플 키: 두 int를 묶은 작은 struct를 키로 쓴다.
        readonly Dictionary<(int from, int to), Note> notes = new();

        public IEnumerable<KeyValuePair<(int from, int to), Note>> All => notes;

        public Note Get(int from, int to)
        {
            if (!notes.TryGetValue((from, to), out var n)) notes[(from, to)] = n = new Note();
            return n;
        }

        public bool TryGet(int from, int to, out Note note) => notes.TryGetValue((from, to), out note) && !note.IsEmpty;

        public void Clear(int from, int to) => notes.Remove((from, to));
    }
}
