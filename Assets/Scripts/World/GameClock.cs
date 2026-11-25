using System;
using UnityEngine;

namespace Improv.World
{
    /// <summary>
    /// 게임 시계 (R-017, R-017a, R-018). 하루의 시각과 시간 속도를 한곳에서 정한다.
    ///
    /// 속도는 유니티의 Time.timeScale로 건다. 그러면 이동·물리·연출·말풍선·대화 휘발·NPC 기억까지
    /// Time.deltaTime / Time.time을 쓰는 모든 것이 한꺼번에 빨라지거나 멈춘다.
    /// 그래서 "기억 시간도 게임 시계로"(계획서 F-4)는 따로 고칠 필요 없이 Time.time이 곧 게임 시간이 된다.
    /// (C로 치면 모든 모듈이 같은 전역 tick 함수를 쓰고 있어서, 그 tick의 배율만 바꾸면 되는 구조다.)
    /// LLM 응답처럼 바깥(실제 시간)에서 오는 것만 멈춤을 따로 지켜야 한다 (NpcDialogueBrain).
    /// </summary>
    public class GameClock : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [Header("하루 (O-004)")]
        [Tooltip("1배속에서 게임 하루가 지나는 실제 초. 1200 = 20분 [가정]")]
        [SerializeField] float dayLengthSeconds = 1200f;
        [Tooltip("게임을 시작하는 시각 (0~24). 저녁 6시 [확정 2026-11-23]")]
        [SerializeField] float startHour = 18f;

        [Header("속도 (R-017a, O-004 [가정])")]
        [SerializeField] float[] speedSteps = { 0f, 0.5f, 1f, 2f, 4f };
        [Tooltip("일반 플레이에서 올릴 수 있는 가장 높은 단계 번호. 4배(번호 4)는 LLM 호출을 줄인 뒤(계획서 T-5) 연다 (R-017a)")]
        [SerializeField] int maxPlayableStep = 3;
        [SerializeField] int startStep = 2;   // 1배

        [Header("수면 (R-018, O-004)")]
        [Tooltip("플레이어가 자는 동안의 속도. 10배 [확정 2026-11-23]")]
        [SerializeField] float sleepSpeed = 10f;
        [Tooltip("자고 있으면 이 시각에 저절로 깬다 (0~24). 아침 7시 [확정 2026-11-23]")]
        [SerializeField] float wakeHour = 7f;

        // =====================================================================
        // #2. 상태
        // =====================================================================

        /// <summary>씬에 하나뿐인 시계. 다른 스크립트가 찾지 않고 바로 쓰게 한다 (싱글턴).</summary>
        public static GameClock Instance { get; private set; }

        double elapsed;        // 시작부터 흐른 게임 시간 (1배속 실제 초 단위). float는 오래 쌓이면 오차가 커서 double
        int step;              // 지금 속도 단계 번호
        int lastRunningStep;   // 멈추기 전 단계. P를 다시 누르면 여기로 돌아간다
        bool playerSleeping;

        /// <summary>속도·멈춤·수면이 바뀌면 알린다. 시계 화면이 다시 그린다.</summary>
        public event Action Changed;
        /// <summary>자고 있다가 아침(wakeHour)이 되면 한 번 알린다. 플레이어 수면 조종자가 깨운다.</summary>
        public event Action MorningReached;

        public float Speed => playerSleeping ? sleepSpeed : speedSteps[step];
        public bool IsPaused => Speed == 0f;
        public bool IsPlayerSleeping => playerSleeping;
        public float WakeHour => wakeHour;
        /// <summary>1배속에서 게임 하루가 지나는 시간 (Time.deltaTime 단위의 초). 상태바가 "하루에 얼마"를 초당 값으로 바꿀 때 쓴다.</summary>
        public float DayLengthSeconds => dayLengthSeconds;
        public int Step => step;
        public int MaxPlayableStep => maxPlayableStep;
        public float StepSpeed(int index) => speedSteps[index];
        public int StepCount => speedSteps.Length;

        /// <summary>하루 시작(자정)부터 센 전체 게임 시간을 시간 단위로. 예: 1일째 저녁 6시 = 18.</summary>
        double TotalHours => startHour + elapsed / dayLengthSeconds * 24.0;
        /// <summary>며칠째인가. 첫날이 1.</summary>
        public int Day => 1 + (int)Math.Floor(TotalHours / 24.0);
        /// <summary>오늘의 시각 (0~24, 소수).</summary>
        public float HourOfDay => (float)(TotalHours % 24.0);
        /// <summary>"18:05" 모양 글자.</summary>
        public string TimeText
        {
            get
            {
                float h = HourOfDay;
                int hour = (int)h;
                int minute = (int)((h - hour) * 60f);
                return $"{hour:00}:{minute:00}";
            }
        }

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[GameClock] 씬에 시계가 둘 있습니다. 하나만 두세요.", this);
                enabled = false;
                return;
            }
            Instance = this;
            step = Mathf.Clamp(startStep, 0, speedSteps.Length - 1);
            lastRunningStep = speedSteps[step] > 0f ? step : 2;
            Apply();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            Time.timeScale = 1f;   // 시계가 사라지면(Play 끝 등) 속도를 원래대로. 안 하면 에디터에 배율이 남을 수 있다
        }

        void Update()
        {
            float before = HourOfDay;
            // Time.deltaTime은 이미 timeScale이 곱해진 값이다. 그래서 그냥 더하면 게임 시간이 된다.
            elapsed += Time.deltaTime;

            // 자는 중 아침이 되었나. 시각이 wakeHour를 "넘어가는 순간"만 본다 (자정을 넘는 경우도 처리).
            if (playerSleeping && Crossed(before, HourOfDay, wakeHour)) MorningReached?.Invoke();
        }

        static bool Crossed(float from, float to, float mark)
        {
            if (from <= to) return from < mark && mark <= to;
            return from < mark || mark <= to;   // 23시 → 0시처럼 하루가 넘어간 경우
        }

        // =====================================================================
        // #4. 속도 바꾸기 (R-017a)
        // =====================================================================

        /// <summary>P: 멈춤과 풀기를 오간다.</summary>
        public void TogglePause()
        {
            if (playerSleeping) return;   // 자는 동안은 수면 속도가 우선이다
            if (speedSteps[step] > 0f) { lastRunningStep = step; SetStep(0); }
            else SetStep(lastRunningStep);
        }

        /// <summary>+ / -: 한 단계씩. 멈춤(0번)에서 +를 누르면 가장 느린 속도부터.</summary>
        public void StepUp() => SetStep(step + 1);
        public void StepDown() => SetStep(step - 1);

        public void SetStep(int index)
        {
            index = Mathf.Clamp(index, 0, maxPlayableStep);
            if (speedSteps[index] > 0f) lastRunningStep = index;
            step = index;
            Apply();
        }

        /// <summary>플레이어가 잠들거나 깰 때 수면 조종자가 부른다 (R-018).</summary>
        public void SetPlayerSleeping(bool sleeping)
        {
            playerSleeping = sleeping;
            // 멈춘 채로 잠들었다가 깨면 계속 멈춰 있어 헷갈린다. 깨면 멈추기 전 속도로 돌아간다.
            if (!sleeping && speedSteps[step] == 0f) step = lastRunningStep;
            Apply();
        }

        void Apply()
        {
            Time.timeScale = Speed;
            Changed?.Invoke();
        }

        /// <summary>테스트용: 게임 시간을 바로 건너뛴다 (게임 시간 시 단위).</summary>
        public void DebugAdvanceHours(float hours) => elapsed += hours / 24.0 * dayLengthSeconds;
    }
}
