using Improv.Npc.Generation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Improv.UI
{
    /// <summary>
    /// 판이 시작할 때 캐릭터 시트를 만드는 동안 화면을 덮는 표시 (GDD 7장 Generating, 계획서 T-8).
    /// 무엇을 하는 중인지는 CastGenerator가 정하고 여기는 보여 주기만 한다 (P-2). 시계와 같은 UIDocument(Clock.uxml)를 쓴다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class CastGeneratingView : MonoBehaviour
    {
        VisualElement overlay;
        Label text;

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            overlay = root.Q("generating-overlay");
            text = root.Q<Label>("generating-text");
            if (overlay == null || text == null)
            {
                Debug.LogError("[CastGeneratingView] Clock.uxml에서 generating-overlay나 generating-text를 찾지 못했습니다.", this);
                enabled = false;
            }
        }

        void Update()
        {
            var generator = CastGenerator.Instance;
            bool show = generator != null && generator.IsGenerating;
            overlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show) text.text = generator.Status;
        }
    }
}
