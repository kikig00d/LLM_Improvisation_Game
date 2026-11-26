using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Improv.Llm;
using UnityEngine;

namespace Improv.Npc.Generation
{
    /// <summary>
    /// "LLM이 빈칸 채워 줘" (GDD R-029b). 쓰다 만 답안지(PersonaDraft)를 JSON 그대로 보내고, 빈칸만 채운 답을 받는다.
    /// 이미 쓴 칸은 절대 바꾸지 않는다. 보기 목록에 없는 말, 너무 긴 말은 코드가 버린다 (설정 과잉 방지).
    /// </summary>
    public static class PersonaFiller
    {
        static readonly Regex HangulOnly = new(@"^[가-힣 ]+$");

        /// <summary>빈칸을 채운다. 하나라도 채웠으면 true. 실패하면 답안지는 그대로다.</summary>
        public static async Awaitable<bool> FillAsync(ILlmClient client, PersonaDraft draft, CharacterTables tables, MakingTemplates templates,
                                                      ICollection<string> takenNames, LlmRequestOptions options, CancellationToken cancel)
        {
            var messages = new List<ChatMessage>
            {
                ChatMessage.System(BuildSystem(tables, templates)),
                ChatMessage.User("채울 답안지:\n" + JsonUtility.ToJson(draft)),
            };
            var reply = await client.SendAsync(messages, cancel, jsonMode: true, options);
            if (reply.Status == LlmStatus.Cancelled) throw new OperationCanceledException();
            if (!reply.Ok) { Debug.LogWarning($"[메이킹] LLM이 답하지 못했습니다: {reply.Status} {reply.Error}"); return false; }

            PersonaDraft got;
            try { got = JsonUtility.FromJson<PersonaDraft>(reply.Text); }
            catch (Exception) { Debug.LogWarning($"[메이킹] LLM 답을 읽지 못했습니다: {reply.Text}"); return false; }
            if (got == null) return false;
            got.EnsureShape(tables, templates);
            int filled = Merge(draft, got, tables, templates, takenNames);
            Debug.Log($"[메이킹] LLM이 빈칸 {filled}개를 채웠습니다 ({reply.ElapsedSeconds:0.0}초).");
            return filled > 0;
        }

        static string BuildSystem(CharacterTables tables, MakingTemplates templates)
        {
            var sb = new StringBuilder();
            sb.AppendLine("너는 눈보라에 갇힌 겨울 산장이 무대인 추리극 게임에서, 플레이어의 캐릭터 답안지를 채우는 도우미다.");
            sb.AppendLine("답안지(JSON)의 빈칸만 채운다. 빈칸은 \"\", 0, 또는 t가 -1인 것이다. 이미 채운 칸은 글자 하나 바꾸지 않고 그대로 돌려준다.");
            sb.AppendLine("빈칸은 이미 채운 칸과 어울리게 채운다. 나이와 직업, 어린 시절, 집에서의 자리, 지난 일이 서로 맞아야 한다.");
            // 사용자 2026-11-26: 캐릭터 챗봇처럼 설정이 부풀지 않게 규격화한다.
            sb.AppendLine("설정을 부풀리지 않는다. 평범한 사람의 작은 일로 쓴다. 비밀 조직, 초능력, 살인 같은 큰 일은 쓰지 않는다.");
            sb.AppendLine("목록 칸은 아래 보기에서 글자 그대로 하나 고른다:");
            sb.AppendLine($"- gender: {PersonaDraft.Male} / {PersonaDraft.Female}");
            sb.AppendLine($"- age: {tables.ageRange.x}~{tables.ageRange.y} 숫자");
            sb.AppendLine($"- name: 한글 이름, {templates.nameMaxLength}자 이하");
            sb.AppendLine("- childhood: " + Join(tables.childhoods.ConvertAll(c => c.title)));
            sb.AppendLine("- adulthood(직업): " + Join(tables.adulthoods.ConvertAll(c => c.title)));
            sb.AppendLine("- family(집에서의 자리, {남자말|여자말}은 그대로 둔다): " + Join(templates.familyPlaces));
            sb.AppendLine("- arrival: " + Join(tables.arrivalReasons));
            sb.AppendLine($"- traits({tables.traitsPerCharacter}개, 서로 다르게): " + Join(tables.traits.ConvertAll(t => t.name)));
            sb.AppendLine($"- beliefs({tables.beliefsPerCharacter}개): " + Join(tables.beliefs));
            sb.AppendLine("personal은 그 사람의 지난 일 줄이다. 줄마다 주어는 그 사람 자신이다.");
            sb.AppendLine("- when: " + Join(templates.times));
            sb.AppendLine($"- where: 장소, {templates.placeMaxLength}자 이하");
            sb.AppendLine($"- what, how, why: 아래 틀 중 하나의 번호를 t에, 틀의 빈칸 {{…}}에 들어갈 말을 순서대로 b에 넣는다. b의 말은 하나에 {templates.blankMaxLength}자 이하. how와 why는 비워도 된다(t: -1).");
            AppendTemplates(sb, "what", templates.whatTemplates);
            AppendTemplates(sb, "how", templates.howTemplates);
            AppendTemplates(sb, "why", templates.whyTemplates);
            sb.AppendLine("모든 글은 한글로만 쓴다. bond는 바꾸지 않는다.");
            if (!string.IsNullOrWhiteSpace(templates.llmExample))
            {
                // 8b 모델은 규칙보다 채워진 보기 하나를 더 잘 따른다 (PastWriter와 같은 까닭). 모양만 보게 한다.
                sb.AppendLine("다 채운 보기 (다른 사람이다. 모양만 참고하고 내용은 베끼지 않는다):");
                sb.AppendLine(templates.llmExample);
            }
            sb.AppendLine("받은 답안지와 같은 모양의 JSON 한 개로만 답한다.");
            return sb.ToString();
        }

        static string Join(List<string> list) => string.Join(" / ", list.ConvertAll(s => s.Replace("\n", " ")));

        static void AppendTemplates(StringBuilder sb, string key, List<string> list)
        {
            sb.Append($"  {key} 틀:");
            for (int i = 0; i < list.Count; i++) sb.Append($" {i}) {list[i]}");
            sb.AppendLine();
        }

        // =====================================================================
        // 빈칸에만 옮겨 담기. 보기에 없는 말, 긴 말, 한글이 아닌 말은 버린다.
        // =====================================================================

        static int Merge(PersonaDraft d, PersonaDraft got, CharacterTables tables, MakingTemplates t, ICollection<string> takenNames)
        {
            int n = 0;
            if (Empty(d.name) && !Empty(got.name))
            {
                string name = got.name.Trim();
                if (name.Length <= t.nameMaxLength && HangulOnly.IsMatch(name) && !takenNames.Contains(name)) { d.name = name; n++; }
            }
            if (Empty(d.gender) && (got.gender == PersonaDraft.Male || got.gender == PersonaDraft.Female)) { d.gender = got.gender; n++; }
            if (d.age <= 0 && got.age >= tables.ageRange.x && got.age <= tables.ageRange.y) { d.age = got.age; n++; }
            n += Pick(ref d.childhood, got.childhood, tables.childhoods.ConvertAll(c => c.title));
            n += Pick(ref d.adulthood, got.adulthood, tables.adulthoods.ConvertAll(c => c.title));
            n += Pick(ref d.arrival, got.arrival, tables.arrivalReasons);
            if (Empty(d.family) && !Empty(got.family))
            {
                // LLM이 {아버지|어머니}를 풀어서 쓸 수도 있어 둘 다 맞춰 본다.
                string match = t.familyPlaces.Find(f => f == got.family.Trim() || MakingTemplates.Gendered(f, false) == got.family.Trim() || MakingTemplates.Gendered(f, true) == got.family.Trim());
                if (match != null) { d.family = match; n++; }
            }
            for (int i = 0; i < d.traits.Length; i++)
            {
                if (!Empty(d.traits[i])) continue;
                foreach (string cand in got.traits)
                {
                    var trait = tables.traits.Find(x => x.name == cand?.Trim());
                    if (trait == null || Array.IndexOf(d.traits, trait.name) >= 0) continue;
                    if (Array.Exists(d.traits, have => trait.conflicts.Contains(have))) continue;   // 수다쟁이 + 과묵함 X
                    d.traits[i] = trait.name; n++;
                    break;
                }
            }
            for (int i = 0; i < d.beliefs.Length; i++)
            {
                if (!Empty(d.beliefs[i])) continue;
                foreach (string cand in got.beliefs)
                {
                    string b = tables.beliefs.Find(x => x == cand?.Trim());
                    if (b == null || Array.IndexOf(d.beliefs, b) >= 0) continue;
                    d.beliefs[i] = b; n++;
                    break;
                }
            }
            for (int i = 0; i < d.personal.Length && i < got.personal.Length; i++)
            {
                var line = d.personal[i];
                var g = got.personal[i];
                if (g == null) continue;
                n += Pick(ref line.when, g.when, t.times);
                if (Empty(line.where) && !Empty(g.where) && g.where.Trim().Length <= t.placeMaxLength && HangulOnly.IsMatch(g.where.Trim())) { line.where = g.where.Trim(); n++; }
                n += PickTemplate(line.what, g.what, t.whatTemplates, t.blankMaxLength);
                n += PickTemplate(line.how, g.how, t.howTemplates, t.blankMaxLength);
                n += PickTemplate(line.why, g.why, t.whyTemplates, t.blankMaxLength);
            }
            return n;
        }

        static bool Empty(string s) => string.IsNullOrWhiteSpace(s);

        static int Pick(ref string field, string value, List<string> options)
        {
            if (!Empty(field) || Empty(value)) return 0;
            string match = options.Find(o => o.Trim() == value.Trim());
            if (match == null) return 0;
            field = match;
            return 1;
        }

        // 틀을 이미 골랐으면 빈칸만, 안 골랐으면 틀째로 받는다.
        static int PickTemplate(TemplatePick mine, TemplatePick got, List<string> templates, int maxLength)
        {
            if (got == null || got.t < 0 || got.t >= templates.Count) return 0;
            if (mine.IsEmpty) mine.Choose(got.t, templates);
            else if (mine.t != got.t) return 0;   // 플레이어가 고른 틀과 다른 틀로 답했으면 버린다
            int n = 0;
            for (int i = 0; i < mine.b.Length; i++)
            {
                if (!Empty(mine.b[i]) || got.b == null || i >= got.b.Length || Empty(got.b[i])) continue;
                string w = got.b[i].Trim();
                if (w.Length > maxLength || !HangulOnly.IsMatch(w)) continue;
                mine.b[i] = w; n++;
            }
            return n;
        }
    }
}
