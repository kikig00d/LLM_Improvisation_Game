using System;
using UnityEngine;

namespace Improv.Characters.Speech
{
    /// <summary>
    /// 말소리를 월드에 퍼뜨린다. 들을 수 있는 귀(Hearing)에만 전달한다 (R-014a, R-014b).
    /// 대화 상대를 고르는 개념이 없다. 말은 소리이고, 소리는 범위 안의 모두에게 간다.
    /// </summary>
    public static class SpeechSystem
    {
        /// <summary>
        /// 누군가 말했을 때 알림. 말풍선처럼 "보여주기만" 하는 쪽이 듣는다.
        /// event: C의 콜백 함수 목록과 같다. += 로 함수를 등록해 두면 Invoke 때 모두 불린다.
        /// </summary>
        public static event Action<SpeechEvent> Spoken;

        public static void Broadcast(SpeechEvent speech)
        {
            Debug.Log($"[말소리] {speech.Speaker.DisplayName}: {speech.Text}", speech.Speaker);
            Spoken?.Invoke(speech);

            // 목록을 도는 중에 누가 사라지거나 생겨도 안전하도록 복사본을 돈다.
            foreach (var hearing in Hearing.All.ToArray())
            {
                // 들리는지, 멀어서 뭉개지는지(R-014c)는 귀가 스스로 판단한다.
                if (hearing != null && hearing.isActiveAndEnabled)
                    hearing.TryReceive(speech);
            }
        }
    }
}
