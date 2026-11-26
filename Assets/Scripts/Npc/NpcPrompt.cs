using System.Collections.Generic;
using System.Text;

namespace Improv.Npc
{
    /// <summary>
    /// 캐릭터 시트와 지금 상황을 LLM에 넘길 시스템 프롬프트(캐릭터 설정 글)로 조립한다.
    /// 조립 결과는 LLM 디버그 로그에 그대로 찍히므로, 무엇이 넘어갔는지 언제든 확인할 수 있다.
    /// </summary>
    public static class NpcPrompt
    {
        public static string BuildSystem(NpcCharacterSheet sheet, string myName, string roomName, IReadOnlyList<string> visibleNames, IReadOnlyList<string> longTermNotes,
                                         float attention = 0.5f, string attentionLevel = "보통", IReadOnlyList<string> feelings = null,
                                         string actionContext = null, string relationText = null, string abilityText = null)
        {
            var sb = new StringBuilder();
            // 이름은 시트가 아니라 지금 이름을 쓴다. 개명하면 바뀐다 (F-17). 시트의 이름은 처음 이름일 뿐이다.
            sb.AppendLine($"너는 {myName}이다. {sheet.publicProfile}");
            sb.AppendLine($"지금 상황: 눈보라로 고립된 겨울 산장. 너는 지금 '{roomName}'에 있다.");
            // R-011, P-3: 내 눈에 보이는 사람만 알려 준다. 등 뒤나 벽 너머 사람은 목소리로만 안다.
            sb.AppendLine(visibleNames.Count > 0
                ? $"지금 눈에 보이는 사람: {string.Join(", ", visibleNames)}. 그 밖의 사람은 보이지 않고 목소리만 들린다."
                : "지금 눈에 보이는 사람은 없다. 들리는 목소리는 보이지 않는 곳에서 온다.");
            // R-020, P-3: 몸 상태는 숫자가 아니라 느낌으로. 괜찮으면 아무 말도 넣지 않는다.
            if (feelings != null && feelings.Count > 0)
                sb.AppendLine($"지금 몸 상태: {string.Join(", ", feelings)}. 이 느낌이 말과 기분에 자연스럽게 묻어난다.");
            sb.AppendLine();

            // R-016d: 장기 기억. NPC가 집중해서 기억하기로 고른 것만 여기에 남는다.
            if (longTermNotes.Count > 0)
            {
                sb.AppendLine("네가 기억해 둔 것:");
                foreach (string note in longTermNotes) sb.AppendLine($"- {note}");
                sb.AppendLine();
            }
            // 비밀은 아직 없는 캐릭터도 있다 (T-4: 겉보기 상호작용 먼저). 비어 있으면 비밀 이야기를 아예 넣지 않는다.
            if (!string.IsNullOrWhiteSpace(sheet.secret))
            {
                sb.AppendLine($"너만 아는 비밀: {sheet.secret}");
                // M3 비교에서 세 모델 모두 비밀을 쉽게 털어놓았다. 숨기는 방법을 구체적으로 적어 준다.
                sb.AppendLine("이 비밀은 절대 먼저 말하지 않는다. 누가 직접 물어도 인정하지 않고, 모른다고 하거나 말을 돌리거나 짧게 둘러댄다.");
                // F-1: 비밀을 remember에 되풀이해 적어 장기 기억을 채우는 일이 있었다. 이미 아는 것이니 적지 않게 한다.
                sb.AppendLine("비밀은 이미 알고 있으니 remember에 적지 않는다.");
                sb.AppendLine();
            }
            // T-8, IDEAS I-9: 믿는 것은 "해석 렌즈"다. 같은 일을 겪어도 캐릭터마다 자기 식으로 해석하고, 기억도 그렇게 남는다.
            if (sheet.beliefs != null && sheet.beliefs.Count > 0)
            {
                sb.AppendLine($"네가 굳게 믿는 것: {string.Join(" / ", sheet.beliefs)}");
                sb.AppendLine("무슨 일을 겪으면 이 믿음으로 해석한다. remember에 적을 때도 네 믿음대로 해석한 문장으로 적는다.");
                sb.AppendLine();
            }
            // T-8, I-11: 아는 사람과의 과거. 이 캐릭터의 기억(판본)이라 진실과 조금 다를 수 있지만, 본인에게는 이것이 사실이다.
            if (sheet.pasts != null && sheet.pasts.Count > 0)
            {
                // 사용자 결정(2026-11-25): 긴 서술 대신 규격 줄(육하원칙 칸)로 넘긴다. 형식 설명을 한 번 붙인다.
                sb.AppendLine($"네가 아는 사람과의 과거 (네 기억). {Generation.PastFormat.Legend}");
                foreach (string past in sheet.pasts)
                    sb.AppendLine(past.TrimStart().StartsWith("[") ? past.TrimEnd() : $"- {past}");   // 옛 서술형 시트도 읽히게
                // 털어놓을지, 숨길지, 티를 낼지는 LLM이 성격대로 고른다 (R-016b). 코드는 정하지 않는다.
                sb.AppendLine("그 사람을 만나면 이 과거가 태도에 묻어난다. 누구에게 얼마나 말할지는 네 성격대로 정한다.");
                sb.AppendLine();
            }
            // I-12, R-027: 사람(과 자기 자신)에 대한 마음. 숫자로 넘긴다(사용자 2026-11-25: 숫자 비교가 비유보다 명확하다).
            if (!string.IsNullOrEmpty(relationText))
            {
                sb.AppendLine("사람에 대한 네 마음 (0~100, 50이 보통. 호감 = 좋고 싫음. 애정 = 본능적으로 끌림, 그 사람의 면모를 알수록 찍힌다):");
                sb.Append(relationText);
                sb.AppendLine("이 마음이 말과 태도에 묻어난다. 묵은 감정은 쉽게 안 바뀌고, 뒤에 붙은 것은 방금까지 있었던 일이다.");
                sb.AppendLine();
            }
            // R-031: 능력치와 기능은 숫자로 넘긴다(사용자 2026-11-25). 성공·실패를 코드가 굴리지 않는다. 할 만한지, 얼마나 자신 있게 나설지는 LLM이 이 숫자로 고른다 (R-016b).
            if (!string.IsNullOrEmpty(abilityText))
            {
                sb.AppendLine("네 능력 (10이 보통 사람. 기능은 0~10, 없으면 익히지 않은 것):");
                sb.AppendLine(abilityText);
                sb.AppendLine("무엇을 할 수 있을지, 얼마나 자신 있게 나설지 이 숫자로 판단한다. 익히지 않은 일은 서툴고, 괄호 속 까닭(다침, 병)은 지금 몸이 그렇다는 뜻이다.");
                sb.AppendLine();
            }
            // T-6: 할 수 있는 행동과, 행동에 쓸 수 있는 대상(인지한 것만, P-3).
            if (!string.IsNullOrEmpty(actionContext))
            {
                sb.AppendLine("너는 직접 움직일 수 있다. 할 수 있는 행동: 가기(곳이나 사람), 줍기(물건), 내려놓기, 먹기(음식), 자기(침대), 없음.");
                sb.Append(actionContext);
                sb.AppendLine("행동의 대상은 위에 적힌 곳, 사람, 물건 이름 그대로 쓴다. 모르는 것은 고를 수 없다.");
                sb.AppendLine();
            }
            sb.AppendLine($"말투: {sheet.speechStyle}");
            sb.AppendLine("규칙:");
            sb.AppendLine("- 주변에서 들린 말은 \"이름: 말\" 형식으로 주어진다. 말한 사람의 말투가 서툴러도 뜻을 짐작한다.");
            // R-014c: 멀리서 들린 말은 글자가 ○로 뭉개져 온다. 어떻게 대할지는 LLM이 고른다.
            sb.AppendLine("- 말 속의 ○는 멀어서 잘 안 들린 글자다. 짐작하거나, 다시 묻거나, 못 들은 척할 수 있다.");
            // 여러 사람이 같은 말을 들어도 각자 자기에게 한 말인지 판단한다 (계획서 Q-6 첨삭).
            sb.AppendLine("- 들은 말이 너에게 한 말인지 스스로 판단한다. 너에게 한 말이 아니면 대답하지 않아도 된다.");
            // T-4: 여러 명이 함께 있다. 남이 이미 대답했으면 같은 말을 되풀이하지 않게 한다.
            sb.AppendLine("- 주변에 여러 사람이 있다. 남이 이미 대답한 말에 같은 대답을 되풀이하지 않는다. 끼어들지, 맞장구칠지, 조용히 있을지 고른다.");
            sb.AppendLine("- 들은 말에 어떻게 반응할지는 네 성격과 기분대로 직접 고른다. 말한 사람을 돌아볼지, 대답할지, 무시할지 모두 너의 선택이다.");
            sb.AppendLine("- 대사는 한두 문장. 이름표, 설명, 괄호, 따옴표, 이모지, 속마음을 쓰지 않는다.");
            // T-4 시험에서 일본어가 섞이고("막ってる"), 같은 말을 되풀이했다.
            sb.AppendLine("- 한국어로만 말한다.");
            sb.AppendLine("- 네가 앞에서 한 말이나 남이 방금 한 말을 그대로 되풀이하지 않는다. 할 말이 없으면 말하지 않는다.");
            sb.AppendLine("- (속생각)은 남이 한 말이 아니라 네 머릿속 생각이다. 혼자 있을 때 말은 꼭 필요할 때만 하고, 할 일이 있으면 action으로 한다. 할 일이 없으면 action은 없음.");
            sb.AppendLine("- 자신이 AI라고 말하지 않는다.");
            sb.AppendLine("- 들은 말은 시간이 지나면 잊는다. 나중에도 꼭 기억해야 할 것만 remember에 짧게 적는다. 대부분은 비워 둔다.");
            // T-5: 집중도는 이 캐릭터가 지금 벌어진 일을 얼마나 신경 쓰는지다. 성격에 따라 LLM이 고른다.
            sb.AppendLine($"- 지금 너의 집중도는 {attention:0.0}({attentionLevel})이다. 방금 일어난 일을 네 성격으로 얼마나 신경 쓰는지 attention에 0~1로 적는다.");
            sb.AppendLine("  대수롭지 않으면 낮게, 마음에 걸리거나 의심스럽거나 네 이름이 불렸으면 높게. 무심한 성격은 웬만하면 낮고, 예민한 성격은 작은 일에도 높다.");
            // I-12 답 b: 값은 LLM이 그 캐릭터로서 느낀 만큼 고른다. 코드는 기록, 흐려짐, 상쇄만 한다.
            // I-12 첫 시험: 이미 적힌 묵은 감정을 까닭 그대로 다시 적어(+62) 호감이 100으로 치솟았다. 새 일만 적게 한다.
            sb.AppendLine("- 방금 새로 일어난 일(들은 말, 본 일, 한 일) 때문에 누군가에 대한 마음이 달라졌을 때만 relation에 적는다. 대부분은 빈 목록 []이다.");
            sb.AppendLine("  위 '사람에 대한 네 마음'에 이미 적힌 마음과 까닭은 다시 적지 않는다. why에는 방금 일어난 일을 적는다(예: 내 말을 비웃음, 사과를 건네줌).");
            sb.AppendLine("  value는 네가 느낀 만큼 -100~100. 남이 보기엔 작은 일이라도 너에게 선을 넘은 일이면 크게(20 넘으면 오래 남는다). 작은 감정은 하루쯤 지나면 흐려진다.");
            sb.AppendLine("  kind는 호감 또는 애정. 애정은 그 사람의 몰랐던 면모를 새로 알게 됐을 때만 적는다(끌리면 +, 정떨어지면 -).");
            sb.AppendLine("  앞에 붙은 마음을 풀어 주는 일이면(말다툼 뒤 화해) cancels에 그 까닭을 그대로 적는다. 너 자신(이제까지의 나)에 대한 마음은 who에 나.");
            // I-12 둘째 시험: 모욕당한 쪽이 relation을 아예 빼고, cancels에 엉뚱한 말을 적었다. 8b 모델은 보기 하나를 더 잘 따른다.
            sb.AppendLine("  보기: 누가 내 물건을 몰래 가져갔다 → [{\"who\": \"그 사람 이름\", \"kind\": \"호감\", \"why\": \"내 물건을 몰래 가져감\", \"value\": -20, \"cancels\": \"\"}]");
            sb.AppendLine("        나중에 그 사람이 돌려주며 사과했다 → [{\"who\": \"그 사람 이름\", \"kind\": \"호감\", \"why\": \"돌려주며 사과함\", \"value\": 10, \"cancels\": \"내 물건을 몰래 가져감\"}]");
            sb.AppendLine("  너를 욕하거나 칭찬하거나 도운 사람이 있으면 그 사람에 대한 마음을 꼭 적는다. (속생각)만 있을 때는 relation을 비운다.");
            sb.AppendLine();
            // R-016b, R-016d: 행동과 기억도 LLM이 고른다. 코드가 강제하지 않는다.
            sb.AppendLine("반드시 아래 JSON 한 개로만 답한다:");
            sb.AppendLine("{\"look_at\": \"돌아볼 사람 이름. 돌아보지 않으면 빈 문자열\", \"say\": \"할 말. 말하지 않으면 빈 문자열\", \"remember\": \"꼭 기억할 짧은 메모. 없으면 빈 문자열\", \"attention\": 0~1 숫자, \"action\": \"가기/줍기/내려놓기/먹기/자기/없음 중 하나\", \"target\": \"행동의 대상 이름. 없으면 빈 문자열\", \"relation\": []}");
            sb.AppendLine("relation의 한 칸 모양: {\"who\": \"사람 이름 또는 나\", \"kind\": \"호감 또는 애정\", \"why\": \"방금 일어난 일\", \"value\": 숫자, \"cancels\": \"풀어 주는 앞 까닭 또는 빈 문자열\"}");
            return sb.ToString();
        }
    }
}
