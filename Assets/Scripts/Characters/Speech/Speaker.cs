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

        [Tooltip("CharacterIdentity가 없을 때만 쓰는 이름. 보통은 신원(CharacterIdentity)의 이름을 쓴다 (F-17).")]
        [SerializeField] string displayName = "이름 없음";
        [SerializeField] float mouthHeight = 1.5f;          // 발에서 입까지. 말소리가 여기서 시작된다
        [SerializeField] float bubbleHeight = 2.1f;         // 발에서 말풍선 아래 끝까지

        // =====================================================================
        // #2. 상태
        // =====================================================================

        // 씬에 있는 모든 입. 말풍선 표시가 "생각 중" 상태를 확인할 때 쓴다. Stairs.All과 같은 방식.
        public static readonly List<Speaker> All = new();

        CharacterMotor motor;
        CharacterIdentity identity;   // 이름의 주인. 이름은 바뀔 수 있으니 매번 여기서 읽는다

        /// <summary>말풍선과 대화 기록에 붙는 이름. 말할 때마다 그 순간의 이름이 SpeechEvent.SpeakerName에 담긴다.</summary>
        public string DisplayName => Identity != null ? Identity.DisplayName : displayName;
        // 다른 컴포넌트의 Awake가 내 Awake보다 먼저 불릴 수 있어(NPC 두뇌가 이름을 정할 때), 처음 쓸 때 찾아 둔다.
        public CharacterIdentity Identity
        {
            get
            {
                if (identity == null) identity = GetComponent<CharacterIdentity>();
                return identity;
            }
        }

        /// <summary>NPC는 캐릭터 시트의 이름을 쓰도록 두뇌가 시작할 때 맞춘다. 신원이 있으면 신원의 이름을 바꾼다.</summary>
        public void SetDisplayName(string newName)
        {
            if (Identity != null) Identity.Rename(newName);
            else displayName = newName;
        }
        public Vector2 MouthPosition => motor.FeetPosition + Vector2.up * mouthHeight;
        public Vector3 BubbleAnchor => (Vector3)(motor.FeetPosition + Vector2.up * bubbleHeight);

        /// <summary>LLM 답을 오래 기다리는 중이라 머리 위에 "..."를 띄워야 하는지 (E-004). NPC 두뇌가 정한다.</summary>
        public bool ShowThinking { get; set; }

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Awake()
        {
            motor = GetComponent<CharacterMotor>();
        }
        void OnEnable() => All.Add(this);
        void OnDisable()
        {
            All.Remove(this);
            ShowThinking = false;
        }

        // =====================================================================
        // #4. 말하기
        // =====================================================================

        /// <param name="chainDepth">NPC끼리 이어진 말의 번호 (SpeechEvent.ChainDepth). 플레이어는 늘 0.</param>
        public void Say(string text, SpeechVolume volume = SpeechVolume.Normal, int chainDepth = 0)
        {
            // 가드 절: 빈 말은 퍼뜨리지 않는다.
            if (string.IsNullOrWhiteSpace(text)) return;
            SpeechSystem.Broadcast(new SpeechEvent(this, text.Trim(), MouthPosition, volume, Time.time, chainDepth));
        }
    }
}
