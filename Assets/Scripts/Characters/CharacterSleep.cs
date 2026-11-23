using System;
using Improv.Characters.Sight;
using Improv.World;
using UnityEngine;

namespace Improv.Characters
{
    /// <summary>
    /// 잠자는 능력 (R-018, R-019). 플레이어와 NPC가 같은 몸으로 잔다 (P-1).
    /// 몸의 능력만 제공한다: 침대에 눕고, 눈을 감고, 일어난다. 언제 자고 깰지는 조종자가 정한다
    /// (플레이어는 PlayerSleepInput, NPC는 나중에 NPC AI).
    /// 자는 동안 귀(Hearing)는 그대로 열려 있다. 그래서 자면서도 주변 말이 들린다 (사용자 결정 2026-11-23).
    /// </summary>
    [RequireComponent(typeof(CharacterMotor))]
    public class CharacterSleep : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [SerializeField] Transform visualRoot;   // 눕힐 그림. 비우면 "Visual" 자식

        // =====================================================================
        // #2. 상태
        // =====================================================================

        CharacterMotor motor;
        Rigidbody2D body;
        Vision vision;                // 없어도 된다. 있으면 자는 동안 눈을 감는다
        CharacterInteractor interactor;
        Quaternion visualRotation;
        Vector3 visualPosition;

        public bool IsAsleep => Bed != null;
        public Bed Bed { get; private set; }

        public event Action FellAsleep;
        public event Action WokeUp;

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            body = GetComponent<Rigidbody2D>();
            vision = GetComponent<Vision>();
            interactor = GetComponent<CharacterInteractor>();
            if (visualRoot == null) visualRoot = transform.Find("Visual");
            if (visualRoot == null)
            {
                Debug.LogError($"[CharacterSleep] '{name}'에 눕힐 그림(Visual)이 없습니다.", this);
                enabled = false;
            }
        }

        // =====================================================================
        // #4. 자고 깨기
        // =====================================================================

        /// <summary>잘 수 있는지 묻기만 한다 (부작용 없음). 침대가 부르는 미리보기에 쓴다.</summary>
        public InteractionResult CanSleepIn(Bed bed)
        {
            if (!enabled || IsAsleep) return InteractionResult.Busy;
            if (bed.Sleeper != null) return InteractionResult.Busy;                       // 누가 이미 자고 있다
            if (interactor != null && interactor.Held != null) return InteractionResult.HandsFull;   // 상자를 든 채 눕지 않는다
            if (motor.IsClimbing) return InteractionResult.Busy;
            return InteractionResult.Done;
        }

        public InteractionResult SleepIn(Bed bed)
        {
            var result = CanSleepIn(bed);
            if (result != InteractionResult.Done) return result;

            Bed = bed;
            bed.Sleeper = this;
            motor.SetMoveInput(Vector2.zero);

            // 침대 가운데로 옮긴다. "층 + x좌표" 모델이라 같은 층의 x만 바꾼다 (y는 그대로 바닥).
            Vector2 p = body.position;
            p.x = bed.SleepX;
            body.position = p;
            transform.position = p;

            // 그림만 눕힌다. 판정용 몸(충돌체, 발 위치)은 그대로라 귀와 눈의 위치 계산이 바뀌지 않는다.
            visualRotation = visualRoot.localRotation;
            visualPosition = visualRoot.localPosition;
            visualRoot.localRotation = Quaternion.Euler(0f, 0f, 90f);
            // 돌린 그림이 실제로 차지하는 사각형을 재서, 그 사각형의 아래 가운데를 침대 위 가운데에 맞춘다.
            // 외형 그림마다 기준점과 크기가 달라도(그림 교체 가능, CLAUDE.md) 침대에 바르게 눕는다.
            if (TryGetVisualBounds(out Bounds lying))
            {
                Vector3 target = bed.LieCenter;
                Vector3 current = new(lying.center.x, lying.min.y, 0f);
                visualRoot.position += target - current;
            }

            if (vision != null) vision.SetEyesClosed(true);
            FellAsleep?.Invoke();
            return InteractionResult.Done;
        }

        // 그림 조각(SpriteRenderer)들을 모두 감싸는 사각형. Bounds.Encapsulate: 사각형을 넓혀 다른 사각형까지 덮는다.
        bool TryGetVisualBounds(out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var r in visualRoot.GetComponentsInChildren<SpriteRenderer>())
            {
                if (r.sprite == null) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any;
        }

        public void WakeUp()
        {
            if (!IsAsleep) return;
            Bed.Sleeper = null;
            Bed = null;
            visualRoot.localRotation = visualRotation;
            visualRoot.localPosition = visualPosition;
            if (vision != null) vision.SetEyesClosed(false);
            WokeUp?.Invoke();
        }
    }
}
