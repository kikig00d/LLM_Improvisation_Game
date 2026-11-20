using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Improv.Llm
{
    /// <summary>
    /// 화이트박스 확인용 LLM 로그 (GDD 10장 "LLM 디버그").
    /// 게임이 보낸 JSON, 받은 JSON, 걸린 시간을 손대지 않고 그대로 남긴다.
    /// - Console: 한 줄 요약이 보이고, 클릭하면 아래 칸에 전문이 나온다.
    /// - 파일: 프로젝트 폴더의 Logs/llm_debug.log. 길게 비교할 때는 이쪽이 읽기 편하다 (git에는 안 올라간다).
    /// </summary>
    public static class LlmDebugLog
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        public static bool Enabled = true;

        // Application.dataPath는 Assets 폴더다. 그 위가 프로젝트 폴더.
        static string FilePath => Path.Combine(Application.dataPath, "..", "Logs", "llm_debug.log");

        static int counter; // 요청 번호. 콘솔과 파일에서 같은 요청을 찾기 쉽게 한다

        // =====================================================================
        // #2. 기록
        // =====================================================================

        public static void Write(string url, string requestJson, long httpCode, string responseJson, LlmReply reply)
        {
            if (!Enabled) return;
            counter++;

            string tokens = reply.CompletionTokens > 0
                ? $" | 토큰 보냄 {reply.PromptTokens} / 받음 {reply.CompletionTokens}"
                : "";
            string summary = $"[LLM #{counter}] {reply.Model} | {reply.Status} | {reply.ElapsedSeconds:F2}초{tokens}";

            var detail = new StringBuilder();
            detail.AppendLine(summary);
            detail.AppendLine($"--- 보낸 요청: POST {url}");
            detail.AppendLine(requestJson);
            detail.AppendLine($"--- 받은 응답: HTTP {httpCode}");
            detail.AppendLine(string.IsNullOrEmpty(responseJson) ? "(없음)" : responseJson);
            if (!string.IsNullOrEmpty(reply.Reasoning))
            {
                detail.AppendLine("--- 모델의 생각 과정 (대사에서는 뺌)");
                detail.AppendLine(reply.Reasoning);
            }
            detail.AppendLine(reply.Ok ? $"--- NPC 대사: {reply.Text}" : $"--- 실패 이유: {reply.Error}");

            string text = detail.ToString();

            // 실패는 경고로 찍어 눈에 띄게 한다. 오류(빨간색)는 E-005 처리하는 쪽에서 한 번만 찍는다.
            if (reply.Ok) Debug.Log(text);
            else Debug.LogWarning(text);

            AppendToFile(text);
        }

        static void AppendToFile(string text)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.AppendAllText(FilePath, $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{text}\n", Encoding.UTF8);
            }
            catch (Exception e)
            {
                // 로그 파일 쓰기 실패가 게임을 멈추면 안 되므로 경고만 남긴다.
                Debug.LogWarning($"[LLM] 로그 파일을 쓰지 못함: {e.Message}");
            }
        }
    }
}
