using System.Collections.Generic;
using Improv.Characters.Speech;
using UnityEngine;
using UnityEngine.UIElements;

namespace Improv.UI
{
    /// <summary>
    /// 말한 사람 머리 위에 말풍선을 띄운다 (R-016a). LLM을 기다리는 NPC 위에는 "..."를 띄운다 (E-004).
    /// 플레이어 귀에 들린 말만 보인다. 누가 무엇을 들었는지는 Hearing이 판정하고, 여기는 표시만 한다 (P-1, P-2).
    /// "..." 생각 중 표시는 소리가 아니라 눈에 보이는 모습이라 지금은 거리와 상관없이 보인다. 시야(M5)에서 다시 다룬다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class SpeechBubbleOverlay : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [SerializeField] Camera worldCamera;              // 비우면 Main Camera
        [Tooltip("이 귀에 들린 말만 말풍선으로 보인다 (R-016a). 보통 플레이어. 비우면 모든 말을 보인다(디버그용).")]
        [SerializeField] Hearing listener;
        [Tooltip("생각 중 ... 표시는 소리가 아니라 모습이라, 이 눈에 보일 때만 뜬다 (F-6). 비우면 늘 뜬다.")]
        [SerializeField] Improv.Characters.Sight.Vision viewer;
        [SerializeField] float baseDuration = 2.5f;       // 말풍선이 떠 있는 기본 시간 (초)
        [SerializeField] float perCharDuration = 0.12f;   // 글자 하나당 더 떠 있는 시간. 긴 말은 오래 읽게

        // =====================================================================
        // #2. 상태
        // =====================================================================

        class Bubble
        {
            public Label Label;
            public string Text;
            public float HideAt;
        }

        UIDocument document;
        VisualElement layer;
        readonly Dictionary<Speaker, Bubble> bubbles = new();
        readonly List<Speaker> removeBuffer = new();

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Awake()
        {
            document = GetComponent<UIDocument>();
            if (worldCamera == null) worldCamera = Camera.main;
        }

        void OnEnable()
        {
            layer = document.rootVisualElement.Q("bubble-layer");
            if (layer == null) Debug.LogError("[SpeechBubbleOverlay] UXML에서 bubble-layer를 찾지 못했습니다.", this);
            bubbles.Clear();   // UIDocument가 화면을 새로 만들었으므로 예전 말풍선은 버린다
            // 플레이어 귀가 있으면 그 귀에 들린 말만, 없으면 모든 말을 받는다.
            if (listener != null) listener.Heard += OnSpoken;
            else SpeechSystem.Spoken += OnSpoken;
        }

        void OnDisable()
        {
            if (listener != null) listener.Heard -= OnSpoken;
            else SpeechSystem.Spoken -= OnSpoken;
        }

        void OnSpoken(SpeechEvent speech)
        {
            Bubble bubble = GetOrCreate(speech.Speaker);
            if (bubble == null) return;
            bubble.Text = speech.Text;
            bubble.HideAt = Time.time + baseDuration + speech.Text.Length * perCharDuration;
        }

        // LateUpdate: 모든 Update(캐릭터 이동)가 끝난 뒤에 불린다. 그래서 말풍선이 캐릭터를 한 프레임 늦게 따라가지 않는다.
        void LateUpdate()
        {
            if (layer == null || layer.panel == null || worldCamera == null) return;

            // 생각 중인 NPC는 말풍선이 없어도 만든다.
            foreach (var speaker in Speaker.All)
                if (speaker.ShowThinking) GetOrCreate(speaker);

            removeBuffer.Clear();
            foreach (var pair in bubbles)
            {
                Speaker speaker = pair.Key;
                Bubble bubble = pair.Value;
                if (speaker == null) { bubble.Label.RemoveFromHierarchy(); removeBuffer.Add(speaker); continue; }

                bool talking = Time.time < bubble.HideAt;
                bool thinking = !talking && speaker.isActiveAndEnabled && speaker.ShowThinking && CanSee(speaker);

                bubble.Label.style.display = talking || thinking ? DisplayStyle.Flex : DisplayStyle.None;
                if (!talking && !thinking) continue;

                bubble.Label.text = talking ? bubble.Text : "...";
                bubble.Label.EnableInClassList("bubble--thinking", thinking);

                // 월드 좌표(머리 위)를 UI 좌표로 바꾼다. 해상도에 맞춰 늘어나는 UI 배율까지 계산해 준다.
                Vector2 p = RuntimePanelUtils.CameraTransformWorldToPanel(layer.panel, speaker.BubbleAnchor, worldCamera);
                bubble.Label.style.left = p.x;
                bubble.Label.style.top = p.y;
            }
            foreach (var dead in removeBuffer) bubbles.Remove(dead);
        }

        bool CanSee(Speaker speaker)
        {
            if (viewer == null) return true;
            var target = speaker.GetComponent<Improv.Characters.Sight.SightTarget>();
            return target == null || viewer.CanSee(target);
        }

        Bubble GetOrCreate(Speaker speaker)
        {
            if (layer == null) return null;
            if (bubbles.TryGetValue(speaker, out Bubble bubble)) return bubble;

            var label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList("bubble");
            label.style.display = DisplayStyle.None;
            layer.Add(label);

            bubble = new Bubble { Label = label, Text = "", HideAt = 0f };
            bubbles.Add(speaker, bubble);
            return bubble;
        }
    }
}
