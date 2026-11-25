using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Improv.Characters;
using Improv.Characters.Speech;
using Improv.Llm;
using Improv.World;
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

        [Header("여러 NPC (계획서 T-4, Q-6 [가정])")]
        [Tooltip("말을 듣고 LLM에 묻기 전 뜸 들이는 시간 범위 (초). NPC마다 무작위라 답이 한꺼번에 몰리지 않고, 그 사이 남이 먼저 한 말도 듣고 판단한다.")]
        [SerializeField] Vector2 replyDelayRange = new(0.3f, 1.5f);
        [Tooltip("플레이어가 끼지 않은 NPC끼리의 말이 이 번호에 닿으면 더 묻지 않고 듣기만 한다. LLM 호출량을 지키는 안전장치 (P-4).")]
        [SerializeField] int maxNpcChain = 6;

        // =====================================================================
        // #2. 상태
        // =====================================================================

        Speaker speaker;
        Hearing hearing;
        RoomPresence roomPresence;   // 없어도 된다. 있으면 지금 방 이름을 프롬프트에 넣는다
        CharacterAnimator characterAnimator;   // 없어도 된다. 있으면 LLM이 고를 때 돌아볼 수 있다
        Improv.Characters.Sight.Vision vision;  // 없어도 된다. 있으면 지금 보이는 사람을 프롬프트에 넣는다 (P-3)
        NpcAttention attention;                 // 없어도 된다. 있으면 집중도에 따라 생각하는 노력을 바꾼다 (T-5)
        CharacterVitals vitals;                 // 없어도 된다. 있으면 몸 상태를 느낌으로 프롬프트에 넣고, 죽으면 멈춘다 (R-020)
        readonly List<string> feelings = new();
        readonly List<Improv.Characters.Sight.SightTarget> seenBuffer = new();
        ILlmClient client;

        // 단기 기억: 들은 말과 내가 한 말, 그리고 그때의 시각. 시간이 지나면 잊는다 (R-016d).
        // (ChatMessage, float) 는 튜플: 이름 없는 작은 struct를 그 자리에서 만드는 문법.
        readonly List<(ChatMessage message, float time)> shortTerm = new();
        // 장기 기억: LLM이 집중해서 기억하기로 고른 짧은 메모 (R-016d).
        readonly List<string> longTerm = new();
        readonly List<SpeechEvent> pending = new();       // 답을 만드는 동안 새로 들린 말. 선입선출로 다음 차례에 처리
        // 이름 → 그 목소리가 마지막으로 들린 곳. id가 아니라 이름으로 기억하는 것은 일부러다:
        // NPC는 들은 이름만 안다(P-3). 동명이인이면 마지막에 들린 쪽으로 돌아보며 헷갈린다 (F-17).
        readonly Dictionary<string, Vector2> lastHeardFrom = new();
        bool busy;
        int requestTicket;
        int replyChainDepth;   // 지금 만드는 답이 몇 번째 NPC끼리 말인가. 답할 때 Speaker.Say에 넘긴다

        // "이름: " 모양의 앞머리를 찾는 패턴. Clean()에서 쓴다.
        static readonly Regex NamePrefix = new(@"^\s*[^\s:：]{1,10}\s*[:：]\s*");
        // 일본 가나(3040~30FF)와 한자(4E00~9FFF). qwen3이 가끔 섞어 쓴다("막ってる", "빈肚子"). 한국어 게임이라 그런 대사는 버린다.
        static readonly Regex ForeignScript = new(@"[\u3040-\u30FF\u4E00-\u9FFF]");

        // 내가 최근에 한 말. 같은 말을 그대로 되풀이하면 버린다 (T-4 시험에서 같은 문장을 세 번 말했다).
        readonly List<string> recentLines = new();
        const int RecentLineCount = 4;

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
            attention = GetComponent<NpcAttention>();
            vitals = GetComponent<CharacterVitals>();

            if (sheet == null) { Debug.LogError($"[NpcDialogueBrain] '{name}'의 캐릭터 시트가 비어 있습니다.", this); enabled = false; return; }
            if (llm == null) { Debug.LogError($"[NpcDialogueBrain] '{name}'의 LLM 설정이 비어 있습니다.", this); enabled = false; return; }

            speaker.SetDisplayName(sheet.displayName);   // 시트의 이름은 "처음 이름". 그 뒤로는 신원(CharacterIdentity)이 이름의 주인이다
            client = llm.CreateClient();
            if (attention != null) attention.Init(sheet.baselineAttention);
        }

        void OnEnable()
        {
            hearing.Heard += OnHeard;
            if (vitals != null) vitals.Died += OnDied;
        }

        void OnDisable()
        {
            hearing.Heard -= OnHeard;
            if (vitals != null) vitals.Died -= OnDied;
        }

        // 죽으면 두뇌도 멈춘다 (R-022). 진행 중인 LLM 답은 destroyCancellationToken이 아니라 enabled로 막는다.
        void OnDied()
        {
            pending.Clear();
            speaker.ShowThinking = false;
            enabled = false;
        }

        // =====================================================================
        // #4. 듣기와 답하기
        // =====================================================================

        void OnHeard(SpeechEvent speech)
        {
            if (speech.Speaker == speaker) return;   // 내 목소리도 내 귀에 들리지만 대답할 대상은 아니다

            // 돌아볼지는 여기서 정하지 않는다. 어디서 들렸는지만 기억해 두고, LLM이 고르면 그쪽을 본다 (R-016b).
            lastHeardFrom[speech.SpeakerName] = speech.Origin;
            // T-5: 내 이름이 들리면 귀가 쫑긋한다(집중도 오름). 대답할지는 여전히 LLM이 고른다.
            if (attention != null && speech.Text.Contains(speaker.DisplayName)) attention.OnNameCalled();
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
                    // Q-6: 바로 묻지 않고 잠깐 뜸을 들인다. 그 사이 다른 사람이 먼저 답하면 그 말도 pending에 쌓여
                    // 함께 LLM에 넘어가므로, "누가 이미 대답했네" 하고 스스로 판단할 수 있다 (R-016e).
                    await Awaitable.WaitForSecondsAsync(UnityEngine.Random.Range(replyDelayRange.x, replyDelayRange.y), cancel);

                    // 이번에 들은 말 중 가장 작은 번호를 기준으로 한다. 플레이어 말(0)이 섞였으면 다시 처음부터 센다.
                    int depth = int.MaxValue;
                    foreach (var heard in pending)
                    {
                        shortTerm.Add((ChatMessage.User($"{heard.SpeakerName}: {heard.Text}"), heard.Time));
                        depth = Mathf.Min(depth, heard.ChainDepth);
                    }
                    pending.Clear();
                    Forget();

                    // T-4 안전장치: NPC끼리 너무 오래 이어지면 듣기만 한다(기억에는 남는다). 대답할지 고르는 건 여전히 LLM이다.
                    if (depth >= maxNpcChain)
                    {
                        Debug.Log($"[NPC {speaker.DisplayName}] NPC끼리 말이 {depth}번 이어져 이번에는 듣기만 함", this);
                        continue;
                    }
                    replyChainDepth = depth + 1;

                    string room = roomPresence != null && roomPresence.CurrentRoom != null
                        ? roomPresence.CurrentRoom.DisplayName : "산장 안";
                    float attentionValue = attention != null ? attention.Value : 0.5f;
                    string attentionLevel = attention != null ? attention.LevelName : "보통";
                    if (vitals != null) vitals.DescribeFeelings(feelings); else feelings.Clear();
                    var messages = new List<ChatMessage> { ChatMessage.System(NpcPrompt.BuildSystem(sheet, speaker.DisplayName, room, VisibleNames(), longTerm, attentionValue, attentionLevel, feelings)) };
                    // T-5: 집중도가 낮으면 최근 몇 줄만 떠올린다. 들은 말 자체는 단기 기억에 그대로 있다(흐려지지 않음).
                    int lines = attention != null ? attention.HistoryLines(shortTerm.Count) : shortTerm.Count;
                    for (int i = shortTerm.Count - lines; i < shortTerm.Count; i++) messages.Add(shortTerm[i].message);
                    LlmRequestOptions options = attention?.RequestOptions();
                    Debug.Log($"[NPC {speaker.DisplayName}] 집중도 {attentionValue:0.00}({attentionLevel}): 기억 {lines}줄, 생각 {options?.ReasoningEffort ?? "기본"}", this);

                    int ticket = ++requestTicket;
                    _ = ShowThinkingAfterDelay(ticket, cancel);
                    // jsonMode: 대사와 행동을 JSON 하나로 함께 고르게 한다. 호출 횟수는 그대로다 (P-4).
                    LlmReply reply = await client.SendAsync(messages, cancel, jsonMode: true, options);
                    requestTicket++;              // 늦게 깨어난 "..." 표시가 켜지지 않게 번호를 바꾼다
                    speaker.ShowThinking = false;

                    if (reply.Status == LlmStatus.Cancelled) return;

                    // R-017a: LLM은 실제 시간으로 돌아서 멈춘 동안에도 답이 온다. 그 답은 들고 있다가 풀린 뒤 말한다.
                    // NextFrameAsync는 시간 배율이 0이어도 화면 프레임마다 깨어난다.
                    while (GameClock.Instance != null && GameClock.Instance.IsPaused) await Awaitable.NextFrameAsync(cancel);

                    if (!reply.Ok)
                    {
                        // E-005: 실패하면 "..."라고만 말하고 콘솔에 오류를 남긴다. 실패한 답은 기억에 넣지 않는다.
                        Debug.LogError($"[NPC {speaker.DisplayName}] LLM 응답 실패 ({reply.Status}): {reply.Error}", this);
                        speaker.Say("...", chainDepth: replyChainDepth);
                        continue;
                    }

                    if (!enabled) return;   // 답을 기다리는 사이 죽었다 (R-022)

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
            public float attention = -1f;   // 이 일을 얼마나 신경 쓰나 (0~1, T-5). 답에 없으면 -1로 남아 그대로 둔다
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
            // 집중도 (T-5). LLM이 이 캐릭터가 지금 일을 얼마나 신경 쓰는지 골랐다. 다음 판단의 노력이 여기에 따라 달라진다.
            if (attention != null && decision.attention >= 0f)
            {
                float before = attention.Value;
                attention.Set(decision.attention);
                Debug.Log($"[NPC {speaker.DisplayName}] 집중도 {before:0.00} → {attention.Value:0.00} ({attention.LevelName})", this);
            }

            // 돌아보기 (R-016b). 들은 적 있는 목소리만 찾을 수 있다. 모르는 이름이면 무시한다.
            if (!string.IsNullOrWhiteSpace(decision.look_at) && characterAnimator != null)
            {
                if (lastHeardFrom.TryGetValue(decision.look_at.Trim(), out Vector2 from)) characterAnimator.FaceTowards(from);
                else Debug.Log($"[NPC {speaker.DisplayName}] 모르는 사람을 보려 함: {decision.look_at}", this);
            }

            // 집중해서 기억하기 (R-016d). 고르지 않은 말은 단기 기억과 함께 시간이 지나면 사라진다.
            // 이미 적어 둔 메모와 똑같으면 다시 적지 않는다. 같은 메모가 장기 기억을 채워 다른 기억을 밀어내지 않게.
            if (!string.IsNullOrWhiteSpace(decision.remember) && !longTerm.Contains(decision.remember.Trim()))
            {
                longTerm.Add(decision.remember.Trim());
                if (longTerm.Count > maxLongTermNotes) longTerm.RemoveAt(0);
                Debug.Log($"[NPC {speaker.DisplayName}] 기억함: {decision.remember}", this);
            }

            // 말하기. 말하지 않기로 고른 것도 선택이다.
            string line = Clean(decision.say ?? "");
            if (line.Length == 0) { Debug.Log($"[NPC {speaker.DisplayName}] 대답하지 않기로 함", this); return; }

            // 품질 거르개: 반응을 고르는 게 아니라 깨진 답을 버리는 것이다. 버린 답은 콘솔에 남긴다.
            if (ForeignScript.IsMatch(line)) { Debug.LogWarning($"[NPC {speaker.DisplayName}] 한국어가 아닌 글자가 섞여 버림: {line}", this); return; }
            if (recentLines.Contains(line)) { Debug.Log($"[NPC {speaker.DisplayName}] 방금 한 말을 되풀이해 버림: {line}", this); return; }

            recentLines.Add(line);
            if (recentLines.Count > RecentLineCount) recentLines.RemoveAt(0);
            speaker.Say(line, chainDepth: replyChainDepth);
        }

        string Clean(string text)
        {
            string line = text.Trim();
            // 모델이 규칙을 어기고 "한도윤: ..."처럼 자기 이름표를 붙이면 떼어 낸다. 남의 이름이면 건드리지 않는다.
            Match prefix = NamePrefix.Match(line);
            if (prefix.Success && prefix.Value.Contains(speaker.DisplayName)) line = line.Substring(prefix.Length);
            return line.Trim('"', '“', '”', '\'', ' ');
        }
    }
}
