using System;
using System.Collections.Generic;
using Improv.Characters;
using Improv.Npc.Generation;
using UnityEngine;

namespace Improv.Player
{
    /// <summary>
    /// 플레이어 캐릭터가 누구인가 (GDD R-029). 캐릭터 메이킹 창이 채우고, 생성기(CastGenerator)가 NPC를 이 사람에 맞춰 만든다.
    /// 생성기가 쓴 겹치는 과거 중 플레이어의 판본도 여기 둔다. 플레이어만 아는 기억이다 (P-3: NPC는 자기 판본만, 플레이어도 자기 판본만).
    /// 관계도 창의 '나' 칸이 이것을 보여 준다.
    /// </summary>
    [RequireComponent(typeof(CharacterIdentity))]
    public class PlayerPersona : MonoBehaviour
    {
        /// <summary>메이킹 창에 쓰는 중인 답안지.</summary>
        public PersonaDraft Draft { get; } = new();

        /// <summary>다 쓴 사람. null이면 아직 메이킹 중이다.</summary>
        public CastMember Member { get; private set; }
        public bool Ready => Member != null;

        /// <summary>겹치는 과거의 내 판본 (PastFormat.Memory 글).</summary>
        public readonly List<string> memories = new();

        public event Action Confirmed;

        CharacterIdentity identity;

        void Awake() => identity = GetComponent<CharacterIdentity>();

        /// <summary>메이킹을 마친다. 이름표를 바꾸면 단어 사전과 말풍선도 새 이름을 쓴다 (RosterChanged).</summary>
        public void Confirm(CastMember member)
        {
            Member = member;
            identity.Rename(member.name);
            Confirmed?.Invoke();
        }

        public void AddMemory(string text)
        {
            if (!string.IsNullOrWhiteSpace(text)) memories.Add(text);
        }
    }
}
