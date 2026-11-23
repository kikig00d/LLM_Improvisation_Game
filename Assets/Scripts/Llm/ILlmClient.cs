using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Improv.Llm
{
    /// <summary>
    /// "대화 목록을 보내면 답을 돌려준다"는 약속만 정한 인터페이스.
    /// C로 치면 함수 포인터 하나를 담은 struct와 같다. 게임 코드는 이것만 보고,
    /// 실제 연결(Ollama 서버, 나중에 게임에 내장하는 방식 등)은 구현 클래스만 갈아 끼운다 (GDD 10장, Q1).
    /// </summary>
    public interface ILlmClient
    {
        string Model { get; }

        /// <summary>
        /// Awaitable: 유니티 6의 비동기 작업. await 하는 동안 게임은 멈추지 않고 다음 프레임을 계속 그린다.
        /// C로 치면 콜백 함수를 넘기는 대신, 결과가 올 때까지 "여기서 잠깐 나갔다 돌아온다"고 표시하는 것.
        /// </summary>
        /// <param name="jsonMode">true면 답을 JSON 한 덩어리로만 받는다. NPC가 대사와 행동(돌아보기 등)을 함께 고를 때 쓴다.</param>
        Awaitable<LlmReply> SendAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancel, bool jsonMode = false);
    }
}
