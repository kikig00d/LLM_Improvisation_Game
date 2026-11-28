using System;

namespace Improv.Llm
{
    // =====================================================================
    // LLM과 주고받는 데이터 묶음. MonoBehaviour가 아닌 일반 클래스라 한 파일에 모아 둔다.
    // [Serializable]: JsonUtility가 이 클래스를 JSON으로 바꾸거나 JSON에서 채울 수 있게 표시한다.
    // C로 치면 필드만 있는 struct를 직렬화 가능하다고 표시하는 것과 같다.
    // =====================================================================

    /// <summary>대화 한 줄. role은 "system"(캐릭터 설정), "user"(상대의 말), "assistant"(NPC의 말).</summary>
    [Serializable]
    public class ChatMessage
    {
        public string role;
        public string content;

        public ChatMessage(string role, string content)
        {
            this.role = role;
            this.content = content;
        }

        public static ChatMessage System(string text) => new("system", text);
        public static ChatMessage User(string text) => new("user", text);
        public static ChatMessage Assistant(string text) => new("assistant", text);
    }

    /// <summary>응답 결과의 종류. 실패해도 이유를 구분해 로그와 화면에 보여준다 (E-005).</summary>
    public enum LlmStatus
    {
        Ok,
        Timeout,        // 정해진 시간 안에 답이 안 옴
        ConnectionError,// 서버가 꺼져 있거나 주소가 틀림
        HttpError,      // 서버는 받았지만 오류 코드로 답함 (모델 이름 오타 등)
        BadResponse,    // 답은 왔지만 내용을 해석할 수 없음
        Cancelled       // 게임이 끝나는 등 우리가 먼저 취소함
    }

    /// <summary>LLM에 한 번 물어본 결과. 성공이든 실패든 이것 하나로 돌려준다.</summary>
    public class LlmReply
    {
        public LlmStatus Status;
        public string Text;          // NPC가 말할 문장 (생각 과정은 뺀 것)
        public string Reasoning;     // 생각하는 모델이 따로 내놓은 생각 과정 (있을 때만)
        public string Error;         // 실패 이유
        public string Model;
        public double ElapsedSeconds;
        public int PromptTokens;     // 보낸 글의 토큰 수 (서버가 알려줄 때만)
        public int CompletionTokens; // 받은 답의 토큰 수

        public bool Ok => Status == LlmStatus.Ok;
    }

    /// <summary>
    /// 한 번의 요청에만 쓰는 설정 (T-5). 비어 있는 항목은 LlmSettings의 기본값을 쓴다.
    /// NPC 집중도에 따라 "얼마나 공들여 생각할지"를 요청마다 바꿀 때 쓴다.
    /// </summary>
    public class LlmRequestOptions
    {
        public string ReasoningEffort;   // "none"이면 생각 끔, "low" 등이면 생각 켬. null이면 기본값
        public int MaxTokens;            // 0이면 기본값. 생각을 켜면 생각 글자도 여기에 들어가서 넉넉해야 한다
        public int? Seed;                // 있으면 같은 씨드 + 같은 질문에 같은 답을 내려 한다 (T-8 캐릭터 생성 재현). int?는 "값이 없을 수도 있는 int"
        public int TimeoutSeconds;       // 0이면 기본값(LlmSettings). 판 시작 때 과거 쓰기처럼 한 번뿐인 긴 답은 길게 준다
        public string JsonSchema;        // 있으면 답을 이 JSON 스키마로 묶는다(서버가 enum 밖 단어를 못 내게 막음, R-032c). 스키마 JSON 글 그대로
    }
}
