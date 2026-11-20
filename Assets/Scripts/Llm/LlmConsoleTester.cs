using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Improv.Llm
{
    /// <summary>
    /// M3 확인용: 유니티에서 로컬 LLM에 문장을 보내고 받은 답을 Console에 찍는다 (GDD 11장 M3, R-016).
    /// 같은 대화를 여러 모델에 보내 품질과 응답 시간을 비교하는 데도 쓴다 (Q2).
    /// 사용법: Play 중에 Inspector에서 이 컴포넌트의 ⋮ 메뉴 → "LLM에 보내기".
    /// M4에서 진짜 NPC 대화가 생기면 이 컴포넌트는 지운다.
    /// </summary>
    public class LlmConsoleTester : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [Header("연결")]
        [SerializeField] string baseUrl = "http://localhost:11434/v1";
        [Tooltip("위에서부터 차례로 같은 질문을 보낸다. 하나만 두면 그 모델만 쓴다.")]
        [SerializeField] string[] models = { "llama3.1:8b" };
        [SerializeField] int timeoutSeconds = 60;
        [SerializeField, Range(0f, 1.5f)] float temperature = 0.7f;
        [SerializeField] int maxTokens = 120;
        [Tooltip("생각하는 모델(qwen3 등)의 생각 단계. none이면 끄고 바로 답한다. 비우면 보내지 않는다.")]
        [SerializeField] string reasoningEffort = "none";

        [Header("대화 내용")]
        [SerializeField, TextArea(6, 12)] string systemPrompt =
            "너는 눈보라로 고립된 겨울 산장의 관리인 '한도윤'이다. 50대 남자. 무뚝뚝하지만 책임감이 강하다.\n" +
            "비밀: 어젯밤 창고 열쇠를 잃어버렸다. 들키기 싫어서 말을 돌린다.\n" +
            "말투 규칙: 아주 짧고 투박한 문장. 쉬운 단어만 쓴다. 한 번에 1~2문장. 문법이 조금 어색해도 뜻은 분명하게.\n" +
            "예: \"눈 많다. 길 막혔다. 내일 본다.\"\n" +
            "설명, 괄호, 이모지, 따옴표를 쓰지 않는다. 자신이 AI라고 말하지 않는다. 대사만 말한다.";
        [Tooltip("플레이어가 하는 말. 각 줄을 따로 보낸다 (앞 대화 기억 없이).")]
        [SerializeField] string[] playerLines = { "너 누구다?", "어젯밤 무엇 했다?", "창고 열쇠 어디 있다?", "눈 언제 그친다?" };

        [Header("동작")]
        [SerializeField] bool sendOnStart = true;
        [Tooltip("이 시간보다 답이 늦으면 '생각 중'을 표시한다 (E-004).")]
        [SerializeField] float thinkingIndicatorDelay = 1.5f;

        // =====================================================================
        // #2. 상태
        // =====================================================================

        /// <summary>답을 기다리는 중인지. M4에서 NPC 머리 위 생각 중 표시가 이 값을 본다 (E-004).</summary>
        public bool IsThinking { get; private set; }

        bool running;
        int requestTicket; // 질문마다 1씩 올리는 번호. 늦게 깨어난 "생각 중" 알림이 엉뚱한 질문에 붙지 않게 한다

        // =====================================================================
        // #3. 유니티 이벤트
        // =====================================================================

        void Awake()
        {
            if (models == null || models.Length == 0) Debug.LogError("[LLM] 모델 이름이 비어 있음", this);
            if (playerLines == null || playerLines.Length == 0) Debug.LogError("[LLM] 보낼 문장이 비어 있음", this);
        }

        void Start()
        {
            if (sendOnStart) SendAll();
        }

        // =====================================================================
        // #4. 보내기
        // =====================================================================

        [ContextMenu("LLM에 보내기")]
        public void SendAll()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[LLM] Play 중에만 보낼 수 있음", this); return; }
            if (running) { Debug.LogWarning("[LLM] 이미 보내는 중", this); return; }

            // 반환값(Awaitable)을 버린다는 뜻의 _. 끝날 때까지 기다리지 않고 시작만 시킨다.
            _ = RunAsync(destroyCancellationToken);
        }

        async Awaitable RunAsync(CancellationToken cancel)
        {
            running = true;
            var summary = new StringBuilder("[LLM 비교 요약] (첫 호출은 모델을 메모리에 올리는 시간이 포함돼 따로 적는다)\n");

            try
            {
                foreach (string model in models)
                {
                    var client = new OpenAiCompatibleClient(baseUrl, model, timeoutSeconds, temperature, maxTokens, reasoningEffort);
                    double warmTotal = 0;
                    int warmCount = 0;
                    var lines = new StringBuilder();

                    for (int i = 0; i < playerLines.Length; i++)
                    {
                        string say = playerLines[i];
                        Debug.Log($"[플레이어 → {model}] {say}");

                        var messages = new List<ChatMessage> { ChatMessage.System(systemPrompt), ChatMessage.User(say) };
                        LlmReply reply = await AskWithThinkingIndicator(client, messages, cancel);
                        if (reply.Status == LlmStatus.Cancelled) return;

                        // E-005: 실패하면 NPC는 "..."라고만 말하고, 콘솔에 오류를 남긴다.
                        string npcLine = reply.Ok ? reply.Text : "...";
                        if (!reply.Ok) Debug.LogError($"[LLM] {model} 응답 실패 ({reply.Status}): {reply.Error}", this);
                        Debug.Log($"[NPC({model})] {npcLine}   ({reply.ElapsedSeconds:F2}초)");

                        lines.AppendLine($"    {say} → {npcLine}  ({reply.ElapsedSeconds:F2}초{(i == 0 ? ", 첫 호출" : "")})");
                        if (i > 0 && reply.Ok) { warmTotal += reply.ElapsedSeconds; warmCount++; }
                    }

                    string average = warmCount > 0 ? $"{warmTotal / warmCount:F2}초" : "측정 못 함";
                    summary.AppendLine($"■ {model}: 첫 호출 뒤 평균 {average}");
                    summary.Append(lines);
                }
                Debug.Log(summary.ToString());
            }
            catch (OperationCanceledException)
            {
                // Play를 멈춰서 기다리던 작업이 취소된 경우. 정상이므로 조용히 끝낸다.
            }
            finally
            {
                running = false;
                IsThinking = false;
            }
        }

        /// <summary>답을 기다리면서, 일정 시간이 지나면 "생각 중"을 알린다 (E-004).</summary>
        async Awaitable<LlmReply> AskWithThinkingIndicator(ILlmClient client, List<ChatMessage> messages, CancellationToken cancel)
        {
            IsThinking = true;
            int ticket = ++requestTicket;
            _ = ShowThinkingAfterDelay(client.Model, ticket, cancel);
            try
            {
                return await client.SendAsync(messages, cancel);
            }
            finally
            {
                IsThinking = false;
            }
        }

        async Awaitable ShowThinkingAfterDelay(string model, int ticket, CancellationToken cancel)
        {
            try
            {
                await Awaitable.WaitForSecondsAsync(thinkingIndicatorDelay, cancel);
                // 기다리는 사이 다음 질문이 시작됐으면 번호가 달라져 있으니 알리지 않는다.
                // 지금은 말풍선이 없어 콘솔에만 알린다. M4에서 NPC 머리 위 표시로 바꾼다.
                if (IsThinking && ticket == requestTicket) Debug.Log($"[NPC({model})] (생각 중...)");
            }
            catch (OperationCanceledException) { }
        }
    }
}
