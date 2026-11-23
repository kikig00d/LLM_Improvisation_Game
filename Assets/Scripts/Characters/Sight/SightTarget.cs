using System.Collections.Generic;
using UnityEngine;

namespace Improv.Characters.Sight
{
    /// <summary>
    /// 눈에 보일 수도, 안 보일 수도 있는 것 (캐릭터, 상자 등. R-011, R-012).
    /// 산장 구조(벽, 바닥, 문)는 늘 보이므로 붙이지 않는다 (R-010).
    /// 판정은 Vision이 하고, 숨기기·보이기 연출은 FogOfWarView가 한다. 이 컴포넌트는 "어디를 보면 되나"만 알려 준다.
    /// </summary>
    public class SightTarget : MonoBehaviour
    {
        // =====================================================================
        // #1. 상태
        // =====================================================================

        public static readonly List<SightTarget> All = new();

        Collider2D body;                 // 크기를 재는 몸. 없으면 그림 크기를 쓴다
        SpriteRenderer[] renderers;      // 숨기고 보일 그림들. Awake 때 한 번만 모은다 (나중에 생기는 강조 테두리는 빼려고)
        readonly Vector2[] points = new Vector2[3];

        public IReadOnlyList<SpriteRenderer> Renderers => renderers;
        public Collider2D Body => body;

        // =====================================================================
        // #2. 유니티 생명주기
        // =====================================================================

        void Awake()
        {
            // 트리거가 아닌 충돌체 = 실제 몸. 문 클릭 영역 같은 트리거는 크기 재는 데 쓰지 않는다.
            foreach (var c in GetComponentsInChildren<Collider2D>())
                if (!c.isTrigger) { body = c; break; }
            renderers = GetComponentsInChildren<SpriteRenderer>();
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        // =====================================================================
        // #3. 볼 곳
        // =====================================================================

        /// <summary>
        /// 이 중 한 점이라도 보이면 보인다고 친다: 발 근처, 가운데, 머리 근처.
        /// 그래서 상자 뒤에 선 사람은 머리가 보이고, 상자 뒤의 상자는 통째로 가려진다 (R-012).
        /// </summary>
        public IReadOnlyList<Vector2> SamplePoints()
        {
            Bounds b = Bounds();
            float inset = Mathf.Min(0.15f, b.extents.y * 0.5f);   // 가장자리에서 조금 안쪽. 바닥 선과 겹쳐 판정이 흔들리지 않게
            points[0] = new Vector2(b.center.x, b.min.y + inset);
            points[1] = b.center;
            points[2] = new Vector2(b.center.x, b.max.y - inset);
            return points;
        }

        public Bounds Bounds()
        {
            if (body != null && body.enabled) return body.bounds;
            if (renderers.Length > 0) return renderers[0].bounds;
            return new Bounds(transform.position, Vector3.one * 0.1f);
        }
    }
}
