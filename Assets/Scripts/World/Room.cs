using System.Collections.Generic;
using UnityEngine;

namespace Improv.World
{
    /// <summary>
    /// 산장의 방 하나. 이름과 차지하는 사각형 영역만 가진 데이터다.
    /// "누가 어느 방에 있나"는 나중에 시야(같은 방), NPC 기억, LLM 문맥에 쓰인다. (설계 원칙 P-2)
    /// </summary>
    public class Room : MonoBehaviour
    {
        // ==========================================
        // #1. 전체 방 목록
        // ==========================================

        // Stairs와 같은 방식. 켜진 방만 목록에 들어간다.
        static readonly List<Room> all = new();
        public static IReadOnlyList<Room> All => all;

        // ==========================================
        // #2. 설정값
        // ==========================================

        [SerializeField] string displayName = "방";              // 화면과 로그, 나중에 LLM 문맥에 쓰일 이름
        [SerializeField] Vector2 size = new(4f, 3f);             // 방의 가로, 세로 (유닛). 위치는 Transform 중심

        // ==========================================
        // #3. 바깥에 제공하는 정보
        // ==========================================

        public string DisplayName => displayName;

        // Rect는 왼쪽 아래 모서리와 크기로 정의하는 사각형. 중심에서 절반 크기를 빼서 모서리를 구한다.
        public Rect Bounds => new((Vector2)transform.position - size * 0.5f, size);

        public bool Contains(Vector2 point) => Bounds.Contains(point);

        // 주어진 위치를 포함하는 방을 찾는다. 없으면 null.
        public static Room FindAt(Vector2 point)
        {
            foreach (var room in all)
            {
                if (room.Contains(point)) return room;
            }
            return null;
        }

        // ==========================================
        // #4. 유니티 생명주기
        // ==========================================

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.8f);
            Gizmos.DrawWireCube(transform.position, size);
        }
    }
}
