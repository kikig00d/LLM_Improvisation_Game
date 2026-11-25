using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Improv.UI
{
    /// <summary>
    /// 관계도 창(T-9)의 화살표만 그리는 판. 사람 칸(노드)과 글자는 그 위에 따로 놓는다.
    /// generateVisualContent: UI Toolkit이 이 요소를 다시 그릴 때 부르는 함수. Painter2D로 선을 긋는다
    /// (C로 치면 그리기 콜백에 함수 포인터를 넘겨 두는 것).
    /// </summary>
    public class RelationGraphElement : VisualElement
    {
        public struct Arrow
        {
            public Vector2 from, to;   // 두 사람 칸의 가운데 (이 요소 안의 좌표)
            public Color color;
            public float side;         // 방향의 오른쪽으로 비켜 긋는 거리. A→B와 B→A가 겹치지 않게
            public float width;
        }

        public readonly List<Arrow> arrows = new();
        public float nodeRadius = 42f;   // 칸 가장자리에서 멈추게

        public RelationGraphElement()
        {
            pickingMode = PickingMode.Ignore;   // 클릭은 칸과 글자가 받는다
            generateVisualContent += Draw;
        }

        /// <summary>
        /// 화살표 위의 한 점(t: 0 = 출발, 1 = 도착). 글자를 여기에 놓는다.
        /// 가운데(0.5)에 놓으면 엇갈리는 선의 글자가 한데 겹쳐서, 출발 쪽에 가깝게 놓아 방향도 함께 드러낸다.
        /// </summary>
        public static Vector2 PointAt(Arrow a, float t, float extraSide = 0f)
        {
            Vector2 dir = (a.to - a.from).normalized;
            Vector2 right = new(-dir.y, dir.x);
            return Vector2.Lerp(a.from, a.to, t) + right * (a.side + extraSide);
        }

        void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            foreach (var a in arrows)
            {
                Vector2 dir = a.to - a.from;
                if (dir.sqrMagnitude < 1f) continue;
                dir.Normalize();
                Vector2 right = new(-dir.y, dir.x);
                Vector2 start = a.from + dir * nodeRadius + right * a.side;
                Vector2 end = a.to - dir * (nodeRadius + 4f) + right * a.side;

                p.strokeColor = a.color;
                p.fillColor = a.color;
                p.lineWidth = a.width;
                p.lineCap = LineCap.Round;
                p.BeginPath();
                p.MoveTo(start);
                p.LineTo(end);
                p.Stroke();

                // 화살촉: 끝점에서 뒤로 물러난 두 점과 삼각형
                const float head = 14f;
                p.BeginPath();
                p.MoveTo(end + dir * 4f);
                p.LineTo(end - dir * head + right * head * 0.5f);
                p.LineTo(end - dir * head - right * head * 0.5f);
                p.ClosePath();
                p.Fill();
            }
        }
    }
}
