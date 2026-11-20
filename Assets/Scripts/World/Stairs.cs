using System.Collections.Generic;
using UnityEngine;

namespace Improv.World
{
    /// <summary>
    /// 계단 하나. 아래 끝과 위 끝, 두 점을 잇는 직선 경로다.
    /// 캐릭터는 이 경로를 따라 층을 오간다. (기획서 R-002)
    /// </summary>
    public class Stairs : MonoBehaviour
    {
        // ==========================================
        // #1. 전체 계단 목록
        // ==========================================

        // 씬에 있는 모든 계단을 모아 두는 목록.
        // 캐릭터가 "내 발밑 근처에 계단이 있나?"를 물어볼 때 씬 전체를 뒤지지 않고 여기만 보면 된다.
        // static이라 클래스에 하나만 존재한다. C의 전역 변수와 비슷하지만 이 클래스 안에서만 쓴다.
        static readonly List<Stairs> all = new();

        // 바깥에는 읽기 전용으로만 보여준다. 다른 코드가 실수로 목록을 지우지 못하게.
        public static IReadOnlyList<Stairs> All => all;

        // ==========================================
        // #2. 설정값 (Inspector에서 조정)
        // ==========================================

        // [SerializeField]는 private 변수를 Inspector에 노출한다.
        // public으로 열지 않고도 에디터에서 값을 바꾸고, 씬 파일에 저장할 수 있다.
        [SerializeField] Transform bottom;  // 아래 끝 지점. 캐릭터 발 위치 기준
        [SerializeField] Transform top;     // 위 끝 지점. 캐릭터 발 위치 기준

        // 계단 끝에서 이 범위 안에 발이 있으면 계단에 오를 수 있다.
        [SerializeField] float entryHalfWidth = 0.6f;   // 좌우 허용 거리 (유닛)
        [SerializeField] float entryHalfHeight = 0.3f;  // 위아래 허용 거리 (유닛)

        // ==========================================
        // #3. 바깥에 제공하는 정보
        // ==========================================

        public Vector2 Bottom => bottom.position;
        public Vector2 Top => top.position;
        public float Length => Vector2.Distance(Bottom, Top);

        // t = 0이면 아래 끝, 1이면 위 끝, 0.5면 정확히 중간.
        public Vector2 PointAt(float t) => Vector2.Lerp(Bottom, Top, t);

        public bool IsNearBottom(Vector2 feet) => IsNear(feet, Bottom);
        public bool IsNearTop(Vector2 feet) => IsNear(feet, Top);

        bool IsNear(Vector2 feet, Vector2 end) =>
            Mathf.Abs(feet.x - end.x) <= entryHalfWidth &&
            Mathf.Abs(feet.y - end.y) <= entryHalfHeight;

        // ==========================================
        // #4. 유니티 생명주기
        // ==========================================

        // Awake: 오브젝트가 처음 깨어날 때 한 번. 필수 참조가 비었는지 여기서 검사한다.
        void Awake()
        {
            if (bottom == null || top == null)
            {
                // 두 번째 인자 this를 넘기면 Console에서 로그를 클릭했을 때 이 오브젝트가 선택된다.
                Debug.LogError($"[Stairs] '{name}'의 bottom 또는 top이 비어 있습니다.", this);
                enabled = false;
            }
        }

        // OnEnable / OnDisable: 켜지고 꺼질 때마다 불린다. 목록 등록과 해제를 짝으로 맞춘다.
        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        // Scene 뷰에만 보이는 보조선. 게임 화면에는 안 나온다.
        void OnDrawGizmos()
        {
            if (bottom == null || top == null) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(bottom.position, top.position);
            var entrySize = new Vector3(entryHalfWidth * 2f, entryHalfHeight * 2f, 0f);
            Gizmos.DrawWireCube(bottom.position, entrySize);
            Gizmos.DrawWireCube(top.position, entrySize);
        }
    }
}
