using System.Collections.Generic;
using Improv.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Improv.UI
{
    /// <summary>
    /// 시계 화면 (R-017, R-017a, R-018). 며칠째·시각, 속도 버튼, 멈춤 표시, 자는 중 화면을 그린다.
    /// 시간 판정은 전부 GameClock이 하고, 여기는 보여 주고 버튼 클릭을 GameClock에 넘기기만 한다 (P-2).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ClockView : MonoBehaviour
    {
        // =====================================================================
        // #1. 상태
        // =====================================================================

        UIDocument document;
        Label timeLabel, sleepText, pauseBanner;
        VisualElement sleepOverlay;
        readonly List<Button> speedButtons = new();

        // =====================================================================
        // #2. 유니티 생명주기
        // =====================================================================

        void Awake() => document = GetComponent<UIDocument>();

        // UIDocument는 OnEnable 때 화면을 새로 만든다. 그래서 요소 찾기와 버튼 만들기도 OnEnable에서 (DialogueWindow와 같은 이유).
        void OnEnable()
        {
            var root = document.rootVisualElement;
            timeLabel = root.Q<Label>("clock-time");
            sleepOverlay = root.Q("sleep-overlay");
            sleepText = root.Q<Label>("sleep-text");
            pauseBanner = root.Q<Label>("pause-banner");
            var row = root.Q("speed-buttons");
            if (timeLabel == null || row == null || sleepOverlay == null || pauseBanner == null)
            {
                Debug.LogError("[ClockView] Clock.uxml에서 요소를 찾지 못했습니다.", this);
                enabled = false;
                return;
            }

            BuildSpeedButtons();
        }

        // 시계(GameClock)가 이 오브젝트보다 늦게 깨어났으면 OnEnable 때는 버튼을 못 만든다. 그때는 Start에서 만든다.
        void Start()
        {
            if (speedButtons.Count == 0) BuildSpeedButtons();
        }

        /// <summary>속도 버튼을 GameClock의 단계 수만큼 코드로 만든다. 단계 값을 바꾸면 버튼도 따라 바뀐다.</summary>
        void BuildSpeedButtons()
        {
            var clock = GameClock.Instance;
            var row = document.rootVisualElement.Q("speed-buttons");
            if (clock == null || row == null) return;
            row.Clear();
            speedButtons.Clear();
            for (int i = 0; i < clock.StepCount; i++)
            {
                int index = i;   // 람다가 반복 변수 i 대신 이 복사본을 붙잡게 한다 (C#의 클로저)
                float s = clock.StepSpeed(i);
                var button = new Button(() => GameClock.Instance?.SetStep(index)) { text = s == 0f ? "멈춤" : $"{s:0.#}배" };
                button.AddToClassList("speed-button");
                if (i > clock.MaxPlayableStep)
                {
                    button.SetEnabled(false);
                    button.tooltip = "LLM 호출을 줄인 뒤에 열린다 (R-017a)";
                }
                row.Add(button);
                speedButtons.Add(button);
            }
        }

        void Update()
        {
            var clock = GameClock.Instance;
            if (clock == null) return;

            timeLabel.text = $"{clock.Day}일째  {clock.TimeText}";
            for (int i = 0; i < speedButtons.Count; i++)
                speedButtons[i].EnableInClassList("speed-button--on", !clock.IsPlayerSleeping && i == clock.Step);

            pauseBanner.style.display = clock.IsPaused ? DisplayStyle.Flex : DisplayStyle.None;

            sleepOverlay.style.display = clock.IsPlayerSleeping ? DisplayStyle.Flex : DisplayStyle.None;
            if (clock.IsPlayerSleeping)
                sleepText.text = $"자는 중… 시간이 {clock.Speed:0.#}배로 흐릅니다\n아무 키나 누르면 일어납니다 (아침 {clock.WakeHour:0}시에 저절로 깹니다)";
        }
    }
}
