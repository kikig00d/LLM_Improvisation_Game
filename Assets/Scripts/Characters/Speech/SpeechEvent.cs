using UnityEngine;

namespace Improv.Characters.Speech
{
    /// <summary>목소리 크기. 크기에 따라 들리는 범위가 달라진다 (R-014a). M4에서는 Normal만 쓴다.</summary>
    public enum SpeechVolume
    {
        Whisper,  // 귓속말 (배율은 O-006 [미정])
        Normal,
        Shout     // 외치기 (배율은 O-006 [미정])
    }

    /// <summary>
    /// "누가, 어디서, 얼마나 크게, 무슨 말을 했다"는 월드의 사건 하나.
    /// 말풍선(화면)과 귀(판정)가 모두 이 사건을 보고 각자 일한다 (P-2: 판정은 월드 데이터로, UI는 표시만).
    /// class(참조 타입)라서 여러 귀에 넘겨도 복사되지 않고 같은 사건을 가리킨다.
    /// </summary>
    public class SpeechEvent
    {
        public readonly Speaker Speaker;
        /// <summary>
        /// 말한 순간 말한 사람의 이름 (F-17). 이름은 바뀔 수 있어서(개명), 나중에 Speaker에서 다시 읽지 않고 그때 이름을 담아 둔다.
        /// 듣는 쪽은 이 이름만 안다. 같은 이름의 다른 사람(동명이인)이면 듣는 쪽도 구분하지 못한다 (P-3).
        /// </summary>
        public readonly string SpeakerName;
        public readonly string Text;
        public readonly Vector2 Origin;      // 말소리가 시작된 곳 (입 위치)
        public readonly SpeechVolume Volume;
        public readonly float Time;          // 말한 게임 시각 (Time.time)
        /// <summary>
        /// 플레이어가 끼지 않고 NPC끼리 이어진 말이 몇 번째인가 (계획서 T-4). 플레이어의 말은 0.
        /// NPC가 들은 말에 답하면 그 말의 번호 + 1이 된다. 이 번호가 상한을 넘으면 NPC가 LLM을 부르지 않는다 (P-4).
        /// </summary>
        public readonly int ChainDepth;

        public SpeechEvent(Speaker speaker, string text, Vector2 origin, SpeechVolume volume, float time, int chainDepth = 0)
        {
            Speaker = speaker;
            SpeakerName = speaker != null ? speaker.DisplayName : "";
            Text = text;
            Origin = origin;
            Volume = volume;
            Time = time;
            ChainDepth = chainDepth;
        }

        /// <summary>
        /// 같은 사건인데 글만 다른 사본. 멀어서 뭉개져 들린 말(R-014c)을 귀마다 따로 만들 때 쓴다.
        /// 원래 사건은 그대로 두므로, 다른 귀나 말풍선에는 영향이 없다.
        /// </summary>
        public SpeechEvent WithText(string text) => new(Speaker, text, Origin, Volume, Time, ChainDepth);
    }
}
