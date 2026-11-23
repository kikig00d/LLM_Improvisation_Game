using UnityEngine;

namespace Improv.Llm
{
    /// <summary>
    /// LLM 연결 설정을 에셋 파일 하나에 모아 둔다. NPC가 여럿이어도 같은 파일을 가리키면 된다.
    /// ScriptableObject: 씬에 붙지 않고 프로젝트에 파일(.asset)로 저장되는 데이터 묶음.
    /// C로 치면 설정값 struct를 파일로 저장해 두고 여러 곳에서 포인터로 가리키는 것과 같다.
    /// </summary>
    [CreateAssetMenu(fileName = "LlmSettings", menuName = "Improv/LLM Settings")]
    public class LlmSettings : ScriptableObject
    {
        [SerializeField] string baseUrl = "http://localhost:11434/v1";
        [SerializeField] string model = "qwen3:8b";   // Q2 결정
        [SerializeField] int timeoutSeconds = 60;
        [SerializeField, Range(0f, 1.5f)] float temperature = 0.7f;
        [SerializeField] int maxTokens = 120;
        [Tooltip("생각하는 모델(qwen3)의 생각 단계. none이면 끄고 바로 답한다.")]
        [SerializeField] string reasoningEffort = "none";

        public ILlmClient CreateClient() =>
            new OpenAiCompatibleClient(baseUrl, model, timeoutSeconds, temperature, maxTokens, reasoningEffort);
    }
}
