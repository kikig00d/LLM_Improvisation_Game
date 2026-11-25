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
    /// 순서: 진실(육하원칙)을 먼저 쓰고, 그다음 두 당사자의 판본을 쓴다.
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
            public PastTruth truth;
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

            if (reply.Ok && TryParse(reply.Text, a, b, out var truth, out var va, out var vb))
            {
                past.truth = truth;
                past.versionA = va;
                past.versionB = vb;
                past.fromLlm = true;
            }
            else
            {
                Debug.LogWarning($"[캐스트] {a.name}·{b.name}의 과거를 LLM이 쓰지 못해 짧은 대체 글을 씁니다: {reply.Status} {reply.Error}");
                past.truth = new PastTruth { who = $"{a.name}, {b.name}", when = $"{yearsAgo}년 전", where = "", what = kind.title, how = "", why = "" };
                past.versionA = new PastVersion { name = a.name, memory = $"{b.name}와(과)는 {kind.title}(으)로 얽힌 사이다.", feeling = "" };
                past.versionB = new PastVersion { name = b.name, memory = $"{a.name}와(과)는 {kind.title}(으)로 얽힌 사이다.", feeling = "" };
            }
            return past;
        }

        static string BuildSystem()
        {
            var sb = new StringBuilder();
            sb.AppendLine("너는 눈보라에 갇힌 겨울 산장을 무대로 한 추리극의 작가다. 두 사람이 예전에 함께 겪은 일 하나를 쓴다.");
            sb.AppendLine("1. 먼저 실제로 일어난 진실을 육하원칙(누가, 언제, 어디서, 무엇을, 어떻게, 왜)으로 쓴다. 각 칸은 한 문장.");
            sb.AppendLine("2. 그다음 두 사람이 각자 기억하는 판본을 쓴다. 둘 다 그 일의 당사자다.");
            // 사용자 결정(2026-11-25): 왜곡은 성격·성향·관점·충격 때문일 때만. 그 밖에는 호감으로 기울 뿐 대체로 그대로 기억한다.
            sb.AppendLine("   - 당사자는 성격, 믿는 것, 그때 받은 충격 때문에 기억이 왜곡될 수 있다. 자기에게 유리하게 바꾸거나, 상처 때문에 부풀리거나, 잊고 싶은 부분을 흐린다.");
            sb.AppendLine("   - 상대에 대한 감정 때문에 해석이 한쪽으로 기울 수 있다.");
            sb.AppendLine("   - 하지만 성격이나 충격 때문이 아니면, 일어난 일 자체는 대체로 그대로 기억한다. 두 판본은 같은 사건으로 알아볼 수 있어야 한다.");
            // T-8 첫 시험: 29세와 48세가 "같은 고등학교"였다고 쓰고, 주어가 뒤섞였다("나는 송미란이 나를 괴롭혔다").
            // 그래서 '언제'는 코드가 정해 주고, LLM은 그때 나이에 맞는 장소와 관계만 고른다.
            sb.AppendLine("   - '언제'는 주어진 그대로 쓴다. 그때 두 사람의 나이에 맞는 장소와 관계로 쓴다. 나이 차가 크면 같은 반 친구였을 수 없다.");
            sb.AppendLine("3. memory는 그 사람의 1인칭 기억, 2~3문장. 자기는 '나', 상대는 이름으로 부르고, 누가 누구에게 무엇을 했는지 주어를 분명히 쓴다.");
            sb.AppendLine("   - 가해한 쪽은 자기가 한 일을 기억한다(변명하거나 축소할 수는 있다). 당한 쪽은 당한 일을 기억한다.");
            sb.AppendLine("4. feeling은 지금 상대에게 품은 마음, 짧은 한 구절.");
            // 생각을 켠 시험에서 한자가 섞였다("赔상", "面前").
            sb.AppendLine("모든 글은 한글로만 쓴다. 한자, 중국어, 일본어를 섞지 않는다. 이름은 주어진 그대로 쓴다. 산장에 오기 전의 일이다.");
            sb.AppendLine("반드시 아래 JSON 한 개로만 답한다:");
            sb.AppendLine("{\"truth\": {\"who\": \"\", \"when\": \"\", \"where\": \"\", \"what\": \"\", \"how\": \"\", \"why\": \"\"}, \"versions\": [{\"name\": \"첫째 사람 이름\", \"memory\": \"\", \"feeling\": \"\"}, {\"name\": \"둘째 사람 이름\", \"memory\": \"\", \"feeling\": \"\"}]}");
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
            var traits = new List<string>();
            foreach (var t in m.traits) traits.Add(t.name);
            return $"{m.name}, {m.age}세 {(m.female ? "여자" : "남자")}. 직업: {m.adulthood.title}({m.adulthood.story}) 어린 시절: {m.childhood.title}({m.childhood.story}) " +
                   $"성격: {string.Join(", ", traits)}. 믿는 것: {string.Join(" / ", m.beliefs)}";
        }

        static bool TryParse(string text, CastMember a, CastMember b, out PastTruth truth, out PastVersion va, out PastVersion vb)
        {
            truth = null; va = null; vb = null;
            Reply parsed;
            try { parsed = JsonUtility.FromJson<Reply>(text); }
            catch (Exception) { return false; }
            if (parsed?.truth == null || parsed.versions == null || parsed.versions.Length < 2) return false;
            if (string.IsNullOrWhiteSpace(parsed.truth.what)) return false;

            // 이름으로 짝을 찾는다. LLM이 이름을 조금 다르게 쓰면 순서대로 붙인다.
            foreach (var v in parsed.versions)
            {
                if (v == null || string.IsNullOrWhiteSpace(v.memory)) continue;
                if (va == null && v.name != null && v.name.Contains(a.name)) va = v;
                else if (vb == null && v.name != null && v.name.Contains(b.name)) vb = v;
            }
            va ??= parsed.versions[0];
            vb ??= parsed.versions[1] != va ? parsed.versions[1] : parsed.versions[0];
            if (va == vb || string.IsNullOrWhiteSpace(va.memory) || string.IsNullOrWhiteSpace(vb.memory)) return false;
            va.name = a.name;
            vb.name = b.name;
            truth = parsed.truth;
            return true;
        }
    }
}
