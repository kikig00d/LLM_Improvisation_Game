using UnityEngine;
using UnityEngine.UIElements;

namespace Improv.UI
{
    /// <summary>
    /// 마우스가 화면 UI(시계 버튼, 대화창 등) 위에 있는지 알려 준다.
    /// UI 버튼을 누른 클릭이 뒤의 월드(상자 내려놓기 등)까지 눌러 버리지 않게, 월드 클릭 쪽이 먼저 묻는다.
    /// USS 클래스 "ui-solid"가 붙은 요소(또는 그 자식)만 클릭을 가로막는다. 말풍선 판처럼 화면 전체를 덮는 요소는 통과시킨다.
    /// static 클래스: 인스턴스 없이 함수만 모아 둔 것 (C의 전역 함수 모음과 같다).
    /// </summary>
    public static class UiPointer
    {
        public const string SolidClass = "ui-solid";

        /// <param name="screenPosition">화면 픽셀 좌표 (Input System의 Point 값. 원점이 왼쪽 아래)</param>
        public static bool IsOverSolidUI(Vector2 screenPosition)
        {
            foreach (var doc in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                var panel = doc.rootVisualElement?.panel;
                if (panel == null) continue;
                // UI Toolkit 좌표는 원점이 왼쪽 위라서 y를 뒤집은 뒤 패널 좌표로 바꾼다.
                Vector2 p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
                for (VisualElement e = panel.Pick(p); e != null; e = e.parent)
                    if (e.ClassListContains(SolidClass)) return true;
            }
            return false;
        }
    }
}
