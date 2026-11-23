using System.Collections.Generic;
using UnityEngine;

namespace Improv.Characters.Speech
{
    /// <summary>
    /// 캐릭터의 입. 플레이어와 NPC가 같이 쓴다 (P-1).
    /// 플레이어는 대화창이, NPC는 NpcDialogueBrain이 Say()를 부른다.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor))]
    public class Speaker : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [SerializeField] string displayName = "이름 없음";   // 말풍선과 대화 기록에 붙는 이름
        [SerializeField] float mouthHeight = 1.5f;          // 발에서 입까지. 말소리가 여기서 시작된다
        [SerializeField] float bubbleHeight = 2.1f;         // 발에서 말풍선 아래 끝까지

        // =====================================================================
        // #2. 상태
        // =====================================================================

        // 씬에 있는 모든 입. 말풍선 표시가 "생각 중" 상태를 확인할 때 쓴다. Stairs.All과 같은 방식.
        public static readonly List<Speaker> All = new();

        CharacterMotor motor;

        public string DisplayName => displayName;

        /// <summary>NPC는 캐릭터 시트의 이름을 쓰도록 두뇌가 시작할 때 맞춘다.</summary>
        public void SetDisplayName(string newName) => displayName = newName;
        public Vector2 MouthPosition => motor.FeetPosition + Vector2.up * mouthHeight;
        public Vector3 BubbleAnchor => (Vector3)(motor.FeetPosition + Vector2.up * bubbleHeight);

        /// <summary>LLM 답을 오래 기다리는 중이라 머리 위에 "..."를 띄워야 하는지 (E-004). NPC 두뇌가 정한다.</summary>
        public bool ShowThinking { get; set; }

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Awake() => motor = GetComponent<CharacterMotor>();
        void OnEnable() => All.Add(this);
        void OnDisable()
        {
            All.Remove(this);
            ShowThinking = false;
        }

        // =====================================================================
        // #4. 말하기
        // =====================================================================

        public void Say(string text, SpeechVolume volume = SpeechVolume.Normal)
        {
            // 가드 절: 빈 말은 퍼뜨리지 않는다.
            if (string.IsNullOrWhiteSpace(text)) return;
            SpeechSystem.Broadcast(new SpeechEvent(this, text.Trim(), MouthPosition, volume, Time.time));
        }
    }
}
