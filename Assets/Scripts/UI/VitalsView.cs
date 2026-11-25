using System.Collections.Generic;
using Improv.Characters;
using UnityEngine;
using UnityEngine.UIElements;

namespace Improv.UI
{
    /// <summary>
    /// 플레이어 상태바 화면 (R-020, GDD 8장 플레이 HUD). 왼쪽 아래에 바 5개, 죽으면 패배 화면 (R-022).
    /// 값은 CharacterVitals가 정하고 여기는 그리기만 한다 (P-2). 시계와 같은 UIDocument(Clock.uxml)를 쓴다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class VitalsView : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [SerializeField] CharacterVitals target;   // 플레이어의 상태바
        [Tooltip("이 값 아래면(스트레스는 위면) 이름을 붉게 표시한다")]
        [SerializeField] float warnBelow = 30f;

        // =====================================================================
        // #2. 상태
        // =====================================================================

        // 바 한 줄. struct가 아니라 class인 이유: 리스트에 넣은 뒤 그 안의 요소를 계속 고쳐 쓰기 때문 (C의 포인터 목록과 같다).
        class Row
        {
            public string name;
            public System.Func<CharacterVitals, float> read;   // 이 바의 값을 읽는 함수 (C의 함수 포인터)
            public bool highIsBad;                               // 스트레스처럼 높을수록 나쁜 바
            public Label label, value;
            public VisualElement fill;
        }

        UIDocument document;
        VisualElement deathOverlay;
        readonly List<Row> rows = new();

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Awake()
        {
            document = GetComponent<UIDocument>();
            if (target == null) Debug.LogError("[VitalsView] target(플레이어의 CharacterVitals)이 비어 있습니다.", this);
        }

        void OnEnable()
        {
            var root = document.rootVisualElement;
            var panel = root.Q("vitals-panel");
            deathOverlay = root.Q("death-overlay");
            if (panel == null || deathOverlay == null)
            {
                Debug.LogError("[VitalsView] Clock.uxml에서 vitals-panel이나 death-overlay를 찾지 못했습니다.", this);
                enabled = false;
                return;
            }

            panel.Clear();
            rows.Clear();
            AddRow(panel, "건강", v => v.Health, false, new Color(0.85f, 0.3f, 0.3f));
            AddRow(panel, "배고픔", v => v.Hunger, false, new Color(0.9f, 0.65f, 0.25f));
            AddRow(panel, "스테미너", v => v.Stamina, false, new Color(0.35f, 0.75f, 0.4f));
            AddRow(panel, "체온", v => v.Temperature, false, new Color(0.4f, 0.6f, 0.95f));
            AddRow(panel, "스트레스", v => v.Stress, true, new Color(0.7f, 0.45f, 0.85f));
        }

        void AddRow(VisualElement panel, string title, System.Func<CharacterVitals, float> read, bool highIsBad, Color color)
        {
            var row = new VisualElement();
            row.AddToClassList("vital-row");
            var label = new Label(title);
            label.AddToClassList("vital-label");
            var track = new VisualElement();
            track.AddToClassList("vital-track");
            var fill = new VisualElement();
            fill.AddToClassList("vital-fill");
            fill.style.backgroundColor = color;
            track.Add(fill);
            var value = new Label();
            value.AddToClassList("vital-value");
            row.Add(label);
            row.Add(track);
            row.Add(value);
            panel.Add(row);
            rows.Add(new Row { name = title, read = read, highIsBad = highIsBad, label = label, value = value, fill = fill });
        }

        void Update()
        {
            if (target == null) return;
            foreach (var row in rows)
            {
                float v = row.read(target);
                row.fill.style.width = Length.Percent(v / CharacterVitals.Max * 100f);
                row.value.text = Mathf.RoundToInt(v).ToString();
                bool warn = row.highIsBad ? v > CharacterVitals.Max - warnBelow : v < warnBelow;
                row.label.EnableInClassList("vital-label--low", warn);
            }
            deathOverlay.style.display = target.IsDead ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
