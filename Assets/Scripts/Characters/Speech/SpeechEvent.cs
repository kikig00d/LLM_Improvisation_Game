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
        public readonly string Text;
        public readonly Vector2 Origin;      // 말소리가 시작된 곳 (입 위치)
        public readonly SpeechVolume Volume;
        public readonly float Time;          // 말한 게임 시각 (Time.time)

        public SpeechEvent(Speaker speaker, string text, Vector2 origin, SpeechVolume volume, float time)
        {
            Speaker = speaker;
            Text = text;
            Origin = origin;
            Volume = volume;
            Time = time;
        }
    }
}
