using System.Collections.Generic;
using UnityEngine;

namespace Improv.World
{
    /// <summary>
    /// 시선을 막는 판(층 사이 바닥) (R-012).
    /// 바닥에는 충돌체가 없다. 충돌체를 붙이면 이동·손·소리 판정까지 바뀌므로, 시야만 쓰는 사각형을 따로 둔다.
    /// 사각형은 붙어 있는 그림(SpriteRenderer)의 크기를 그대로 쓴다. 계단 구멍에는 바닥 그림이 없으니 그 위로는 보인다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class SightBlocker : MonoBehaviour
    {
        public static readonly List<SightBlocker> All = new();

        SpriteRenderer spriteRenderer;

        /// <summary>월드 좌표의 사각형. 바닥은 움직이지 않지만, 그림을 옮겨도 맞도록 매번 그림에서 읽는다.</summary>
        public Rect WorldRect
        {
            get
            {
                Bounds b = spriteRenderer.bounds;
                return new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
            }
        }

        void Awake() => spriteRenderer = GetComponent<SpriteRenderer>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>
        /// origin에서 dir(길이 1) 방향으로 쏜 선이 이 사각형에 처음 닿는 거리. 안 닿으면 float.PositiveInfinity.
        /// "슬랩(slab) 방식": x 범위와 y 범위 각각에 들어가는 구간을 구해 겹치는 부분을 찾는다.
        /// </summary>
        public float RayDistance(Vector2 origin, Vector2 dir)
        {
            Rect r = WorldRect;
            float tMin = 0f, tMax = float.PositiveInfinity;
            if (!Slab(origin.x, dir.x, r.xMin, r.xMax, ref tMin, ref tMax)) return float.PositiveInfinity;
            if (!Slab(origin.y, dir.y, r.yMin, r.yMax, ref tMin, ref tMax)) return float.PositiveInfinity;
            return tMin;
        }

        static bool Slab(float o, float d, float min, float max, ref float tMin, ref float tMax)
        {
            if (Mathf.Abs(d) < 1e-6f) return o >= min && o <= max;   // 축과 나란한 선: 처음부터 안에 있어야 닿는다
            float t1 = (min - o) / d, t2 = (max - o) / d;
            if (t1 > t2) (t1, t2) = (t2, t1);                          // 튜플로 두 값 바꾸기 (C의 swap)
            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            return tMin <= tMax;
        }
    }
}
