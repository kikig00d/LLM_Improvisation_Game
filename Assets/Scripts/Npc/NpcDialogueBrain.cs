using System;
using System.Collections.Generic;
using System.Text;
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

        [Header("혼자 생각하기 (계획서 T-6, P-4 [가정])")]
        [Tooltip("아무 말도 안 들리고 할 일도 없으면 이 시간(게임 초)마다 한 번 혼자 무엇을 할지 생각한다. 50 = 게임 1시간")]
        [SerializeField] float idleThinkSeconds = 50f;
        [Tooltip("판이 시작하고 처음 한 번 둘러보고 생각하기까지의 시간(게임 초). 여럿이 한꺼번에 묻지 않게 이 범위에서 무작위")]
        [SerializeField] Vector2 firstThinkDelay = new(1f, 6f);
        [Tooltip("이 시간(게임 초) 넘게 못 본 사람이 눈에 들어오면 한 번 생각한다")]
        [SerializeField] float noticeAfterUnseen = 120f;
        [Tooltip("혼자 하는 생각(말을 들은 때 말고) 사이의 최소 간격(게임 초). 행동 → 생각 → 행동이 LLM 호출 고리가 되지 않게 (P-4)")]
        [SerializeField] float minSelfThinkInterval = 8f;

        // =====================================================================
        // #2. 상태
        // =====================================================================

        Speaker speaker;
        Hearing hearing;
        RoomPresence roomPresence;   // 없어도 된다. 있으면 지금 방 이름을 프롬프트에 넣는다
        CharacterAnimator characterAnimator;   // 없어도 된다. 있으면 LLM이 고를 때 돌아볼 수 있다
        Improv.Characters.Sight.Vision vision;  // 없어도 된다. 있으면 지금 보이는 사람을 프롬프트에 넣는다 (P-3)
        NpcAttention attention;                 // 없어도 된다. 있으면 집중도에 따라 생각하는 노력을 바꾼다 (T-5)
        CharacterAbilities abilities;           // 없어도 된다. 있으면 능력치를 숫자로 프롬프트에 넣는다 (R-031)
        CharacterVitals vitals;                 // 없어도 된다. 있으면 몸 상태를 느낌으로 프롬프트에 넣고, 죽으면 멈춘다 (R-020)
        readonly List<string> feelings = new();
        NpcActions actions;                     // 없어도 된다. 있으면 LLM이 고른 행동(가기, 줍기, 먹기 등)을 한다 (T-6)
        CharacterSleep sleep;
        float idleTimer;
        float feelingCheckTimer;
        string lastFeelings = "";
        string thinkReason;                     // 혼자 생각할 까닭. null이면 생각할 일이 없다
        float firstThinkAt = -1f;               // 처음 생각할 게임 시각. -1이면 이미 했다
        float nextSelfThinkAt;                  // 이 게임 시각 전에는 혼자 생각을 시작하지 않는다
        float noticeTimer;
        readonly Dictionary<int, float> lastSeenAt = new();   // 사람 id → 마지막으로 본 게임 시각
        readonly List<string> noticed = new();
        readonly List<Improv.Characters.Sight.SightTarget> seenBuffer = new();
        ILlmClient client;
        RelationBook relations;                 // 사람(과 이제까지의 나)에 대한 마음 (I-12, R-027). Start에서 시트의 묵은 감정으로 채운다
        readonly StringBuilder relationText = new();

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
        public RelationBook Relations => relations;   // 나중에 관계도 창(T-9)이 읽는다
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
            abilities = GetComponent<CharacterAbilities>();
            actions = GetComponent<NpcActions>();
            sleep = GetComponent<CharacterSleep>();

            if (sheet == null) { Debug.LogError($"[NpcDialogueBrain] '{name}'의 캐릭터 시트가 비어 있습니다.", this); enabled = false; return; }
            if (llm == null) { Debug.LogError($"[NpcDialogueBrain] '{name}'의 LLM 설정이 비어 있습니다.", this); enabled = false; return; }

            speaker.SetDisplayName(sheet.displayName);   // 시트의 이름은 "처음 이름". 그 뒤로는 신원(CharacterIdentity)이 이름의 주인이다
            client = llm.CreateClient();
            if (attention != null) attention.Init(sheet.baselineAttention);
        }

        /// <summary>
        /// 시트를 바꿔 끼운다 (T-8: 판이 시작할 때 CastGenerator가 만든 시트). 이름과 평소 집중도도 새 시트대로.
        /// 아직 아무 말도 듣기 전에만 부른다. 기억은 지우지 않는다.
        /// </summary>
        public void ApplySheet(NpcCharacterSheet newSheet)
        {
            if (newSheet == null) return;
            sheet = newSheet;
            speaker.SetDisplayName(sheet.displayName);
            if (attention != null) attention.Init(sheet.baselineAttention);
        }

        public NpcCharacterSheet Sheet => sheet;

        void OnEnable()
        {
            hearing.Heard += OnHeard;
            if (vitals != null) vitals.Died += OnDied;
            if (actions != null) actions.ActionFinished += OnActionFinished;
        }

        void OnDisable()
        {
            hearing.Heard -= OnHeard;
            if (vitals != null) vitals.Died -= OnDied;
            if (actions != null) actions.ActionFinished -= OnActionFinished;
        }

        // =====================================================================
        // 혼자 생각하기 (T-6, P-4). 평소에는 코드가 몸을 움직이고, LLM은 이럴 때만 부른다:
        // 들은 말이 있을 때(아래 OnHeard), 행동이 끝났을 때, 몸 상태가 달라졌을 때, 오래 아무 일 없을 때.
        // =====================================================================

        // Start는 처음 켜질 때 한 번 불린다. 시트를 만드는 동안 꺼져 있었다면 다 만든 뒤에 불린다 (T-8 CastGenerator).
        void Start()
        {
            // 판이 시작하면 각자 한 번 둘러보고 생각한다. T-8 확인 때 첫 채팅 전까지 아무도 움직이지 않았다(계기가 없어서).
            firstThinkAt = Time.time + UnityEngine.Random.Range(firstThinkDelay.x, firstThinkDelay.y);
            SeedRelations();
        }

        // 시트의 묵은 감정을 관계도에 옮긴다. Start는 CastGenerator가 시트를 끼운 뒤에 돈다(생성 중엔 두뇌가 꺼져 있다).
        // 시트는 이름으로 적혀 있어서 여기서 한 번만 id로 바꾼다. 그 뒤로 코드는 id로만 사람을 구분한다 (F-17).
        void SeedRelations()
        {
            var me = GetComponent<CharacterIdentity>();
            relations = new RelationBook(me != null ? me.Id : -1);
            relations.SetOld(relations.selfId, sheet.selfFeeling, sheet.selfWhy);
            foreach (var old in sheet.oldFeelings)
            {
                var who = CharacterIdentity.All.Find(c => c.DisplayName == old.name);
                if (who == null) { Debug.LogWarning($"[NPC {speaker.DisplayName}] 묵은 감정의 상대 '{old.name}'를 씬에서 못 찾음", this); continue; }
                relations.SetOld(who.Id, old.value, old.why);
            }
        }

        string DescribeRelations()
        {
            relationText.Clear();
            relations?.Describe(relationText, id => CharacterIdentity.Find(id)?.DisplayName, Time.time);
            return relationText.ToString();
        }

        void Update()
        {
            if (actions == null) return;
            bool asleep = sleep != null && sleep.IsAsleep;

            if (thinkReason != null && !busy && Time.time >= nextSelfThinkAt) _ = ReplyLoopAsync(destroyCancellationToken);

            if (firstThinkAt >= 0f && Time.time >= firstThinkAt)
            {
                firstThinkAt = -1f;
                NoticePeople(silent: true);   // 지금 보이는 사람은 처음 생각에 함께 들어가니 따로 알리지 않는다
                RequestThink("산장에 사람들이 모였다. 주변을 둘러보고 무엇을 할지 생각한다");
            }

            // 한동안 못 본 사람이 눈에 들어오면 한 번 생각한다 (과거로 얽힌 사람과 마주치는 순간 등).
            noticeTimer -= Time.deltaTime;
            if (noticeTimer <= 0f && firstThinkAt < 0f && !asleep)
            {
                noticeTimer = 0.5f;
                NoticePeople(silent: false);
            }

            // 몸 상태가 새로 나빠지면 한 번 생각한다 (배고파졌다, 지쳤다).
            feelingCheckTimer -= Time.deltaTime;
            if (feelingCheckTimer <= 0f && vitals != null)
            {
                feelingCheckTimer = 1f;
                vitals.DescribeFeelings(feelings);
                string now = string.Join(", ", feelings);
                if (now != lastFeelings)
                {
                    bool worse = now.Length > 0 && !asleep;
                    lastFeelings = now;
                    if (worse) RequestThink($"몸 상태가 달라졌다: {now}");
                }
            }

            // 할 일도, 들은 말도 없이 오래 지나면 한 번 생각한다. 자는 동안과 움직이는 동안은 세지 않는다.
            if (busy || asleep || actions.IsBusy || pending.Count > 0) { idleTimer = 0f; return; }
            idleTimer += Time.deltaTime;
            if (idleTimer >= idleThinkSeconds) RequestThink("한동안 아무 일도 없었다. 무엇을 할지 생각한다");
        }

        void OnActionFinished(string result) => RequestThink($"방금 한 일: {result}");

        // 보이는 사람을 훑어 "새로 눈에 들어온 사람"을 찾는다. 사람은 이름이 아니라 id로 센다 (F-17).
        void NoticePeople(bool silent)
        {
            if (vision == null) return;
            noticed.Clear();
            vision.CollectVisible(seenBuffer);
            foreach (var target in seenBuffer)
            {
                var other = target.GetComponent<Speaker>();
                var identity = target.GetComponent<CharacterIdentity>();
                if (other == null || identity == null) continue;
                bool fresh = !lastSeenAt.TryGetValue(identity.Id, out float last) || Time.time - last > noticeAfterUnseen;
                lastSeenAt[identity.Id] = Time.time;
                if (fresh) noticed.Add(other.DisplayName);
            }
            // "~이(가) 눈에 들어왔다"는 LLM이 눈(雪)으로 읽고 "눈 들어왔어?"라고 답했다(T-8 프리셋 시험). 조사 없이 쓴다.
            if (!silent && noticed.Count > 0) RequestThink($"새로 보이는 사람: {string.Join(", ", noticed)}");
        }

        /// <summary>혼자 생각할 까닭을 남긴다. 이미 생각 중이면 끝난 뒤 이어서 생각한다.</summary>
        void RequestThink(string reason)
        {
            if (!enabled) return;
            thinkReason = reason;
            idleTimer = 0f;
            if (!busy && Time.time >= nextSelfThinkAt) _ = ReplyLoopAsync(destroyCancellationToken);
            // 간격이 아직 안 됐으면 까닭만 남겨 둔다. Update가 때가 되면 시작한다 (나중 까닭이 앞의 것을 덮는다).
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
                // 답을 만드는 사이 새로 들린 말이나 생각할 일이 생기면 한 번 더 돈다.
                while (pending.Count > 0 || (thinkReason != null && Time.time >= nextSelfThinkAt))
                {
                    // Q-6: 바로 묻지 않고 잠깐 뜸을 들인다. 그 사이 다른 사람이 먼저 답하면 그 말도 pending에 쌓여
                    // 함께 LLM에 넘어가므로, "누가 이미 대답했네" 하고 스스로 판단할 수 있다 (R-016e).
                    await Awaitable.WaitForSecondsAsync(UnityEngine.Random.Range(replyDelayRange.x, replyDelayRange.y), cancel);

                    // 이번에 들은 말 중 가장 작은 번호를 기준으로 한다. 플레이어 말(0)이 섞였으면 다시 처음부터 센다.
                    int depth = int.MaxValue;
                    bool heardSpeech = pending.Count > 0;   // 이번 차례에 남의 말을 들었나. 마음은 겪은 일로만 바뀐다 (I-12)
                    foreach (var heard in pending)
                    {
                        shortTerm.Add((ChatMessage.User($"{heard.SpeakerName}: {heard.Text}"), heard.Time));
                        depth = Mathf.Min(depth, heard.ChainDepth);
                    }
                    pending.Clear();
                    // 혼자 생각할 까닭도 기억에 한 줄로 남긴다. 말이 아니라 속생각이라 괄호로 구분한다.
                    if (thinkReason != null)
                    {
                        shortTerm.Add((ChatMessage.User($"(속생각) {thinkReason}"), Time.time));
                        depth = Mathf.Min(depth, 0);   // 스스로 한 생각은 NPC끼리 말의 연쇄가 아니다
                        thinkReason = null;
                        nextSelfThinkAt = Time.time + minSelfThinkInterval;
                    }
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
                    string actionContext = actions != null ? actions.DescribeForPrompt() : null;
                    var messages = new List<ChatMessage> { ChatMessage.System(NpcPrompt.BuildSystem(sheet, speaker.DisplayName, room, VisibleNames(), longTerm, attentionValue, attentionLevel, feelings, actionContext, DescribeRelations(), abilities != null ? abilities.Describe() : null)) };
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
                    ActOn(ParseDecision(reply.Text), heardSpeech);
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
            public string action;    // 할 행동: 가기, 줍기, 내려놓기, 먹기, 자기, 없음 (T-6)
            public string target;    // 행동의 대상 이름 (방, 사람, 물건)
            public RelationChange[] relation;   // 달라진 마음들 (I-12). 대부분 비어 있다
        }

        [Serializable]
        class RelationChange
        {
            public string who;       // 사람 이름, 또는 "나"(이제까지의 나)
            public string kind;      // "호감" 또는 "애정"
            public string why;       // 짧은 까닭
            public int value;        // LLM이 그 캐릭터로서 느낀 만큼 (I-12 답 b)
            public string cancels;   // 풀어 주는 앞 까닭. 없으면 빈 문자열
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

        void ActOn(NpcDecision decision, bool heardSpeech)
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

            // 마음 (I-12). 값은 LLM이 골랐고, 코드는 기록하고 흐려지게 하고 상쇄만 한다.
            // 혼자 생각한 차례(속생각만)에는 마음을 바꾸지 않는다. I-12 시험에서 사람을 다시 볼 때마다
            // 묵은 감정을 말만 바꿔 새 일처럼 적어("빚을 갚아야 한다 -20") 호감이 끝없이 쌓였다.
            if (decision.relation != null && relations != null)
            {
                if (heardSpeech) foreach (var change in decision.relation) ApplyRelation(change);
                else if (decision.relation.Length > 0) Debug.Log($"[NPC {speaker.DisplayName}] 혼자 생각한 차례라 마음 변화를 버림 ({decision.relation.Length}개)", this);
            }

            // 행동 (T-6). 무엇을 할지는 LLM이 골랐고, 몸으로 해내는 것은 NpcActions가 한다.
            if (actions != null && !string.IsNullOrWhiteSpace(decision.action) && decision.action.Trim() != NpcActions.None)
                actions.Execute(decision.action, decision.target);

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

        void ApplyRelation(RelationChange change)
        {
            if (change == null || string.IsNullOrWhiteSpace(change.who) || change.value == 0) return;
            string who = change.who.Trim();
            int targetId;
            if (who == "나" || who == "이제까지의 나" || who == speaker.DisplayName) targetId = relations.selfId;
            else
            {
                // LLM은 이름만 안다(P-3). 지금 이름으로 찾고, 찾은 뒤로는 id로 적는다.
                var c = CharacterIdentity.All.Find(x => x.DisplayName == who);
                if (c == null) { Debug.Log($"[NPC {speaker.DisplayName}] 모르는 사람에 대한 마음: {who}", this); return; }
                targetId = c.Id;
            }
            bool affection = change.kind != null && change.kind.Contains("애정");
            var r = relations.Get(targetId);
            int before = affection ? r.Affection(Time.time) : r.Affinity(Time.time);
            var mod = relations.Add(targetId, affection, change.why, change.value, change.cancels, Time.time);
            if (mod == null) return;
            int after = affection ? r.Affection(Time.time) : r.Affinity(Time.time);
            string lasting = mod.lasting ? ", 오래 남음" : "";
            string cancel = mod.cancels != null ? $", '{mod.cancels.why}'와(과) 상쇄" : "";
            Debug.Log($"[NPC {speaker.DisplayName}] 마음: {who} {(affection ? "애정" : "호감")} {before} → {after} ({mod.why} {change.value:+0;-0}{lasting}{cancel})", this);
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
