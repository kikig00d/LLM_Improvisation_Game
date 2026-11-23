using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Improv.Characters;
using Improv.Characters.Speech;
using Improv.Llm;
using UnityEngine;

namespace Improv.Npc
{
    /// <summary>
    /// NPC의 대화 두뇌. 귀(Hearing)로 들은 말만 모아 LLM에 묻고, 받은 답을 입(Speaker)으로 말한다 (R-016, P-3).
    /// 대화 상대를 정하지 않는다. 들린 말이면 누구의 말이든 대화 기록에 쌓인다 (R-014a).
    /// </summary>
    [RequireComponent(typeof(Speaker), typeof(Hearing))]
    public class NpcDialogueBrain : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [SerializeField] NpcCharacterSheet sheet;
        [SerializeField] LlmSettings llm;
        [Tooltip("단기 기억에 담는 최대 줄 수. 길수록 LLM이 느려진다.")]
        [SerializeField] int maxHistoryMessages = 16;
        [Tooltip("들은 말을 잊기까지의 시간 (초). R-016d, O-008 [가정]")]
        [SerializeField] float shortTermSeconds = 120f;
        [Tooltip("집중해서 기억한 것(remember)을 몇 개까지 간직하는지. 넘치면 오래된 것부터 잊는다. O-008 [가정]")]
        [SerializeField] int maxLongTermNotes = 10;
        [Tooltip("이 시간보다 답이 늦으면 머리 위에 ...를 띄운다 (E-004).")]
        [SerializeField] float thinkingIndicatorDelay = 1.0f;

        // =====================================================================
        // #2. 상태
        // =====================================================================

        Speaker speaker;
        Hearing hearing;
        RoomPresence roomPresence;   // 없어도 된다. 있으면 지금 방 이름을 프롬프트에 넣는다
        CharacterAnimator characterAnimator;   // 없어도 된다. 있으면 LLM이 고를 때 돌아볼 수 있다
        Improv.Characters.Sight.Vision vision;  // 없어도 된다. 있으면 지금 보이는 사람을 프롬프트에 넣는다 (P-3)
        readonly List<Improv.Characters.Sight.SightTarget> seenBuffer = new();
        ILlmClient client;

        // 단기 기억: 들은 말과 내가 한 말, 그리고 그때의 시각. 시간이 지나면 잊는다 (R-016d).
        // (ChatMessage, float) 는 튜플: 이름 없는 작은 struct를 그 자리에서 만드는 문법.
        readonly List<(ChatMessage message, float time)> shortTerm = new();
        // 장기 기억: LLM이 집중해서 기억하기로 고른 짧은 메모 (R-016d).
        readonly List<string> longTerm = new();
        readonly List<SpeechEvent> pending = new();       // 답을 만드는 동안 새로 들린 말. 선입선출로 다음 차례에 처리
        readonly Dictionary<string, Vector2> lastHeardFrom = new();   // 이름 → 그 목소리가 마지막으로 들린 곳
        bool busy;
        int requestTicket;

        // "이름: " 모양의 앞머리를 찾는 패턴. Clean()에서 쓴다.
        static readonly Regex NamePrefix = new(@"^\s*[^\s:：]{1,10}\s*[:：]\s*");

        public bool IsBusy => busy;

        /// <summary>
        /// 지금 내 눈에 보이는 사람들의 이름 (R-011, P-3). 눈이 본 것만 LLM에 넘긴다.
        /// 돌아보기(look_at)를 고르면 몸이 돌고, 다음 답부터는 돌아본 쪽 사람이 보인다.
        /// </summary>
        public List<string> VisibleNames()
        {
            var names = new List<string>();
            if (vision == null) return names;
            vision.CollectVisible(seenBuffer);
            foreach (var target in seenBuffer)
            {
                var other = target.GetComponent<Speaker>();
                if (other != null) names.Add(other.DisplayName);
            }
            return names;
        }
        public int ShortTermCount => shortTerm.Count;
        public IReadOnlyList<string> LongTerm => longTerm;

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Awake()
        {
            speaker = GetComponent<Speaker>();
            hearing = GetComponent<Hearing>();
            roomPresence = GetComponent<RoomPresence>();
            characterAnimator = GetComponent<CharacterAnimator>();
            vision = GetComponent<Improv.Characters.Sight.Vision>();

            if (sheet == null) { Debug.LogError($"[NpcDialogueBrain] '{name}'의 캐릭터 시트가 비어 있습니다.", this); enabled = false; return; }
            if (llm == null) { Debug.LogError($"[NpcDialogueBrain] '{name}'의 LLM 설정이 비어 있습니다.", this); enabled = false; return; }

            speaker.SetDisplayName(sheet.displayName);
            client = llm.CreateClient();
        }

        void OnEnable() => hearing.Heard += OnHeard;
        void OnDisable() => hearing.Heard -= OnHeard;

        // =====================================================================
        // #4. 듣기와 답하기
        // =====================================================================

        void OnHeard(SpeechEvent speech)
        {
            if (speech.Speaker == speaker) return;   // 내 목소리도 내 귀에 들리지만 대답할 대상은 아니다

            // 돌아볼지는 여기서 정하지 않는다. 어디서 들렸는지만 기억해 두고, LLM이 고르면 그쪽을 본다 (R-016b).
            lastHeardFrom[speech.Speaker.DisplayName] = speech.Origin;
            pending.Add(speech);
            if (!busy) _ = ReplyLoopAsync(destroyCancellationToken);
        }

        async Awaitable ReplyLoopAsync(CancellationToken cancel)
        {
            busy = true;
            try
            {
                // 답을 만드는 사이 새로 들린 말이 있으면 한 번 더 돈다.
                while (pending.Count > 0)
                {
                    foreach (var heard in pending)
                        shortTerm.Add((ChatMessage.User($"{heard.Speaker.DisplayName}: {heard.Text}"), heard.Time));
                    pending.Clear();
                    Forget();

                    string room = roomPresence != null && roomPresence.CurrentRoom != null
                        ? roomPresence.CurrentRoom.DisplayName : "산장 안";
                    var messages = new List<ChatMessage> { ChatMessage.System(NpcPrompt.BuildSystem(sheet, room, VisibleNames(), longTerm)) };
                    foreach (var memory in shortTerm) messages.Add(memory.message);

                    int ticket = ++requestTicket;
                    _ = ShowThinkingAfterDelay(ticket, cancel);
                    // jsonMode: 대사와 행동을 JSON 하나로 함께 고르게 한다. 호출 횟수는 그대로다 (P-4).
                    LlmReply reply = await client.SendAsync(messages, cancel, jsonMode: true);
                    requestTicket++;              // 늦게 깨어난 "..." 표시가 켜지지 않게 번호를 바꾼다
                    speaker.ShowThinking = false;

                    if (reply.Status == LlmStatus.Cancelled) return;

                    if (!reply.Ok)
                    {
                        // E-005: 실패하면 "..."라고만 말하고 콘솔에 오류를 남긴다. 실패한 답은 기억에 넣지 않는다.
                        Debug.LogError($"[NPC {speaker.DisplayName}] LLM 응답 실패 ({reply.Status}): {reply.Error}", this);
                        speaker.Say("...");
                        continue;
                    }

                    // 모델이 쓴 JSON을 그대로 기억에 넣는다. 다음 답도 같은 형식을 따라 하게 된다.
                    shortTerm.Add((ChatMessage.Assistant(reply.Text), Time.time));
                    ActOn(ParseDecision(reply.Text));
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                busy = false;
                speaker.ShowThinking = false;
            }
        }

        async Awaitable ShowThinkingAfterDelay(int ticket, CancellationToken cancel)
        {
            try
            {
                await Awaitable.WaitForSecondsAsync(thinkingIndicatorDelay, cancel);
                if (ticket == requestTicket) speaker.ShowThinking = true;   // E-004
            }
            catch (OperationCanceledException) { }
        }

        // R-016d: 오래된 말은 잊는다. 시간이 지났거나, 줄 수가 넘치면 오래된 것부터.
        void Forget()
        {
            shortTerm.RemoveAll(m => Time.time - m.time > shortTermSeconds);
            int extra = shortTerm.Count - maxHistoryMessages;
            if (extra > 0) shortTerm.RemoveRange(0, extra);
        }

        // =====================================================================
        // #5. LLM이 고른 반응 실행
        // =====================================================================

        /// <summary>LLM이 고른 반응. JSON 키 이름이 곧 필드 이름이다.</summary>
        [Serializable]
        class NpcDecision
        {
            public string look_at;   // 돌아볼 사람 이름. 빈 문자열이면 돌아보지 않는다
            public string say;       // 할 말. 빈 문자열이면 말하지 않는다
            public string remember;  // 집중해서 기억할 짧은 메모. 빈 문자열이면 기억하지 않는다 (R-016d)
        }

        NpcDecision ParseDecision(string json)
        {
            try
            {
                var decision = JsonUtility.FromJson<NpcDecision>(json);
                if (decision != null && (decision.say != null || decision.look_at != null)) return decision;
            }
            catch (ArgumentException) { }

            // JSON이 깨졌으면 글 전체를 대사로 본다. 어떤 답이 왔는지는 LLM 디버그 로그에 남아 있다.
            Debug.LogWarning($"[NPC {speaker.DisplayName}] 답이 JSON 형식이 아니라 전체를 대사로 씀: {json}", this);
            return new NpcDecision { look_at = "", say = json };
        }

        void ActOn(NpcDecision decision)
        {
            // 돌아보기 (R-016b). 들은 적 있는 목소리만 찾을 수 있다. 모르는 이름이면 무시한다.
            if (!string.IsNullOrWhiteSpace(decision.look_at) && characterAnimator != null)
            {
                if (lastHeardFrom.TryGetValue(decision.look_at.Trim(), out Vector2 from)) characterAnimator.FaceTowards(from);
                else Debug.Log($"[NPC {speaker.DisplayName}] 모르는 사람을 보려 함: {decision.look_at}", this);
            }

            // 집중해서 기억하기 (R-016d). 고르지 않은 말은 단기 기억과 함께 시간이 지나면 사라진다.
            if (!string.IsNullOrWhiteSpace(decision.remember))
            {
                longTerm.Add(decision.remember.Trim());
                if (longTerm.Count > maxLongTermNotes) longTerm.RemoveAt(0);
                Debug.Log($"[NPC {speaker.DisplayName}] 기억함: {decision.remember}", this);
            }

            // 말하기. 말하지 않기로 고른 것도 선택이다.
            string line = Clean(decision.say ?? "");
            if (line.Length > 0) speaker.Say(line);
            else Debug.Log($"[NPC {speaker.DisplayName}] 대답하지 않기로 함", this);
        }

        string Clean(string text)
        {
            string line = text.Trim();
            // 모델이 규칙을 어기고 "한도윤: ..."처럼 자기 이름표를 붙이면 떼어 낸다. 남의 이름이면 건드리지 않는다.
            Match prefix = NamePrefix.Match(line);
            if (prefix.Success && prefix.Value.Contains(sheet.displayName)) line = line.Substring(prefix.Length);
            return line.Trim('"', '“', '”', '\'', ' ');
        }
    }
}
