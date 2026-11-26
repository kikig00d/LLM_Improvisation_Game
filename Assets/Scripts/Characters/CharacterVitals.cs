using System;
using System.Collections.Generic;
using Improv.Characters.Sight;
using Improv.Characters.Speech;
using Improv.World;
using UnityEngine;

namespace Improv.Characters
{
    /// <summary>
    /// 상태바 (R-020~R-022, R-008). 플레이어와 NPC가 같은 몸으로 가진다 (P-1).
    /// 상위 바: 건강. 하위 바: 배고픔(100 = 배부름), 스테미너, 체온, 스트레스(0 = 편안).
    /// - 배고픔은 시간이 지나면 준다. 음식(Food)을 먹으면 찬다.
    /// - 스테미너는 걸으면 줄고, 서 있으면 천천히, 자면 빨리 찬다. 무언가를 들고 있으면 더 빨리 준다 (R-008).
    /// - 건강은 하위 바가 0인 동안 줄고, 모두 괜찮으면 천천히 회복한다. 0이 되면 죽는다 (R-022).
    /// - 체온과 스트레스는 아직 원인이 되는 시스템(바깥, 불, 사건)이 없어 고정이다 (사용자 결정 2026-11-25).
    /// 값은 모두 게임 시간(Time.deltaTime) 기준이라 멈추면 그대로, 빨리 감으면 빨리 변한다 (GameClock).
    /// </summary>
    [RequireComponent(typeof(CharacterMotor))]
    public class CharacterVitals : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값 (O-005 [가정])
        // =====================================================================

        public const float Max = 100f;

        [Header("배고픔")]
        [Tooltip("게임 하루에 줄어드는 양. 40이면 아무것도 안 먹을 때 약 2.5일 버틴다.")]
        [SerializeField] float hungerPerDay = 40f;

        [Header("스테미너 (게임 초당)")]
        [SerializeField] float staminaWalkCost = 1.0f;     // 걷는 동안 줄어드는 양
        [SerializeField] float staminaRestGain = 0.3f;     // 서 있는 동안 차는 양
        [SerializeField] float staminaSleepGain = 1.5f;    // 자는 동안 차는 양
        [Tooltip("무언가를 들고 걸으면 이 배수만큼 더 빨리 준다 (R-008). 들고 서 있으면 차지 않는다.")]
        [SerializeField] float carryCostMultiplier = 2f;
        [Tooltip("무언가를 들고 걸을 때 보통 사람(근력 10, 운반 0)의 속도 배율 (R-008)")]
        [SerializeField] float carrySpeedMultiplier = 0.75f;

        [Header("능력치의 효과 (R-031a, O-018 [가정]). 보통(10)에서 1점마다")]
        [SerializeField] float endurancePerPoint = 0.04f;   // 체력: 스테미너가 덜 줄고 더 빨리 찬다
        [SerializeField] float healthPerPoint = 0.05f;      // 건강: 빈 바의 피해를 덜 받고 더 빨리 회복한다
        [SerializeField] float carryStrengthPerPoint = 0.05f;   // 근력: 들고 걸을 때 더 빠르고 덜 지친다
        [SerializeField] float carrySkillPerLevel = 0.1f;       // 운반 기능 1마다

        [Header("건강")]
        [Tooltip("0인 하위 바 하나당 게임 하루에 줄어드는 건강")]
        [SerializeField] float damagePerEmptyBarPerDay = 40f;
        [Tooltip("하위 바가 모두 이 값 위면 건강이 천천히 회복한다")]
        [SerializeField] float healthyThreshold = 30f;
        [SerializeField] float healthRegenPerDay = 10f;

        [Header("처음 값")]
        [SerializeField, Range(0, 100)] float startHealth = 100f;
        [SerializeField, Range(0, 100)] float startHunger = 80f;
        [SerializeField, Range(0, 100)] float startStamina = 100f;
        [SerializeField, Range(0, 100)] float startTemperature = 100f;   // 100 = 따뜻하다 (지금은 고정)
        [SerializeField, Range(0, 100)] float startStress = 0f;          // 0 = 편안하다 (지금은 고정)

        const float WalkSpeedThreshold = 0.1f;   // 이보다 빠르면 걷는 중으로 본다
        const float DefaultDayLength = 1200f;    // 시계가 없을 때의 하루 길이

        // =====================================================================
        // #2. 상태
        // =====================================================================

        CharacterMotor motor;
        CharacterInteractor interactor;   // 없어도 된다. 있으면 들고 있는지 본다
        CharacterSleep sleep;             // 없어도 된다. 있으면 자는지 본다
        CharacterAbilities abilities;     // 없어도 된다. 없으면 보통 사람(모두 10)으로 본다

        public float Health { get; private set; }
        public float Hunger { get; private set; }
        public float Stamina { get; private set; }
        public float Temperature { get; private set; }
        public float Stress { get; private set; }
        public bool IsDead { get; private set; }

        /// <summary>건강이 0이 되어 죽은 순간 한 번 알린다. 조종자(플레이어 입력, NPC 두뇌)가 듣고 멈춘다.</summary>
        public event Action Died;

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            interactor = GetComponent<CharacterInteractor>();
            sleep = GetComponent<CharacterSleep>();
            abilities = GetComponent<CharacterAbilities>();
            Health = startHealth;
            Hunger = startHunger;
            Stamina = startStamina;
            Temperature = startTemperature;
            Stress = startStress;
        }

        void Update()
        {
            if (IsDead) return;
            if (Health <= 0f) { Die(); return; }   // 바깥(디버그 등)에서 0으로 만든 경우. 회복이 먼저 끼어들지 않게 맨 앞에서 본다
            float dt = Time.deltaTime;   // 게임 초
            float day = GameClock.Instance != null ? GameClock.Instance.DayLengthSeconds : DefaultDayLength;

            // 배고픔
            Hunger = Mathf.Clamp(Hunger - hungerPerDay / day * dt, 0f, Max);

            // 스테미너
            bool carrying = interactor != null && interactor.Held != null;
            bool asleep = sleep != null && sleep.IsAsleep;
            bool walking = !asleep && motor.Velocity.magnitude > WalkSpeedThreshold;
            // R-031a: 체력이 높으면 덜 지치고 빨리 찬다. 근력·운반이 높으면 들고 걸어도 덜 지치고 덜 느려진다.
            float endurance = AbilityFactor(Ability.Endurance, endurancePerPoint);
            float carryAbility = abilities != null ? abilities.Factor(Ability.Strength, carryStrengthPerPoint, "운반", carrySkillPerLevel) : 1f;
            float carryCost = 1f + (carryCostMultiplier - 1f) / carryAbility;   // 보통 2배, 장사는 1.5배쯤
            motor.SpeedMultiplier = carrying ? Mathf.Clamp(carrySpeedMultiplier * carryAbility, 0.3f, 1f) : 1f;
            float staminaRate;
            if (asleep) staminaRate = staminaSleepGain * endurance;
            else if (walking) staminaRate = -staminaWalkCost * (carrying ? carryCost : 1f) / endurance;
            else staminaRate = carrying ? 0f : staminaRestGain * endurance;
            Stamina = Mathf.Clamp(Stamina + staminaRate * dt, 0f, Max);

            // 건강: 빈 하위 바(0)가 있으면 줄고, 모두 넉넉하면 회복한다
            int emptyBars = (Hunger <= 0f ? 1 : 0) + (Stamina <= 0f ? 1 : 0) + (Temperature <= 0f ? 1 : 0);
            bool healthy = Hunger > healthyThreshold && Stamina > healthyThreshold && Temperature > healthyThreshold;
            float constitution = AbilityFactor(Ability.Health, healthPerPoint);   // 건장한 노인은 굶어도 덜 상한다
            float healthRate = -emptyBars * damagePerEmptyBarPerDay / constitution + (healthy ? healthRegenPerDay * constitution : 0f);
            Health = Mathf.Clamp(Health + healthRate / day * dt, 0f, Max);

            if (Health <= 0f) Die();
        }

        float AbilityFactor(Ability a, float perPoint) => abilities != null ? abilities.Factor(a, perPoint) : 1f;

        // =====================================================================
        // #4. 바꾸기
        // =====================================================================

        /// <summary>들고 있는 것을 먹는다. 음식이 아니면 먹지 않고 이유를 돌려준다.</summary>
        public InteractionResult TryEatHeld()
        {
            if (IsDead) return InteractionResult.Busy;
            var item = interactor != null ? interactor.Held : null;
            if (item == null) return InteractionResult.NothingHeld;
            var food = item.GetComponent<Food>();
            if (food == null) return InteractionResult.NotEdible;
            if (item.IsTweening) return InteractionResult.Busy;

            Hunger = Mathf.Clamp(Hunger + food.Nutrition, 0f, Max);
            interactor.ConsumeHeld();   // 손에서 없애고 오브젝트도 사라진다
            Debug.Log($"[상태바] {name}이(가) {item.DisplayName}을(를) 먹음. 배고픔 {Hunger:0}", this);
            return InteractionResult.Done;
        }

        /// <summary>테스트용: 값을 바로 바꾼다.</summary>
        public void DebugSet(float? health = null, float? hunger = null, float? stamina = null)
        {
            if (health.HasValue) Health = Mathf.Clamp(health.Value, 0f, Max);
            if (hunger.HasValue) Hunger = Mathf.Clamp(hunger.Value, 0f, Max);
            if (stamina.HasValue) Stamina = Mathf.Clamp(stamina.Value, 0f, Max);
        }

        void Die()
        {
            if (IsDead) return;
            IsDead = true;
            Health = 0f;

            // 몸의 일: 쓰러지고, 보고 듣는 것을 멈춘다. 무엇을 할지(패배 화면, 두뇌 멈춤)는 조종자가 Died를 듣고 정한다.
            motor.SetMoveInput(Vector2.zero);
            if (sleep != null && sleep.IsAsleep) sleep.WakeUp();
            var vision = GetComponent<Vision>();
            if (vision != null) vision.SetEyesClosed(true);
            var hearing = GetComponent<Hearing>();
            if (hearing != null) hearing.enabled = false;
            var visual = transform.Find("Visual");
            if (visual != null) LieDown(visual);

            Debug.Log($"[상태바] {name} 사망 (R-022)", this);
            Died?.Invoke();
        }

        // 그림을 90도 눕히고, 누운 그림의 아래 끝이 바닥(발 높이)에 닿게 내린다. 그대로 돌리면 공중에 뜬다.
        // 판정용 몸(충돌체, 발 위치)은 그대로 둔다 (CharacterSleep과 같은 방식).
        void LieDown(Transform visual)
        {
            visual.localRotation = Quaternion.Euler(0f, 0f, 90f);
            bool any = false;
            Bounds b = default;
            foreach (var r in visual.GetComponentsInChildren<SpriteRenderer>())
            {
                if (r.sprite == null) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            if (any) visual.position += Vector3.up * (motor.FeetPosition.y - b.min.y);
        }

        // =====================================================================
        // #5. 느낌 (NPC가 LLM에 넘길 때, P-3)
        // =====================================================================

        /// <summary>
        /// 몸 상태를 숫자가 아니라 느낌 말로 바꾼다. 사람도 자기 배고픔을 "73점"으로 느끼지 않는다.
        /// 괜찮은 바는 말하지 않는다. 모두 괜찮으면 빈 목록.
        /// </summary>
        public void DescribeFeelings(List<string> result)
        {
            result.Clear();
            if (Hunger < 10f) result.Add("배가 너무 고파 힘이 없다");
            else if (Hunger < 30f) result.Add("배가 고프다");
            if (Stamina < 10f) result.Add("너무 지쳐 쓰러질 것 같다");
            else if (Stamina < 30f) result.Add("피곤하다");
            if (Temperature < 30f) result.Add("춥다");
            if (Stress > 70f) result.Add("신경이 곤두서 있다");
            if (Health < 30f) result.Add("몸이 많이 안 좋다");
            else if (Health < 60f) result.Add("몸이 좀 안 좋다");
        }
    }
}
