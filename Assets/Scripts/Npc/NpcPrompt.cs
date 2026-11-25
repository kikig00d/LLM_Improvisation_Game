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
        public static string BuildSystem(NpcCharacterSheet sheet, string myName, string roomName, IReadOnlyList<string> visibleNames, IReadOnlyList<string> longTermNotes)
        {
            var sb = new StringBuilder();
            // 이름은 시트가 아니라 지금 이름을 쓴다. 개명하면 바뀐다 (F-17). 시트의 이름은 처음 이름일 뿐이다.
            sb.AppendLine($"너는 {myName}이다. {sheet.publicProfile}");
            sb.AppendLine($"지금 상황: 눈보라로 고립된 겨울 산장. 너는 지금 '{roomName}'에 있다.");
            // R-011, P-3: 내 눈에 보이는 사람만 알려 준다. 등 뒤나 벽 너머 사람은 목소리로만 안다.
            sb.AppendLine(visibleNames.Count > 0
                ? $"지금 눈에 보이는 사람: {string.Join(", ", visibleNames)}. 그 밖의 사람은 보이지 않고 목소리만 들린다."
                : "지금 눈에 보이는 사람은 없다. 들리는 목소리는 보이지 않는 곳에서 온다.");
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
            sb.AppendLine("- 자신이 AI라고 말하지 않는다.");
            sb.AppendLine("- 들은 말은 시간이 지나면 잊는다. 나중에도 꼭 기억해야 할 것만 remember에 짧게 적는다. 대부분은 비워 둔다.");
            sb.AppendLine();
            // R-016b, R-016d: 행동과 기억도 LLM이 고른다. 코드가 강제하지 않는다.
            sb.AppendLine("반드시 아래 JSON 한 개로만 답한다:");
            sb.AppendLine("{\"look_at\": \"돌아볼 사람 이름. 돌아보지 않으면 빈 문자열\", \"say\": \"할 말. 말하지 않으면 빈 문자열\", \"remember\": \"꼭 기억할 짧은 메모. 없으면 빈 문자열\"}");
            return sb.ToString();
        }
    }
}
