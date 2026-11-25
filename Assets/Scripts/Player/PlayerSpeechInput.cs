using Improv.Characters.Speech;
using Improv.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Improv.Player
{
    /// <summary>
    /// 플레이어의 말하기 조종자. 스페이스로 대화창을 열고(R-014), 보낸 문장을 플레이어의 입으로 말한다.
    /// 플레이어 귀에 들린 말을 대화 기록에 넣는다 (R-016a). 대화창이 열린 동안은 이동과 클릭을 막는다 (E-003).
    /// </summary>
    [RequireComponent(typeof(Speaker), typeof(Hearing))]
    public class PlayerSpeechInput : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [SerializeField] string talkActionName = "Player/Talk";
        [SerializeField] DialogueWindow window;
        [SerializeField] string selfLabel = "나";   // 대화 기록에서 내 말 앞에 붙는 이름

        // =====================================================================
        // #2. 상태
        // =====================================================================

        Speaker speaker;
        Hearing hearing;
        InputAction talkAction;
        Behaviour[] controlsToPause;   // 대화 중에 끌 플레이어 조작들

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Awake()
        {
            speaker = GetComponent<Speaker>();
            hearing = GetComponent<Hearing>();
            talkAction = InputSystem.actions?.FindAction(talkActionName);

            // E-003: 대화창이 열린 동안 꺼 둘 조작. 키보드로 글자를 치는 동안 WASD로 걷거나 E로 상호작용하면 안 된다.
            controlsToPause = new Behaviour[]
            {
                GetComponent<PlayerInputDriver>(),
                GetComponent<PlayerClickInteraction>(),
                GetComponent<PlayerHoverFeedback>()
            };

            if (talkAction == null) { Debug.LogError($"[PlayerSpeechInput] 입력 액션 '{talkActionName}'을 찾지 못했습니다.", this); enabled = false; }
            if (window == null) { Debug.LogError("[PlayerSpeechInput] DialogueWindow가 비어 있습니다.", this); enabled = false; }
        }

        void OnEnable()
        {
            if (window == null) return;
            hearing.Heard += OnHeard;
            window.Opened += PauseControls;
            window.Closed += ResumeControls;
            window.Submitted += OnSubmitted;
        }

        void OnDisable()
        {
            if (window == null) return;
            hearing.Heard -= OnHeard;
            window.Opened -= PauseControls;
            window.Closed -= ResumeControls;
            window.Submitted -= OnSubmitted;
        }

        void Update()
        {
            // 창이 열려 있을 때의 스페이스는 단어 고르기다. 여기서는 닫혀 있을 때만 본다.
            if (!window.IsOpen && talkAction.WasPressedThisFrame()) window.Open();
        }

        // =====================================================================
        // #4. 연결
        // =====================================================================

        void OnSubmitted(string sentence) => speaker.Say(sentence);

        void OnHeard(SpeechEvent speech)
        {
            bool isSelf = speech.Speaker == speaker;
            window.AddHistory(isSelf ? selfLabel : speech.SpeakerName, speech.Text, isSelf);
        }

        void PauseControls() => SetControls(false);
        void ResumeControls() => SetControls(true);

        void SetControls(bool on)
        {
            foreach (var control in controlsToPause)
                if (control != null) control.enabled = on;
        }
    }
}
