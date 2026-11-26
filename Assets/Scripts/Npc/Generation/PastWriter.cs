using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Improv.Llm;
using UnityEngine;

namespace Improv.Npc.Generation
{
    /// <summary>
    /// 두 사람의 겹치는 과거를 LLM으로 쓴다 (IDEAS I-11, 사용자 결정 2026-11-23·25).
    /// 순서: 진실을 먼저 쓰고, 그다음 두 당사자의 판본을 쓴다. 둘 다 육하원칙 칸으로 된 짧은 줄 목록이다(PastEvent).
    /// 당사자는 성격·믿음·충격으로 왜곡할 수 있지만, 그런 까닭이 아니면 대체로 있는 그대로 기억한다.
    /// LLM이 실패하면 코드가 짧은 대체 글을 쓴다. 판이 시작되지 못하는 일은 없게.
    /// </summary>
    public static class PastWriter
    {
        // LLM JSON 답의 모양. JsonUtility는 필드 이름으로 채우므로 키는 영어로 둔다.
        // 한자와 일본 가나. NPC 대사 거르기(NpcDialogueBrain)와 같은 범위다.
        static readonly System.Text.RegularExpressions.Regex ForeignScript = new("[\u3040-\u30FF\u4E00-\u9FFF]");

        [Serializable]
        class Reply
        {
            public PastEvent[] truth;
            public PastVersion[] versions;
        }

        /// <param name="yearsAgo">몇 년 전 일인가. 코드가 두 사람의 나이에 맞게 정해서 준다 (LLM은 나이 계산을 자주 틀린다).</param>
        public static async Awaitable<SharedPast> WriteAsync(ILlmClient client, CastMember a, CastMember b, PastKind kind, bool heavy, int yearsAgo,
                                                            LlmRequestOptions options, CancellationToken cancel)
        {
            var past = new SharedPast { kind = kind.title, heavy = heavy, a = a, b = b };
            var messages = new List<ChatMessage> { ChatMessage.System(BuildSystem()), ChatMessage.User(BuildUser(a, b, kind, heavy, yearsAgo)) };
            // 한자·가나가 섞이면 한 번 더 쓰게 한다. 두 번째도 섞이면 그대로 쓴다 (판이 시작되지 못하는 것보다 낫다).
            LlmReply reply = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                reply = await client.SendAsync(messages, cancel, jsonMode: true, options);
                if (reply.Status == LlmStatus.Cancelled) throw new OperationCanceledException();
                if (!reply.Ok || !ForeignScript.IsMatch(reply.Text)) break;
                Debug.LogWarning($"[캐스트] {a.name}·{b.name}의 과거에 한자나 가나가 섞여 다시 씁니다.");
                if (options != null && options.Seed != null) options.Seed += 1000;   // 같은 씨드면 같은 답이 나오므로 바꾼다
            }

            // 생각(low)이 토큰을 다 써서 답이 잘리면 읽을 수 없다(I-12 시험: 생각 2800토큰, 모델 문맥은 4096).
            // 그때는 생각을 끄고 한 번 더 쓴다. 생각을 끄면 빠르지만 주어가 덜 정확하다.
            List<PastEvent> truth = null;
            PastVersion va = null, vb = null;   // C처럼 미리 선언. && 뒤가 안 돌면 out이 채워지지 않아서
            bool parsed = reply.Ok && TryParse(reply.Text, a, b, out truth, out va, out vb);
            if (!parsed && reply.Status != LlmStatus.Cancelled && options != null && options.ReasoningEffort != "none")
            {
                Debug.LogWarning($"[캐스트] {a.name}·{b.name}의 과거를 읽지 못해 생각을 끄고 다시 씁니다 ({reply.Status}).");
                var quick = new LlmRequestOptions { ReasoningEffort = "none", MaxTokens = options.MaxTokens, Seed = options.Seed, TimeoutSeconds = options.TimeoutSeconds };
                reply = await client.SendAsync(messages, cancel, jsonMode: true, quick);
                if (reply.Status == LlmStatus.Cancelled) throw new OperationCanceledException();
                parsed = reply.Ok && TryParse(reply.Text, a, b, out truth, out va, out vb);
            }

            if (parsed)
            {
                past.truth = truth;
                past.versionA = va;
                past.versionB = vb;
                past.fromLlm = true;
            }
            else
            {
                Debug.LogWarning($"[캐스트] {a.name}·{b.name}의 과거를 LLM이 쓰지 못해 짧은 대체 글을 씁니다: {reply.Status} {reply.Error}");
                string when = $"{yearsAgo}년 전";
                past.truth.Add(new PastEvent { when = when, who = $"{a.name}, {b.name}", what = $"{kind.title}(으)로 얽힘" });
                past.versionA = new PastVersion { name = a.name, feeling = "" };
                past.versionA.events.Add(new PastEvent { when = when, who = $"나, {b.name}", what = $"{kind.title}(으)로 얽힘" });
                past.versionB = new PastVersion { name = b.name, feeling = "" };
                past.versionB.events.Add(new PastEvent { when = when, who = $"나, {a.name}", what = $"{kind.title}(으)로 얽힘" });
            }
            return past;
        }

        const string Example = "{\"truth\": [{\"when\": \"8년 전\", \"where\": \"시장 골목 가게\", \"who\": \"김철수\", \"what\": \"이영희에게 가게 보증금을 빌림\", \"how\": \"\", \"why\": \"가게가 망해 가서\"}, {\"when\": \"그 뒤\", \"where\": \"\", \"who\": \"김철수\", \"what\": \"돈을 갚지 않고 이사함\", \"how\": \"연락을 끊고\", \"why\": \"갚을 돈이 없어서\"}, {\"when\": \"그 뒤\", \"where\": \"시장 골목\", \"who\": \"이영희\", \"what\": \"가게를 잃음\", \"how\": \"\", \"why\": \"보증금을 못 받아서\"}], \"versions\": [{\"name\": \"김철수\", \"people\": [], \"events\": [{\"when\": \"8년 전\", \"where\": \"시장 골목 가게\", \"who\": \"나\", \"what\": \"이영희에게 돈을 조금 빌림\", \"how\": \"\", \"why\": \"잠깐 급해서\"}, {\"when\": \"그 뒤\", \"where\": \"\", \"who\": \"나\", \"what\": \"일자리를 찾아 멀리 떠남\", \"how\": \"\", \"why\": \"갚을 돈을 벌려고\"}], \"feeling\": \"미안하지만 마주치기 싫음\", \"affinity\": 35}, {\"name\": \"이영희\", \"people\": [\"민지 = 내 딸, 그때 5살\"], \"events\": [{\"when\": \"8년 전\", \"where\": \"시장 골목 가게\", \"who\": \"김철수\", \"what\": \"내 보증금을 빌려 감\", \"how\": \"울면서 부탁해서\", \"why\": \"\"}, {\"when\": \"그 뒤\", \"where\": \"\", \"who\": \"김철수\", \"what\": \"돈을 들고 도망감\", \"how\": \"\", \"why\": \"처음부터 갚을 생각이 없었음(내 생각)\"}, {\"when\": \"그 뒤\", \"where\": \"시장 골목\", \"who\": \"나\", \"what\": \"가게를 잃고 민지와 단칸방으로 옮김\", \"how\": \"\", \"why\": \"\"}], \"feeling\": \"용서 못 함\", \"affinity\": 8}]}";

        static string BuildSystem()
        {
            var sb = new StringBuilder();
            sb.AppendLine("너는 눈보라에 갇힌 겨울 산장을 무대로 한 추리극의 작가다. 두 사람이 예전에 함께 겪은 일 하나를 쓴다.");
            // 사용자 결정(2026-11-25): 긴 서술 대신 규격이 통일된 짧은 줄(육하원칙 칸)로 쓴다. 왈도체 같아도 모양이 늘 같아야 한다.
            sb.AppendLine("글은 사람이 읽는 문장이 아니라 규격이 정해진 기록이다. 사건을 3~6줄로 쪼갠다. 한 줄에는 한 주체가 한 일 하나만 쓴다.");
            sb.AppendLine("한 줄의 칸: when(때), where(곳), who(누가, 한 명 또는 눈보라 같은 것 하나), what(무엇을, 짧은 구절, '~함/~음'으로 끝), how(어떻게, 없으면 빈칸), why(왜, 없으면 빈칸).");
            // 형식 바꾼 첫 시험: "배준호 → 나를 버리고 떠났다", "오상철 → 정태호가 유리 조각을 밟아 다쳤다"처럼 who와 what의 주어가 어긋났다.
            sb.AppendLine("what의 주어는 언제나 who다. what 안에 다른 사람이 주어로 나오면 줄을 나눈다. what에 '나는', '그가' 같은 주어를 다시 쓰지 않는다.");
            sb.AppendLine("1. truth: 실제로 일어난 일을 줄로 쓴다. truth는 기록이라 '나'를 쓰지 않고 모두 이름으로 쓴다.");
            sb.AppendLine("2. versions: 두 사람이 각자 기억하는 판본을 같은 줄 형식으로 쓴다. 둘 다 그 일의 당사자다.");
            // 사용자 결정(2026-11-25): 왜곡은 성격·성향·관점·충격 때문일 때만. 그 밖에는 호감으로 기울 뿐 대체로 그대로 기억한다.
            sb.AppendLine("   - 당사자는 성격, 믿는 것, 그때 받은 충격 때문에 기억이 왜곡될 수 있다. 자기에게 유리하게 바꾸거나, 상처 때문에 부풀리거나, 잊고 싶은 줄을 빼거나 흐린다.");
            sb.AppendLine("   - 상대에 대한 감정 때문에 why(해석)가 한쪽으로 기울 수 있다. 자기 짐작이면 why 끝에 '(내 생각)'을 붙인다.");
            sb.AppendLine("   - 하지만 성격이나 충격 때문이 아니면, 일어난 일 자체는 대체로 그대로 기억한다. 두 판본은 같은 사건으로 알아볼 수 있어야 한다.");
            // T-8 첫 시험: 29세와 48세가 "같은 고등학교"였다고 쓰고, 주어가 뒤섞였다("나는 송미란이 나를 괴롭혔다").
            // 그래서 '때'는 코드가 정해 주고, 줄마다 주체를 한 칸(who)에 따로 적게 한다.
            sb.AppendLine("   - when은 주어진 'N년 전'을 그대로 쓰고, 뒤에 일어난 일은 '그 뒤'로 쓴다. 그때 두 사람의 나이에 맞는 장소와 관계로 쓴다. 나이 차가 크면 같은 반 친구였을 수 없다.");
            sb.AppendLine("   - 판본의 who에서 자기는 '나', 상대는 이름으로 쓴다. 가해한 쪽은 자기가 한 일을 기억한다(변명하거나 축소할 수는 있다). 당한 쪽은 당한 일을 기억한다.");
            sb.AppendLine("   - people: 두 사람 말고 나오는 사람만 '이름 = 관계' 꼴로 적는다(예: '민수 = 내 동생, 그때 15살'). 없으면 빈 목록.");
            sb.AppendLine("   - feeling: 지금 상대에게 품은 마음, 짧은 구절.");
            // I-12: 게임 전부터 품은 '묵은 감정'의 기반값. 숫자는 LLM이 그 사람으로서 정한다.
            sb.AppendLine("   - affinity: 그 마음을 0~100 숫자로. 50이 보통, 0은 증오, 100은 깊은 애정. 짙은 과거면 한쪽 끝으로 치우친다.");
            // 생각을 켠 시험에서 한자가 섞였다("赔상", "面前").
            sb.AppendLine("모든 글은 한글로만 쓴다. 한자, 중국어, 일본어를 섞지 않는다. 이름은 주어진 그대로 쓴다. 산장에 오기 전의 일이다.");
            // 8b 모델은 규칙 설명보다 채워진 보기 하나를 더 잘 따른다. 산장과 상관없는 일로 보여 줘서 그대로 베끼지 않게 한다.
            sb.AppendLine("보기 (다른 두 사람의 일. 모양만 참고하고 내용은 베끼지 않는다):");
            sb.AppendLine(Example);
            sb.AppendLine("반드시 아래 JSON 한 개로만 답한다:");
            const string ev = "{\"when\": \"\", \"where\": \"\", \"who\": \"\", \"what\": \"\", \"how\": \"\", \"why\": \"\"}";
            sb.AppendLine("{\"truth\": [" + ev + ", ...], \"versions\": [{\"name\": \"첫째 사람 이름\", \"people\": [], \"events\": [" + ev + ", ...], \"feeling\": \"\", \"affinity\": 50}, " +
                          "{\"name\": \"둘째 사람 이름\", \"people\": [], \"events\": [...], \"feeling\": \"\", \"affinity\": 50}]}");
            return sb.ToString();
        }

        static string BuildUser(CastMember a, CastMember b, PastKind kind, bool heavy, int yearsAgo)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"첫째 사람: {Describe(a)}");
            sb.AppendLine($"둘째 사람: {Describe(b)}");
            sb.AppendLine($"두 사람 사이에 있었던 일의 종류: {kind.title}. {kind.hint}");
            sb.AppendLine($"언제: {yearsAgo}년 전. 그때 {a.name}은(는) {a.age - yearsAgo}살, {b.name}은(는) {b.age - yearsAgo}살이었다.");
            // GDD 3장 4번: 갈등 구조는 김전일처럼 어느 정도 극단적이어야 사건이 일어나기 쉽다.
            if (heavy)
                sb.AppendLine("이 과거는 짙다. 산장에서 다시 마주치면 한쪽이 살의를 품어도 이상하지 않을 만큼 큰 원한이나 죄책감이 남았다. 누가 어느 쪽인지는 두 사람의 배경과 성격에 맞게 정한다.");
            else
                // 첫 시험에서 "이웃"이 화재 사건이 됐다. 가벼운 인연에는 큰 일을 쓰지 않게 막는다.
                sb.AppendLine("이 과거는 가벼운 인연이다. 큰 갈등은 없고 서로 얼굴과 이름을 알아보는 정도다. 작은 호감이나 껄끄러움은 있어도 된다. 사고, 화재, 죽음, 범죄 같은 큰 일은 쓰지 않는다. 일상의 작은 일 하나면 된다.");
            return sb.ToString();
        }

        static string Describe(CastMember m)
        {
            // 프리셋 NPC는 표의 배경 대신 손으로 쓴 소개 글만 있다.
            if (!string.IsNullOrWhiteSpace(m.profileOverride)) return $"{m.name}. {m.profileOverride}";
            var traits = new List<string>();
            foreach (var t in m.traits) traits.Add(t.name);
            var sb = new StringBuilder($"{m.name}, {m.age}세 {(m.female ? "여자" : "남자")}. 직업: {m.adulthood?.title}({m.adulthood?.story}) 어린 시절: {m.childhood?.title}({m.childhood?.story}) " +
                                       $"성격: {string.Join(", ", traits)}. 믿는 것: {string.Join(" / ", m.beliefs)}");
            // 메이킹에서 플레이어가 쓴 설정. 과거는 이것과 어긋나면 안 된다 (사용자 2026-11-26: 큰 줄기는 플레이어 설정에 맞춘다).
            if (!string.IsNullOrWhiteSpace(m.family)) sb.Append($". 집에서: {m.family}");
            if (m.personal.Count > 0)
            {
                sb.Append($". {m.name}의 지난 일(이것과 어긋나게 쓰지 않는다):");
                // 개인 서사의 주어는 "나"지만, 여기서는 두 사람을 함께 적으므로 이름으로 바꿔 찍는다.
                foreach (var e in m.personal)
                    sb.Append(" / ").Append(PastFormat.Line(new PastEvent { when = e.when, where = e.where, who = m.name, what = e.what, how = e.how, why = e.why }));
            }
            return sb.ToString();
        }

        static bool TryParse(string text, CastMember a, CastMember b, out List<PastEvent> truth, out PastVersion va, out PastVersion vb)
        {
            truth = null; va = null; vb = null;
            Reply parsed;
            try { parsed = JsonUtility.FromJson<Reply>(text); }
            catch (Exception) { return false; }
            if (parsed?.truth == null || parsed.versions == null || parsed.versions.Length < 2) return false;
            truth = Usable(parsed.truth);
            if (truth.Count == 0) return false;

            // 이름으로 짝을 찾는다. LLM이 이름을 조금 다르게 쓰면 순서대로 붙인다.
            foreach (var v in parsed.versions)
            {
                if (v == null || v.events == null || Usable(v.events).Count == 0) continue;
                if (va == null && v.name != null && v.name.Contains(a.name)) va = v;
                else if (vb == null && v.name != null && v.name.Contains(b.name)) vb = v;
            }
            va ??= parsed.versions[0];
            vb ??= parsed.versions[1] != va ? parsed.versions[1] : parsed.versions[0];
            if (va == null || vb == null || va == vb) return false;
            foreach (var v in new[] { va, vb })
            {
                v.events = Usable(v.events);
                v.people ??= new List<string>();
                // people은 두 당사자 말고 나오는 사람만. 첫 시험에서 상대를 "오상철 = 동료"로 다시 적었다.
                v.people.RemoveAll(x => string.IsNullOrWhiteSpace(x) || x.TrimStart().StartsWith(a.name) || x.TrimStart().StartsWith(b.name));
            }
            if (va.events.Count == 0 || vb.events.Count == 0) return false;
            va.name = a.name;
            vb.name = b.name;
            return true;
        }

        // what(무엇을)이 빈 줄은 버린다. 주체나 한 일이 없으면 규격 줄이 되지 않는다.
        static List<PastEvent> Usable(IEnumerable<PastEvent> events)
        {
            var list = new List<PastEvent>();
            if (events == null) return list;
            foreach (var e in events)
                if (e != null && !string.IsNullOrWhiteSpace(e.what) && !string.IsNullOrWhiteSpace(e.who)) list.Add(e);
            return list;
        }
    }
}
