using System.Collections.Generic;
using Improv.World;
using UnityEngine;

namespace Improv.Characters.Sight
{
    /// <summary>
    /// 캐릭터의 눈. 플레이어와 NPC가 같은 규칙으로 본다 (P-1, R-003, R-011~R-012, R-011a).
    /// - 시선은 바라볼 곳을 향해 정해진 속도로만 돈다 (R-003).
    /// - 시선 앞 부채꼴(거리 O-002, 시야각 O-002) 안이고 벽·큰 오브젝트에 가리지 않으면 보인다.
    /// - 등 뒤는 안 보이고, 몸 바로 뒤(뒤통수 정도)만 보인다 (R-011a).
    /// 플레이어 화면의 안개(FogOfWarView)도, NPC가 LLM에 넘기는 "보이는 사람"도 이 눈의 판정만 쓴다 (P-2, P-3).
    /// </summary>
    [RequireComponent(typeof(CharacterMotor))]
    public class Vision : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [SerializeField] float viewDistance = 8f;        // 시야 거리 (유닛). O-002 [확정 2026-11-23]
        [SerializeField] float fieldOfView = 120f;       // 시야각 (도). O-002 [확정 2026-11-23]
        [SerializeField] float turnSpeed = 180f;         // 시선이 도는 최대 속도 (도/초). O-002 [가정]
        [SerializeField] float backRadius = 0.6f;        // 등 뒤에서도 보이는 거리 (유닛). R-011a "뒤통수 정도" [가정]
        [SerializeField] float eyeHeight = 1.5f;         // 발에서 눈까지

        [Header("멀리 보기 (R-004, O-003 [가정])")]
        [SerializeField] float zoomDistanceMultiplier = 1.5f;   // 멀리 볼 때 시야 거리 배율
        [SerializeField] float zoomFovMultiplier = 0.5f;        // 멀리 볼 때 시야각 배율 (망원경처럼 좁아진다)

        [Header("가림 (R-012)")]
        [Tooltip("바닥에 놓인 운반 오브젝트가 이 높이 이상이면 뒤를 가린다")]
        [SerializeField] float minOccluderHeight = 0.5f;

        // =====================================================================
        // #2. 상태
        // =====================================================================

        public static readonly List<Vision> All = new();

        CharacterMotor motor;
        CharacterAnimator characterAnimator;   // 바라볼 곳이 없을 때는 몸이 향한 쪽을 본다
        SightTarget self;                      // 내 몸은 내가 가리지도, 내 목록에 들지도 않는다

        float gazeAngle;          // 지금 시선 각도 (도). 0 = 오른쪽, 90 = 위, 180 = 왼쪽
        Vector2? lookTarget;      // 바라볼 곳. Vector2? 는 "값이 없을 수도 있는 Vector2" (C로 치면 bool has + 값 한 쌍)
        bool zoomed;
        bool eyesClosed;          // 자는 중 (R-018). 감으면 아무것도 안 보인다

        readonly List<RaycastHit2D> hitBuffer = new();
        ContactFilter2D solidOnly;

        public Vector2 EyePosition => motor.FeetPosition + Vector2.up * eyeHeight;
        public Vector2 GazeDirection => new(Mathf.Cos(gazeAngle * Mathf.Deg2Rad), Mathf.Sin(gazeAngle * Mathf.Deg2Rad));
        public float GazeAngle => gazeAngle;
        public bool HasLookTarget => lookTarget.HasValue;
        public bool IsZoomed => zoomed;
        public float ViewDistance => viewDistance * (zoomed ? zoomDistanceMultiplier : 1f);
        public float FieldOfView => fieldOfView * (zoomed ? zoomFovMultiplier : 1f);
        public float BackRadius => backRadius;
        public bool EyesClosed => eyesClosed;

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            characterAnimator = GetComponent<CharacterAnimator>();
            self = GetComponent<SightTarget>();
            solidOnly = ContactFilter2D.noFilter;
            solidOnly.useTriggers = false;   // 문 클릭 영역 같은 트리거는 눈을 막지 않는다
        }

        void Start() => gazeAngle = DesiredAngle();   // 처음에는 돌지 않고 바로 그쪽을 본다

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Update()
        {
            // MoveTowardsAngle: 359도 → 1도처럼 0을 넘어가도 짧은 쪽으로 돈다. 한 프레임에 turnSpeed × 시간만큼만.
            gazeAngle = Mathf.MoveTowardsAngle(gazeAngle, DesiredAngle(), turnSpeed * Time.deltaTime);
            gazeAngle = Mathf.Repeat(gazeAngle, 360f);
        }

        // =====================================================================
        // #4. 조종자가 부르는 것 (몸의 능력만 제공한다. 어디를 볼지는 조종자가 정한다)
        // =====================================================================

        /// <summary>한 점을 바라본다. 플레이어는 마우스 커서를 매 프레임 넘긴다.</summary>
        public void LookAt(Vector2 worldPoint) => lookTarget = worldPoint;

        /// <summary>바라볼 곳을 잊는다. 그 뒤로는 몸이 향한 쪽을 본다.</summary>
        public void ClearLookTarget() => lookTarget = null;

        /// <summary>멀리 보기 (R-004). 시야가 길고 좁아진다.</summary>
        public void SetZoomed(bool on) => zoomed = on;

        /// <summary>눈을 감는다 (잠, R-018). 감은 동안은 내 몸 말고는 아무것도 보이지 않는다. 안개도 화면 전체를 덮는다.</summary>
        public void SetEyesClosed(bool closed) => eyesClosed = closed;

        float DesiredAngle()
        {
            if (lookTarget.HasValue)
            {
                Vector2 d = lookTarget.Value - EyePosition;
                if (d.sqrMagnitude > 0.0001f) return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                return gazeAngle;
            }
            bool right = characterAnimator == null || characterAnimator.FacingRight;
            return right ? 0f : 180f;
        }

        // =====================================================================
        // #5. 판정 (아무것도 바꾸지 않는 미리보기)
        // =====================================================================

        /// <summary>그 대상이 지금 보이는가. 볼 곳(발·가운데·머리) 중 하나라도 보이면 보인다.</summary>
        public bool CanSee(SightTarget target)
        {
            if (target == null || !target.isActiveAndEnabled) return false;
            if (target == self) return true;
            if (eyesClosed) return false;
            foreach (Vector2 p in target.SamplePoints())
                if (CanSeePoint(p, target)) return true;
            return false;
        }

        /// <summary>월드의 한 점이 보이는가. ignore는 가림 판정에서 뺄 대상(보려는 대상 자신).</summary>
        public bool CanSeePoint(Vector2 point, SightTarget ignore = null)
        {
            if (eyesClosed) return false;
            Vector2 eye = EyePosition;
            Vector2 toPoint = point - eye;
            float distance = toPoint.magnitude;

            if (distance > backRadius)
            {
                if (distance > ViewDistance) return false;
                // Vector2.Angle: 두 방향 사이의 각도(0~180). 시야각의 절반 안이면 부채꼴 안이다.
                if (Vector2.Angle(GazeDirection, toPoint) > FieldOfView * 0.5f) return false;
            }
            return !IsBlocked(eye, point, ignore);
        }

        /// <summary>눈에서 dir 방향으로 얼마나 멀리까지 보이나 (안개 그림용). maxDistance를 넘지 않는다.</summary>
        public float VisibleDistance(Vector2 dir, float maxDistance)
        {
            if (eyesClosed) return 0f;   // 감은 눈: 안개가 눈 위치까지 덮는다
            Vector2 eye = EyePosition;
            float nearest = maxDistance;
            int count = Physics2D.Raycast(eye, dir, solidOnly, hitBuffer, maxDistance);
            for (int i = 0; i < count; i++)
                if (Blocks(hitBuffer[i].collider, null) && hitBuffer[i].distance < nearest)
                    nearest = hitBuffer[i].distance;
            foreach (var slab in SightBlocker.All)
                nearest = Mathf.Min(nearest, slab.RayDistance(eye, dir));
            return nearest;
        }

        /// <summary>지금 보이는 대상들. NPC가 LLM에 "보이는 사람"을 넘길 때 쓴다 (P-3).</summary>
        public void CollectVisible(List<SightTarget> result)
        {
            result.Clear();
            foreach (var target in SightTarget.All)
                if (target != self && CanSee(target)) result.Add(target);
        }

        bool IsBlocked(Vector2 from, Vector2 to, SightTarget ignore)
        {
            // 층 사이 바닥: 눈과 점 사이 선분이 바닥판을 지나면 가린다. 계단 구멍에는 바닥판이 없다.
            Vector2 d = to - from;
            float length = d.magnitude;
            if (length < 0.0001f) return false;
            Vector2 dir = d / length;
            foreach (var slab in SightBlocker.All)
                if (slab.RayDistance(from, dir) < length) return true;

            // 벽, 닫힌 문, 바닥에 놓인 큰 오브젝트 (R-012)
            int count = Physics2D.Linecast(from, to, solidOnly, hitBuffer);
            for (int i = 0; i < count; i++)
                if (Blocks(hitBuffer[i].collider, ignore)) return true;
            return false;
        }

        bool Blocks(Collider2D c, SightTarget ignore)
        {
            if (ignore != null && c.transform.IsChildOf(ignore.transform)) return false;   // 보려는 대상 자신
            if (c.GetComponentInParent<CharacterMotor>() != null) return false;            // 사람은 시야를 가리지 않는다
            if (c.GetComponentInParent<SeeThrough>() != null) return false;                // 난간 등
            var carryable = c.GetComponentInParent<Carryable>();
            if (carryable != null)
            {
                // 들려 있거나 옮겨지는 중이면 가리지 않는다. 바닥에 놓인 큰 것만 가린다.
                if (carryable.Holder != null || carryable.IsTweening) return false;
                return c.bounds.size.y >= minOccluderHeight;
            }
            return true;   // 벽, 닫힌 문 (문은 열리면 막는 충돌체가 꺼진다)
        }
    }
}
