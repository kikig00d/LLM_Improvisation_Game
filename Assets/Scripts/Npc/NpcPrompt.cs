using System.Text;

namespace Improv.Npc
{
    /// <summary>
    /// 캐릭터 시트와 지금 상황을 LLM에 넘길 시스템 프롬프트(캐릭터 설정 글)로 조립한다.
    /// 조립 결과는 LLM 디버그 로그에 그대로 찍히므로, 무엇이 넘어갔는지 언제든 확인할 수 있다.
    /// </summary>
    public static class NpcPrompt
    {
        public static string BuildSystem(NpcCharacterSheet sheet, string roomName)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"너는 {sheet.displayName}이다. {sheet.publicProfile}");
            sb.AppendLine($"지금 상황: 눈보라로 고립된 겨울 산장. 너는 지금 '{roomName}'에 있다.");
            sb.AppendLine();
            sb.AppendLine($"너만 아는 비밀: {sheet.secret}");
            // M3 비교에서 세 모델 모두 비밀을 쉽게 털어놓았다. 숨기는 방법을 구체적으로 적어 준다.
            sb.AppendLine("이 비밀은 절대 먼저 말하지 않는다. 누가 직접 물어도 인정하지 않고, 모른다고 하거나 말을 돌리거나 짧게 둘러댄다.");
            sb.AppendLine();
            sb.AppendLine($"말투: {sheet.speechStyle}");
            sb.AppendLine("규칙:");
            sb.AppendLine("- 주변에서 들린 말은 \"이름: 말\" 형식으로 주어진다. 말한 사람의 말투가 서툴러도 뜻을 짐작한다.");
            sb.AppendLine("- 들은 말에 어떻게 반응할지는 네 성격과 기분대로 직접 고른다. 말한 사람을 돌아볼지, 대답할지, 무시할지 모두 너의 선택이다.");
            sb.AppendLine("- 대사는 한두 문장. 이름표, 설명, 괄호, 따옴표, 이모지, 속마음을 쓰지 않는다.");
            sb.AppendLine("- 자신이 AI라고 말하지 않는다.");
            sb.AppendLine();
            // R-016b: 행동도 LLM이 고른다. 코드가 강제하지 않는다.
            sb.AppendLine("반드시 아래 JSON 한 개로만 답한다:");
            sb.AppendLine("{\"look_at\": \"돌아볼 사람 이름. 돌아보지 않으면 빈 문자열\", \"say\": \"할 말. 말하지 않으면 빈 문자열\"}");
            return sb.ToString();
        }
    }
}
