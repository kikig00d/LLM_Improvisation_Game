using System;
using System.Collections.Generic;
using UnityEngine;

namespace Improv.Characters
{
    /// <summary>
    /// 캐릭터의 신원 (계획서 F-17). 진짜 신원은 바뀌지 않는 번호(id)이고, 이름은 언제든 바뀔 수 있는 꼬리표다.
    /// - 개명: Rename()으로 바꾸면 말풍선, 대화 기록, 플레이어 단어 사전이 모두 새 이름을 따른다.
    /// - 동명이인: 이름이 같아도 id가 다르면 다른 사람이다. 코드는 사람을 id로 구분한다.
    ///   NPC는 id를 모르고 이름만 듣는다(P-3). 그래서 동명이인이면 처음엔 헷갈리고, 대화 맥락으로 구분해 가야 한다.
    /// C로 치면 구조체의 고유 키(int id)와 표시용 문자열(char* name)을 나눠 둔 것이다.
    /// </summary>
    public class CharacterIdentity : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [Tooltip("캐릭터 고유 번호. 씬 안에서 겹치면 안 된다. 플레이어 0, NPC 1부터.")]
        [SerializeField] int id;
        [Tooltip("처음 이름. NPC는 시작할 때 캐릭터 시트의 이름으로 바뀐다.")]
        [SerializeField] string displayName = "이름 없음";

        // =====================================================================
        // #2. 상태
        // =====================================================================

        /// <summary>씬에 있는 모든 신원. 단어 사전이 지금 이름들을 모을 때 쓴다.</summary>
        public static readonly List<CharacterIdentity> All = new();

        /// <summary>누가 나타나거나 사라지거나 이름이 바뀌면 알린다. static event: 특정 캐릭터가 아니라 "명단 전체"의 변화.</summary>
        public static event Action RosterChanged;

        public int Id => id;
        public string DisplayName => displayName;

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void OnEnable()
        {
            foreach (var other in All)
                if (other.id == id) Debug.LogError($"[CharacterIdentity] '{name}'와 '{other.name}'의 id가 {id}로 같습니다. id는 겹치면 안 됩니다.", this);
            All.Add(this);
            RosterChanged?.Invoke();
        }

        void OnDisable()
        {
            All.Remove(this);
            RosterChanged?.Invoke();
        }

        // =====================================================================
        // #4. 이름 바꾸기
        // =====================================================================

        /// <summary>이름을 바꾼다 (개명). 같은 이름이 이미 있어도 막지 않는다. 동명이인도 게임의 일부다.</summary>
        public void Rename(string newName)
        {
            if (string.IsNullOrWhiteSpace(newName) || newName == displayName) return;
            displayName = newName.Trim();
            RosterChanged?.Invoke();
        }

        /// <summary>id로 찾는다. 없으면 null.</summary>
        public static CharacterIdentity Find(int id)
        {
            foreach (var c in All) if (c.id == id) return c;
            return null;
        }
    }
}
