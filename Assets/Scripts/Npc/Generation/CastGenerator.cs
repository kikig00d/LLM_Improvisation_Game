using System;
using System.Collections.Generic;
using System.Text;
using Improv.Characters;
using Improv.Llm;
using Improv.World;
using UnityEngine;

namespace Improv.Npc.Generation
{
    /// <summary>
    /// 판이 시작할 때 NPC 캐릭터 시트를 새로 만든다 (계획서 T-8, GDD 7장 Generating, IDEAS I-11).
    /// 1. 코드가 표(CharacterTables)에서 겹치지 않게 무작위로 고른다: 이름, 나이, 유년기·성년기 배경, 성격 특성, 믿는 것.
    /// 2. 사람들 사이에 겹치는 과거를 정한다: 모두 하나 이상, 그중 하나는 짙게(김전일식 갈등).
    /// 3. 겹치는 과거만 LLM이 쓴다: 진실 먼저, 그다음 각자의 판본 (PastWriter).
    /// 4. 시트로 묶어 각 NPC 두뇌에 끼운다. 그동안 시간은 멈추고 두뇌는 꺼 둔다.
    /// 같은 씨드면 1~2가 같다. 3의 글은 LLM 씨드로 최대한 같게 하지만 완전히 같다는 보장은 없다.
    /// 이 컴포넌트를 끄면 NPC는 씬에 연결된 손으로 쓴 시트를 그대로 쓴다.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class CastGenerator : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [SerializeField] CharacterTables tables;
        [SerializeField] LlmSettings llm;
        [SerializeField] List<NpcDialogueBrain> npcs = new();
        [Tooltip("0이면 매 판 새 씨드. 숫자를 넣으면 같은 사람들이 나온다 (Console에 쓴 씨드가 찍힌다)")]
        [SerializeField] int seed = 0;
        [Tooltip("과거를 쓸 때 LLM의 생각 단계. none이면 빠르지만 글이 엉성하고, low면 느리지만 공들인다")]
        [SerializeField] string reasoningEffort = "low";   // 시험: none은 약 20초지만 주어가 꼬이고 조건을 어겼다. low는 약 47초(2건)로 성격대로 갈린 판본을 썼다
        [SerializeField] int maxTokens = 3000;   // 생각을 켜면 생각 글자도 여기에 들어가서 넉넉해야 한다

        // =====================================================================
        // #2. 상태
        // =====================================================================

        public static CastGenerator Instance { get; private set; }
        public bool IsGenerating { get; private set; }
        public string Status { get; private set; } = "";
        public int UsedSeed { get; private set; }
        public IReadOnlyList<CastMember> Cast => cast;
        public IReadOnlyList<SharedPast> Pasts => pasts;
        public IReadOnlyList<NpcCharacterSheet> Sheets => sheets;

        readonly List<CastMember> cast = new();
        readonly List<SharedPast> pasts = new();
        readonly List<NpcCharacterSheet> sheets = new();

        // 과거 하나를 쓸 두 사람과 종류. (C의 작은 struct)
        struct Link
        {
            public int a, b;
            public bool heavy;
            public PastKind kind;
            public int yearsAgo;    // 몇 년 전 일인가. 어린 쪽이 그때 적어도 minAgeAtPast살이 되게 코드가 정한다
        }

        [Tooltip("겹치는 과거가 일어난 때, 두 사람 중 어린 쪽의 나이가 적어도 이만큼은 되게 한다")]
        [SerializeField] int minAgeAtPast = 10;

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Awake()
        {
            Instance = this;
            if (tables == null) { Debug.LogError("[CastGenerator] tables(CharacterTables)가 비어 있습니다.", this); enabled = false; return; }
            if (llm == null) { Debug.LogError("[CastGenerator] llm(LlmSettings)이 비어 있습니다.", this); enabled = false; return; }
            npcs.RemoveAll(n => n == null);
            // 시트가 나오기 전에 NPC가 말을 듣고 답하지 않게 두뇌를 꺼 둔다.
            foreach (var npc in npcs) npc.enabled = false;
            IsGenerating = true;
            Status = "산장에 사람들이 모이는 중...";
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // async Awaitable Start: 유니티가 Start를 기다릴 수 있는 비동기 함수로 부른다. LLM 답을 기다리는 동안 게임이 멈추지 않는다.
        async Awaitable Start()
        {
            if (!enabled) return;
            int resumeStep = PauseClock();
            try
            {
                UsedSeed = seed != 0 ? seed : Math.Max(1, Environment.TickCount & 0x7fffffff);
                var rng = new System.Random(UsedSeed);   // 같은 씨드면 같은 순서로 같은 값이 나온다
                Debug.Log($"[캐스트] 씨드 {UsedSeed}. 같은 사람들을 다시 보려면 CastGenerator의 seed에 이 값을 넣는다.", this);

                BuildMembers(rng);
                var links = PickLinks(rng);

                var client = llm.CreateClient();
                for (int i = 0; i < links.Count; i++)
                {
                    Status = $"사람들의 과거를 쓰는 중... ({i + 1}/{links.Count})";
                    var options = new LlmRequestOptions { ReasoningEffort = reasoningEffort, MaxTokens = maxTokens, Seed = UsedSeed + i };
                    var link = links[i];
                    pasts.Add(await PastWriter.WriteAsync(client, cast[link.a], cast[link.b], link.kind, link.heavy, link.yearsAgo, options, destroyCancellationToken));
                }

                foreach (var m in cast)
                {
                    var sheet = BuildSheet(m);
                    sheets.Add(sheet);
                    m.brain.ApplySheet(sheet);
                }
                LogCast();
            }
            catch (OperationCanceledException) { }   // Play를 멈춰 취소됨
            finally
            {
                IsGenerating = false;
                Status = "";
                foreach (var npc in npcs) if (npc != null) npc.enabled = true;   // 유니티의 null 비교는 "파괴됨"도 잡는다
                ResumeClock(resumeStep);
            }
        }

        // =====================================================================
        // #4. 코드가 고르는 부분 (같은 씨드 → 같은 결과)
        // =====================================================================

        void BuildMembers(System.Random rng)
        {
            // 플레이어처럼 이 생성기가 만들지 않는 사람의 이름과는 겹치지 않게 한다.
            var usedNames = new HashSet<string>();
            foreach (var who in CharacterIdentity.All)
                if (!npcs.Exists(n => n.gameObject == who.gameObject)) usedNames.Add(who.DisplayName);
            var usedChildhoods = new HashSet<Background>();
            var usedAdulthoods = new HashSet<Background>();

            foreach (var brain in npcs)
            {
                var m = new CastMember { brain = brain };
                var identity = brain.GetComponent<CharacterIdentity>();
                m.id = identity != null ? identity.Id : -1;

                var entry = Pick(tables.names, rng, n => !usedNames.Contains(n.name));
                m.name = entry.name;
                m.female = entry.female;
                usedNames.Add(m.name);
                m.age = rng.Next(tables.ageRange.x, tables.ageRange.y + 1);

                // 배경은 한 판 안에서 되도록 겹치지 않게. unique(산장 관리인 등)는 절대 두 번 나오지 않는다.
                m.childhood = Pick(tables.childhoods, rng, c => !usedChildhoods.Contains(c));
                usedChildhoods.Add(m.childhood);
                m.adulthood = Pick(tables.adulthoods, rng, c => !usedAdulthoods.Contains(c), c => !(c.unique && usedAdulthoods.Contains(c)));
                usedAdulthoods.Add(m.adulthood);
                m.arrival = !string.IsNullOrWhiteSpace(m.adulthood.fixedArrival) ? m.adulthood.fixedArrival : Pick(tables.arrivalReasons, rng, _ => true);

                // 특성은 서로 부딪히지 않게 (수다쟁이 + 과묵함 X).
                foreach (var t in Shuffled(tables.traits, rng))
                {
                    if (m.traits.Count >= tables.traitsPerCharacter) break;
                    if (m.traits.Exists(have => have.conflicts.Contains(t.name) || t.conflicts.Contains(have.name))) continue;
                    m.traits.Add(t);
                }
                foreach (var belief in Shuffled(tables.beliefs, rng))
                {
                    if (m.beliefs.Count >= tables.beliefsPerCharacter) break;
                    m.beliefs.Add(belief);
                }

                // 평소 집중도(T-5)는 특성에서 나온다. 예민하고 의심 많으면 높고, 무심하면 낮다.
                float attention = 0.3f;
                foreach (var t in m.traits) attention += t.attention;
                m.attention = Mathf.Clamp(attention, 0.05f, 0.95f);
                cast.Add(m);
            }
        }

        // 겹치는 과거: 모두 하나 이상, 그중 하나는 짙게 (사용자 결정 2026-11-25).
        // 순서를 섞어 앞의 두 사람이 짙은 과거를 갖고, 나머지는 앞사람 중 하나와 가벼운 인연을 갖는다.
        List<Link> PickLinks(System.Random rng)
        {
            var links = new List<Link>();
            if (cast.Count < 2) return links;
            var order = new List<int>();
            for (int i = 0; i < cast.Count; i++) order.Add(i);
            order = Shuffled(order, rng);

            int a0 = order[0], b0 = order[1];
            var heavy = Pick(tables.heavyPasts, rng, p => Fits(p, a0, b0));
            links.Add(new Link { a = a0, b = b0, heavy = true, kind = heavy, yearsAgo = YearsAgo(heavy, a0, b0, rng) });
            var usedLight = new HashSet<PastKind>();
            for (int k = 2; k < order.Count; k++)
            {
                int a = order[k], b = order[rng.Next(k)];
                // 나이가 안 맞는 종류(나이 차 큰 두 사람의 "동창" 등)는 고르지 않는다. 첫 시험에서 35세와 43세가 "고등학교 교실"에 있었다.
                var kind = Pick(tables.lightPasts, rng, p => !usedLight.Contains(p) && Fits(p, a, b), p => Fits(p, a, b));
                usedLight.Add(kind);
                links.Add(new Link { a = a, b = b, heavy = false, kind = kind, yearsAgo = YearsAgo(kind, a, b, rng) });
            }
            return links;
        }

        bool Fits(PastKind kind, int a, int b) => kind.maxAgeGap <= 0 || Mathf.Abs(cast[a].age - cast[b].age) <= kind.maxAgeGap;

        // 언제 일어났나. LLM이 나이 계산을 자주 틀려서 코드가 정한다.
        // 보통: 1년 전부터 어린 쪽이 minAgeAtPast살이던 때까지. youth: 둘 다 10~18살이던 때.
        int YearsAgo(PastKind kind, int a, int b, System.Random rng)
        {
            int younger = Mathf.Min(cast[a].age, cast[b].age), older = Mathf.Max(cast[a].age, cast[b].age);
            if (kind.youth)
            {
                int from = Mathf.Max(1, older - 18), to = younger - 10;   // 이 범위면 둘 다 10~18살
                if (from <= to) return rng.Next(from, to + 1);
            }
            return rng.Next(1, Mathf.Max(2, younger - minAgeAtPast + 1));
        }

        // =====================================================================
        // #5. 시트로 묶기
        // =====================================================================

        NpcCharacterSheet BuildSheet(CastMember m)
        {
            var sheet = ScriptableObject.CreateInstance<NpcCharacterSheet>();
            sheet.name = $"Generated_{m.name}";
            sheet.displayName = m.name;

            var traitText = new List<string>();
            foreach (var t in m.traits) traitText.Add($"{t.name}({t.description})");
            sheet.publicProfile = $"{m.age}세 {(m.female ? "여자" : "남자")}. 직업은 {m.adulthood.title}. {m.adulthood.story} " +
                                  $"어린 시절은 {m.childhood.title}. {m.childhood.story} 산장에 온 까닭: {m.arrival} 성격: {string.Join(", ", traitText)}.";

            // 말투: 왈도체의 기본(짧고 투박) 위에 배경의 말투 힌트를 얹는다.
            var speech = new StringBuilder("아주 짧고 투박한 문장. 쉬운 단어만 쓴다. 한 번에 1~2문장.");
            foreach (var bg in new[] { m.childhood, m.adulthood })
                if (!string.IsNullOrWhiteSpace(bg.speechHint)) speech.Append(' ').Append(bg.speechHint);
            sheet.speechStyle = speech.ToString();

            sheet.baselineAttention = m.attention;
            sheet.beliefs.AddRange(m.beliefs);
            foreach (var past in pasts)
            {
                var mine = past.VersionOf(m);
                if (mine == null) continue;
                var other = past.Other(m);
                string feeling = string.IsNullOrWhiteSpace(mine.feeling) ? "" : $" 지금 {other.name}에게 드는 마음: {mine.feeling}";
                sheet.pasts.Add($"{other.name}와(과)의 일({past.truth.when}): {mine.memory}{feeling}");
            }
            return sheet;
        }

        void LogCast()
        {
            var sb = new StringBuilder($"[캐스트] 씨드 {UsedSeed}로 만든 사람들\n");
            foreach (var m in cast)
            {
                var traits = new List<string>();
                foreach (var t in m.traits) traits.Add(t.name);
                sb.AppendLine($"- {m.name}(id {m.id}) {m.age}세 {(m.female ? "여" : "남")}, {m.childhood.title} → {m.adulthood.title}, 성격 {string.Join("·", traits)}, 집중도 {m.attention:0.00}, 믿음: {string.Join(" / ", m.beliefs)}");
            }
            foreach (var p in pasts)
            {
                sb.AppendLine($"* {p.a.name} - {p.b.name}: {p.kind}{(p.heavy ? " (짙음)" : "")}{(p.fromLlm ? "" : " [대체 글]")}");
                sb.AppendLine($"  진실: {p.truth}");
                sb.AppendLine($"  {p.a.name}의 기억: {p.versionA.memory} / 마음: {p.versionA.feeling}");
                sb.AppendLine($"  {p.b.name}의 기억: {p.versionB.memory} / 마음: {p.versionB.feeling}");
            }
            Debug.Log(sb.ToString(), this);
        }

        // =====================================================================
        // #6. 도우미
        // =====================================================================

        // 조건에 맞는 것 중 무작위 하나. 맞는 것이 없으면 둘째 조건(필수 조건)만 보고, 그래도 없으면 아무거나.
        static T Pick<T>(List<T> list, System.Random rng, Func<T, bool> prefer, Func<T, bool> require = null)
        {
            var candidates = list.FindAll(x => prefer(x) && (require == null || require(x)));
            if (candidates.Count == 0 && require != null) candidates = list.FindAll(x => require(x));
            if (candidates.Count == 0) candidates = list;
            return candidates[rng.Next(candidates.Count)];
        }

        // 피셔-예이츠 섞기. 원본은 그대로 두고 섞인 사본을 돌려준다.
        static List<T> Shuffled<T>(List<T> list, System.Random rng)
        {
            var copy = new List<T>(list);
            for (int i = copy.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (copy[i], copy[j]) = (copy[j], copy[i]);
            }
            return copy;
        }

        // 만드는 동안 시간을 멈춘다 (GDD 7장 Generating). 원래 속도 단계를 돌려준다.
        static int PauseClock()
        {
            var clock = GameClock.Instance;
            if (clock == null) return -1;
            int step = clock.Step;
            clock.SetStep(0);
            return step;
        }

        static void ResumeClock(int step)
        {
            var clock = GameClock.Instance;
            if (clock != null && step > 0) clock.SetStep(step);
        }

#if UNITY_EDITOR
        // Play 중 Inspector의 ⋮ 메뉴에서 고른다. 마음에 드는 판을 에셋으로 남겨 두고, 생성기를 끈 채 NPC에 끼워 다시 쓸 수 있다.
        [ContextMenu("만든 시트를 에셋으로 저장")]
        void SaveSheets()
        {
            if (sheets.Count == 0) { Debug.LogWarning("[캐스트] 아직 만든 시트가 없습니다. Play 중 생성이 끝난 뒤 고르세요.", this); return; }
            const string folder = "Assets/Data/Characters/Generated";
            if (!UnityEditor.AssetDatabase.IsValidFolder(folder)) UnityEditor.AssetDatabase.CreateFolder("Assets/Data/Characters", "Generated");
            foreach (var s in sheets)
            {
                var copy = Instantiate(s);
                string path = UnityEditor.AssetDatabase.GenerateUniqueAssetPath($"{folder}/{UsedSeed}_{s.displayName}.asset");
                UnityEditor.AssetDatabase.CreateAsset(copy, path);
            }
            UnityEditor.AssetDatabase.SaveAssets();
            Debug.Log($"[캐스트] 시트 {sheets.Count}개를 {folder}에 저장했습니다.", this);
        }
#endif
    }
}
