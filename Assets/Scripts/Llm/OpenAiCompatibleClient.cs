using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace Improv.Llm
{
    /// <summary>
    /// OpenAI 호환 HTTP 주소(/v1/chat/completions)로 LLM에 묻는 연결.
    /// Ollama, LM Studio, llama.cpp 서버가 모두 이 형식을 지원하므로 주소만 바꾸면 된다.
    /// MonoBehaviour가 아닌 일반 클래스다. 씬에 붙이지 않고 new로 만들어 쓴다.
    /// </summary>
    public class OpenAiCompatibleClient : ILlmClient
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        readonly string baseUrl;
        readonly int timeoutSeconds;
        readonly float temperature;
        readonly int maxTokens;
        readonly string reasoningEffort;

        public string Model { get; }

        /// <param name="baseUrl">예: http://localhost:11434/v1 (Ollama 기본 주소)</param>
        /// <param name="reasoningEffort">생각하는 모델(qwen3 등)의 생각 단계 조절. "none"이면 생각을 끄고 바로 답한다. 빈 문자열이면 보내지 않는다.</param>
        public OpenAiCompatibleClient(string baseUrl, string model, int timeoutSeconds,
                                      float temperature, int maxTokens, string reasoningEffort)
        {
            this.baseUrl = baseUrl.TrimEnd('/');
            Model = model;
            this.timeoutSeconds = timeoutSeconds;
            this.temperature = temperature;
            this.maxTokens = maxTokens;
            this.reasoningEffort = reasoningEffort;
        }

        // =====================================================================
        // #2. JSON 모양 (OpenAI 형식 그대로. 필드 이름이 JSON 키가 되므로 소문자_밑줄을 쓴다)
        // =====================================================================

        [Serializable]
        class ChatRequest
        {
            public string model;
            public ChatMessage[] messages;
            public bool stream;        // false: 답을 한 번에 받는다. 글자 단위로 흘려받기는 M4 이후 필요하면
            public float temperature;  // 높을수록 답이 다양하고 엉뚱해진다
            public int max_tokens;     // 답 길이 상한. NPC 대사는 짧으므로 작게 잡는다
        }

        [Serializable] class ChatResponse { public string model; public Choice[] choices; public Usage usage; }
        [Serializable] class Choice { public ResponseMessage message; public string finish_reason; }
        [Serializable] class ResponseMessage { public string role; public string content; public string reasoning; }
        [Serializable] class Usage { public int prompt_tokens; public int completion_tokens; }

        // 응답 본문에 <think>...</think>로 생각 과정이 섞여 오는 모델을 위한 정리용 패턴.
        static readonly Regex ThinkBlock = new(@"<think>.*?</think>", RegexOptions.Singleline);

        // =====================================================================
        // #3. 보내고 받기
        // =====================================================================

        public async Awaitable<LlmReply> SendAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancel, bool jsonMode = false)
        {
            string url = baseUrl + "/chat/completions";
            string requestJson = BuildRequestJson(messages, jsonMode);

            var reply = new LlmReply { Model = Model };
            var stopwatch = Stopwatch.StartNew();
            string responseText = null;
            long httpCode = 0;

            // using: 블록이 끝나면 요청 객체를 자동으로 정리한다. C의 free()를 빠뜨리지 않게 해 주는 문법.
            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestJson));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = timeoutSeconds;

                // 게임이 끝나거나 오브젝트가 사라지면 요청도 끊는다. 안 그러면 Play를 멈춘 뒤에도 기다린다.
                using (cancel.Register(request.Abort))
                {
                    await request.SendWebRequest();
                }

                stopwatch.Stop();
                reply.ElapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                httpCode = request.responseCode;
                responseText = request.downloadHandler?.text;

                if (cancel.IsCancellationRequested)
                {
                    reply.Status = LlmStatus.Cancelled;
                    reply.Error = "취소됨";
                }
                else if (request.result == UnityWebRequest.Result.Success)
                {
                    ParseResponse(responseText, reply);
                }
                else
                {
                    reply.Status = ClassifyFailure(request);
                    reply.Error = $"{request.error} {responseText}".Trim();
                }
            }

            // 성공이든 실패든 화이트박스 로그에 그대로 남긴다 (GDD 10장 LLM 디버그).
            LlmDebugLog.Write(url, requestJson, httpCode, responseText, reply);
            return reply;
        }

        // =====================================================================
        // #4. 도우미 함수
        // =====================================================================

        string BuildRequestJson(IReadOnlyList<ChatMessage> messages, bool jsonMode)
        {
            var body = new ChatRequest
            {
                model = Model,
                messages = new List<ChatMessage>(messages).ToArray(),
                stream = false,
                temperature = temperature,
                max_tokens = maxTokens
            };
            string json = JsonUtility.ToJson(body);

            // JsonUtility는 빈 값도 항상 써 버려서, 선택 항목은 필요할 때만 끝에 끼워 넣는다.
            if (!string.IsNullOrEmpty(reasoningEffort))
                json = json.Insert(json.Length - 1, $",\"reasoning_effort\":\"{reasoningEffort}\"");
            // OpenAI 형식의 "JSON으로만 답하기" 옵션. 서버가 문법에 맞는 JSON만 나오게 막아 준다.
            if (jsonMode)
                json = json.Insert(json.Length - 1, ",\"response_format\":{\"type\":\"json_object\"}");
            return json;
        }

        static void ParseResponse(string json, LlmReply reply)
        {
            ChatResponse parsed = null;
            try { parsed = JsonUtility.FromJson<ChatResponse>(json); }
            catch (Exception e) { reply.Error = "JSON 해석 실패: " + e.Message; }

            // 가드 절: 답의 모양이 기대와 다르면 바로 실패로 끝낸다.
            if (parsed?.choices == null || parsed.choices.Length == 0 || parsed.choices[0].message == null)
            {
                reply.Status = LlmStatus.BadResponse;
                reply.Error ??= "choices가 비어 있음";
                return;
            }

            ResponseMessage message = parsed.choices[0].message;
            string content = message.content ?? "";

            // 생각 과정은 NPC 대사에서 빼고 따로 보관한다. 화이트박스 로그에서는 볼 수 있다.
            Match think = ThinkBlock.Match(content);
            reply.Reasoning = !string.IsNullOrEmpty(message.reasoning) ? message.reasoning
                            : think.Success ? think.Value : null;
            reply.Text = ThinkBlock.Replace(content, "").Trim();

            if (parsed.usage != null)
            {
                reply.PromptTokens = parsed.usage.prompt_tokens;
                reply.CompletionTokens = parsed.usage.completion_tokens;
            }

            if (string.IsNullOrEmpty(reply.Text))
            {
                // 생각만 하다 토큰 상한에 걸리면 대사가 비어 온다. 실패로 보고 "..."로 처리되게 한다 (E-005).
                reply.Status = LlmStatus.BadResponse;
                reply.Error = $"대사가 비어 있음 (finish_reason: {parsed.choices[0].finish_reason})";
                return;
            }
            reply.Status = LlmStatus.Ok;
        }

        static LlmStatus ClassifyFailure(UnityWebRequest request)
        {
            if (request.result == UnityWebRequest.Result.ProtocolError) return LlmStatus.HttpError;
            if (request.error != null && request.error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
                return LlmStatus.Timeout;
            return LlmStatus.ConnectionError;
        }
    }
}
