using Improv.Characters;
using Improv.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Improv.Player
{
    /// <summary>
    /// 플레이어의 잠 조종자 (R-018). 침대를 클릭하면 몸(CharacterSleep)이 눕고, 그때부터 여기서 맡는다.
    /// - 자는 동안 시계를 수면 속도로 돌리고, 움직임·클릭·시선·대화 입력을 끈다.
    /// - 아무 키나 마우스 버튼을 누르면, 또는 아침(GameClock.wakeHour)이 되면 깬다 (2026-11-23 결정).
    /// 귀는 끄지 않는다. 자는 동안에도 NPC들 말이 들리고, 대화 기록은 빨라진 만큼 빨리 사라진다 (사용자 결정).
    /// </summary>
    [RequireComponent(typeof(CharacterSleep))]
    public class PlayerSleepInput : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [Tooltip("잠든 직후 이 시간(실제 초) 동안은 키를 눌러도 깨지 않는다. 침대를 누른 클릭이 바로 깨우지 않게.")]
        [SerializeField] float wakeInputDelay = 0.5f;

        // =====================================================================
        // #2. 상태
        // =====================================================================

        CharacterSleep sleep;
        Behaviour[] controls;   // 자는 동안 끌 플레이어 입력들
        float fellAsleepAt;     // 실제 시각 (Time.unscaledTime). 10배속이어도 "실제로" 0.5초를 기다리려고

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Awake()
        {
            sleep = GetComponent<CharacterSleep>();
            // 같은 오브젝트의 플레이어 입력들. 없는 것은 null이라 아래에서 건너뛴다.
            controls = new Behaviour[]
            {
                GetComponent<PlayerInputDriver>(),
                GetComponent<PlayerClickInteraction>(),
                GetComponent<PlayerHoverFeedback>(),
                GetComponent<PlayerGazeInput>(),
                GetComponent<PlayerSpeechInput>(),
            };
        }

        void OnEnable()
        {
            sleep.FellAsleep += OnFellAsleep;
            sleep.WokeUp += OnWokeUp;
            if (GameClock.Instance != null) GameClock.Instance.MorningReached += OnMorning;
        }

        // GameClock.Awake가 이 스크립트의 OnEnable보다 늦을 수 있어 Start에서 한 번 더 확인한다.
        void Start()
        {
            if (GameClock.Instance == null) Debug.LogError("[PlayerSleepInput] 씬에 GameClock이 없습니다. 잠을 자도 시간이 빨라지지 않습니다.", this);
            else { GameClock.Instance.MorningReached -= OnMorning; GameClock.Instance.MorningReached += OnMorning; }
        }

        void OnDisable()
        {
            sleep.FellAsleep -= OnFellAsleep;
            sleep.WokeUp -= OnWokeUp;
            if (GameClock.Instance != null) GameClock.Instance.MorningReached -= OnMorning;
        }

        void Update()
        {
            if (!sleep.IsAsleep || Time.unscaledTime - fellAsleepAt < wakeInputDelay) return;

            // 아무 키, 아무 마우스 버튼. "아무 키"는 액션으로 묶기보다 장치에 직접 묻는 쪽이 정확하다 (새 Input System 장치 API).
            bool key = Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;
            var mouse = Mouse.current;
            bool click = mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame);
            if (key || click) sleep.WakeUp();
        }

        // =====================================================================
        // #4. 잠들고 깨기
        // =====================================================================

        void OnFellAsleep()
        {
            fellAsleepAt = Time.unscaledTime;
            foreach (var c in controls) if (c != null) c.enabled = false;
            if (GameClock.Instance != null) GameClock.Instance.SetPlayerSleeping(true);
        }

        void OnWokeUp()
        {
            foreach (var c in controls) if (c != null) c.enabled = true;
            if (GameClock.Instance != null) GameClock.Instance.SetPlayerSleeping(false);
        }

        void OnMorning() => sleep.WakeUp();
    }
}
