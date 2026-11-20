using System.Collections.Generic;
using UnityEngine;

namespace Improv.World
{
    /// <summary>
    /// 월드 좌표 한 점에 있는 상호작용 대상을 찾는다. 클릭과 마우스 올림(호버)이 같은 규칙을 쓰도록 한곳에 둔다.
    /// static 클래스: 객체를 만들지 않고 함수만 모아 둔 것. C의 일반 함수 모음과 같다.
    /// </summary>
    public static class InteractableLookup
    {
        static readonly List<Collider2D> buffer = new();

        public static Interactable FindAt(Vector2 worldPoint)
        {
            // 클릭 판정은 트리거(문 클릭 영역)도 잡아야 하므로 필터 없이 찾는다.
            int count = Physics2D.OverlapPoint(worldPoint, ContactFilter2D.noFilter, buffer);
            for (int i = 0; i < count; i++)
            {
                // 충돌체가 자식 오브젝트에 있어도 부모의 Interactable을 찾는다.
                var interactable = buffer[i].GetComponentInParent<Interactable>();
                if (interactable != null && interactable.enabled) return interactable;
            }
            return null;
        }
    }
}
