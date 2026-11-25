using System;
using System.Collections.Generic;
using System.Text;
using Improv.Characters;
using Improv.Characters.Sight;
using Improv.Characters.Speech;
using Improv.World;
using UnityEngine;

namespace Improv.Npc
{
    /// <summary>
    /// NPC의 행동 실행자 (계획서 T-6). LLM이 고른 행동(가기, 줍기, 내려놓기, 먹기, 자기)을 같은 몸·같은 손으로 해낸다.
    /// - 무엇을 할지는 LLM이 고른다(R-016b). 여기는 "어떻게"만 한다: 걸어가고, 줍고, 먹는다.
    /// - 갈 수 있는 곳은 NPC가 인지한 것뿐이다 (P-3, 사용자 결정 2026-11-25):
    ///   방 이름과 침대(산장에 사니 안다), 지금 보이는 사람·물건, 그리고 전에 본 자리를 기억하는 사람·물건.
    ///   안 본 사과를 신기하게 찾아가지 않는다. 기억한 자리에 가 보니 없으면 그 기억은 지워지고, 그 결과가 다음 판단에 들어간다.
    /// - 행동이 끝나면(성공이든 실패든) ActionFinished로 두뇌에 알린다. 두뇌가 다음 할 일을 다시 생각한다.
    /// </summary>
    [RequireComponent(typeof(NpcNavigator), typeof(CharacterInteractor))]
    public class NpcActions : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [Tooltip("보이는 것을 기억에 새기는 간격 (게임 초)")]
        [SerializeField] float lookInterval = 0.3f;
        [Tooltip("사람에게 갈 때 이만큼 떨어져 멈춘다")]
        [SerializeField] float personStopDistance = 1.2f;
        [Tooltip("물건을 집으러 갈 때 이만큼 떨어져 멈춘다 (손이 닿는 거리 1.5보다 작게)")]
        [SerializeField] float itemStopDistance = 0.7f;
        [Tooltip("방 경계를 이만큼 넘어 들어서면 그 방에 도착한 것으로 본다 (문간에 서서 길을 막지 않게)")]
        [SerializeField] float roomEnterMargin = 0.4f;

        // 행동 이름. LLM이 JSON의 action에 이 중 하나를 쓴다.
        public const string Go = "가기", PickUp = "줍기", Drop = "내려놓기", Eat = "먹기", Sleep = "자기", None = "없음";
        public const string BedName = "침대";

        // =====================================================================
        // #2. 상태
        // =====================================================================

        // 본 것의 기억 한 칸. class라서 사전 안의 값을 그대로 고쳐 쓸 수 있다.
        class Seen
        {
            public string name;
            public Vector2 position;
            public bool isPerson;
            public SightTarget target;
        }

        enum Step { None, Going, PickingUp, Eating, Chewing, Sleeping }   // Chewing: 주운 음식을 입에 가져가는 중(연출 대기)

        NpcNavigator navigator;
        CharacterInteractor interactor;
        CharacterMotor motor;
        Vision vision;              // 없으면 아무것도 기억하지 못한다
        CharacterVitals vitals;     // 먹기에 쓴다
        CharacterSleep sleep;       // 자기에 쓴다
        Speaker speaker;

        readonly Dictionary<SightTarget, Seen> memory = new();
        readonly List<SightTarget> visibleBuffer = new();
        readonly List<SightTarget> forgetBuffer = new();
        float lookTimer;

        Step step;
        string stepLabel;           // "거실로 가는 중" 같은 지금 하는 일
        Seen itemTarget;            // 줍기·먹기의 대상 물건
        Bed bedTarget;
        bool eatAfterPickUp;
        bool starting;              // Execute 안에서 바로 끝난 행동인가 (이미 거기 있음, 어디 있는지 모름 등)
        Room goingRoom;             // 방으로 가는 중이면 그 방. 도착은 "그 방에 들어섰다"로 판정한다
        string leftRoom;            // 출발할 때 있던 방 이름 ("거실에서 나와 개인실에 들어왔다")

        /// <summary>방금 끝난 행동의 결과. 다음 LLM 판단에 "방금 한 일"로 들어간다.</summary>
        public string LastResult { get; private set; }
        public bool IsBusy => step != Step.None;
        public string CurrentLabel => step == Step.None ? null : stepLabel;

        /// <summary>행동이 끝나면 결과 문장과 함께 알린다. 두뇌가 다음 할 일을 생각한다.</summary>
        public event Action<string> ActionFinished;

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Awake()
        {
            navigator = GetComponent<NpcNavigator>();
            interactor = GetComponent<CharacterInteractor>();
            motor = GetComponent<CharacterMotor>();
            vision = GetComponent<Vision>();
            vitals = GetComponent<CharacterVitals>();
            sleep = GetComponent<CharacterSleep>();
            speaker = GetComponent<Speaker>();
        }

        void OnDisable()
        {
            navigator.Stop();
            step = Step.None;
        }

        void Update()
        {
            lookTimer -= Time.deltaTime;
            if (lookTimer <= 0f) { lookTimer = lookInterval; Look(); }

            // 자는 중이면 아침에 저절로 깬다 (플레이어와 같은 기상 시각, R-018).
            if (sleep != null && sleep.IsAsleep && GameClock.Instance != null)
            {
                float h = GameClock.Instance.HourOfDay;
                if (h >= GameClock.Instance.WakeHour && h < GameClock.Instance.WakeHour + 1f) sleep.WakeUp();
            }

            if (step == Step.None || step == Step.Chewing) return;   // Chewing은 RetryEat가 마무리한다
            if (navigator.Status == NpcNavigator.State.Failed) { Finish($"{stepLabel}였지만 {navigator.FailReason}"); return; }
            if (navigator.Status != NpcNavigator.State.Arrived) return;

            // 도착했다. 단계에 따라 마무리한다.
            switch (step)
            {
                case Step.Going:
                    if (goingRoom != null)
                        Finish(leftRoom != null ? $"{leftRoom}에서 나와 {goingRoom.DisplayName}에 들어왔다" : $"{goingRoom.DisplayName}에 들어왔다");
                    else
                        Finish($"{Destination()}에 도착했다");
                    break;
                case Step.PickingUp:
                case Step.Eating:
                    FinishPickUp();
                    break;
                case Step.Sleeping:
                    var result = sleep != null ? sleep.SleepIn(bedTarget) : InteractionResult.Busy;
                    Finish(result == InteractionResult.Done ? "침대에 누워 잠들었다" : "침대에 누우려 했지만 누울 수 없었다");
                    break;
            }
        }

        // =====================================================================
        // #4. 보기와 기억 (P-3)
        // =====================================================================

        void Look()
        {
            if (vision == null) return;
            vision.CollectVisible(visibleBuffer);
            foreach (var t in visibleBuffer)
            {
                string n = NameOf(t, out bool isPerson);
                if (n == null) continue;
                if (!memory.TryGetValue(t, out var seen)) memory[t] = seen = new Seen { target = t };
                seen.name = n;
                seen.isPerson = isPerson;
                seen.position = t.transform.position;
            }

            // 기억한 자리가 지금 보이는데 그것이 없으면(누가 가져갔거나 먹었으면) 잊는다.
            forgetBuffer.Clear();
            foreach (var kv in memory)
            {
                if (kv.Key == null) { forgetBuffer.Add(kv.Key); continue; }   // 사라진 물건(먹힘 등)
                if (visibleBuffer.Contains(kv.Key)) continue;
                if (vision.CanSeePoint(kv.Value.position + Vector2.up * 0.2f)) forgetBuffer.Add(kv.Key);
            }
            foreach (var t in forgetBuffer) memory.Remove(t);
        }

        string NameOf(SightTarget t, out bool isPerson)
        {
            isPerson = false;
            if (t == null || t.gameObject == gameObject) return null;
            var other = t.GetComponent<Speaker>();
            if (other != null) { isPerson = true; return other.DisplayName; }
            var item = t.GetComponent<Carryable>();
            if (item != null && item.Holder == null) return item.DisplayName;   // 누가 들고 있는 물건은 따로 치지 않는다
            return null;
        }

        // =====================================================================
        // #5. LLM이 고른 행동 시작
        // =====================================================================

        /// <summary>LLM이 고른 행동을 시작한다. 시작할 수 없으면 그 이유가 LastResult가 되고 바로 끝난다.</summary>
        public void Execute(string action, string target)
        {
            action = (action ?? "").Trim();
            target = (target ?? "").Trim();
            if (action.Length == 0 || action == None) return;
            if (sleep != null && sleep.IsAsleep && action != Sleep) sleep.WakeUp();   // 무엇을 하려면 먼저 일어난다

            navigator.Stop();
            step = Step.None;
            eatAfterPickUp = false;
            goingRoom = null;
            LastResult = null;   // 새 행동을 시작하면 지난 결과는 지난 일이다
            Debug.Log($"[NPC {speaker?.DisplayName}] 행동: {action} {target}", this);

            starting = true;
            try
            {
                switch (action)
                {
                    case Go: StartGo(target); break;
                    case PickUp: StartPickUp(target, eat: false); break;
                    case Eat:
                        if (interactor.Held != null && interactor.Held.GetComponent<Food>() != null) { EatHeld(); break; }
                        StartPickUp(target.Length > 0 ? target : FirstKnownFood(), eat: true);
                        break;
                    case Drop: DropHeld(); break;
                    case Sleep: StartSleep(); break;
                    default: Report($"'{action}'은(는) 할 수 없는 행동이다"); break;
                }
            }
            finally { starting = false; }
        }

        void StartGo(string target)
        {
            // 1) 방 이름. 방은 "한 점"이 아니라 영역이다: 지금 방에서 나와 그 방 영역에 들어서면 도착이다.
            //    그래서 방 가운데가 계단 구멍 위라도(계단 방) 상관없다. 가운데 지점은 걸어갈 방향으로만 쓴다.
            foreach (var room in Room.All)
            {
                if (room.DisplayName != target) continue;
                var from = Room.FindAt(motor.FeetPosition + Vector2.up * 0.1f);
                if (from == room) { Report($"이미 {target}에 있다"); return; }
                leftRoom = from != null ? from.DisplayName : null;
                goingRoom = room;
                Rect r = room.Bounds;
                Vector2 point = new(r.center.x, NpcNavigator.FloorOf(r.yMin + 0.1f));
                Begin(Step.Going, $"{target}(으)로 가는 중");
                navigator.GoTo(() => point, 0.5f, feet => IsInside(room, feet));
                return;
            }
            // 2) 침대
            if (target == BedName && FindBed() is Bed bed) { Begin(Step.Going, "침대로 가는 중"); navigator.GoTo(bed.transform.position, 0.6f); return; }
            // 3) 보이거나 기억하는 사람·물건
            var seen = Recall(target);
            if (seen == null) { Report($"{target}이(가) 어디 있는지 모른다"); return; }
            float stop = seen.isPerson ? personStopDistance : itemStopDistance;
            // 이미 곁에 있으면 걷지 않는다. T-8 시험에서 1m 옆 사람에게 "가기"를 고르고 바로 "도착"해 다시 생각하는 고리가 생겼다.
            if (LivePosition(seen) is Vector2 p && Mathf.Abs(p.y - motor.FeetPosition.y) < 1f && Mathf.Abs(p.x - motor.FeetPosition.x) <= stop + 0.3f)
            { Report($"이미 {target} 곁에 있다"); return; }
            Begin(Step.Going, $"{target}에게 가는 중");
            navigator.GoTo(() => LivePosition(seen), stop);
        }

        void StartPickUp(string target, bool eat)
        {
            if (string.IsNullOrEmpty(target)) { Report("먹을 것이 어디 있는지 모른다"); return; }
            if (interactor.Held != null) { Report($"손에 이미 {interactor.Held.DisplayName}을(를) 들고 있다"); return; }
            var seen = Recall(target);
            if (seen == null || seen.isPerson) { Report($"{target}이(가) 어디 있는지 모른다"); return; }
            itemTarget = seen;
            eatAfterPickUp = eat;
            Begin(eat ? Step.Eating : Step.PickingUp, $"{target}을(를) {(eat ? "먹으러" : "주우러")} 가는 중");
            navigator.GoTo(() => LivePosition(seen), itemStopDistance);
        }

        void FinishPickUp()
        {
            var item = itemTarget?.target != null ? itemTarget.target.GetComponent<Carryable>() : null;
            string name = itemTarget?.name ?? "물건";
            if (item == null || item.Holder != null || Vector2.Distance(item.transform.position, itemTarget.position) > 0.3f && !vision.CanSee(itemTarget.target))
            {
                if (itemTarget?.target != null) memory.Remove(itemTarget.target);
                Finish($"{name}이(가) 있던 곳에 가 보니 {name}이(가) 없었다");
                return;
            }
            var result = interactor.TryInteract(item);
            if (result != InteractionResult.Done) { Finish($"{name}을(를) 집으려 했지만 못 집었다"); return; }
            memory.Remove(itemTarget.target);
            if (eatAfterPickUp) { step = Step.None; EatHeld(); return; }
            Finish($"{name}을(를) 주웠다");
        }

        void EatHeld()
        {
            string name = interactor.Held != null ? interactor.Held.DisplayName : "음식";
            var result = vitals != null ? vitals.TryEatHeld() : InteractionResult.Busy;
            // 사과를 막 주운 직후면 들어 올리는 연출이 끝나지 않아 Busy일 수 있다. 잠깐 뒤 다시 먹는다.
            if (result == InteractionResult.Busy && vitals != null && interactor.Held != null) { Begin(Step.Chewing, $"{name}을(를) 먹는 중"); Invoke(nameof(RetryEat), 0.4f); return; }
            Finish(result == InteractionResult.Done ? $"{name}을(를) 먹었다" : $"{name}을(를) 먹으려 했지만 먹을 수 없었다");
        }

        void RetryEat()
        {
            if (step != Step.Chewing) return;   // 그 사이 다른 행동을 시작했다
            step = Step.None;
            EatHeld();
        }

        void DropHeld()
        {
            if (interactor.Held == null) { Report("손에 든 것이 없다"); return; }
            string name = interactor.Held.DisplayName;
            var anim = GetComponent<CharacterAnimator>();
            float dir = anim == null || anim.FacingRight ? 1f : -1f;
            var result = interactor.TryDropAt(motor.FeetPosition + new Vector2(dir * 0.8f, 0.1f));
            if (result != InteractionResult.Done) result = interactor.TryDropAt(motor.FeetPosition + new Vector2(-dir * 0.8f, 0.1f));
            Report(result == InteractionResult.Done ? $"{name}을(를) 내려놓았다" : $"{name}을(를) 내려놓을 자리가 없었다");
        }

        void StartSleep()
        {
            var bed = FindBed();
            if (bed == null || sleep == null) { Report("잘 곳이 없다"); return; }
            if (bed.Sleeper != null) { Report("침대에 누가 자고 있다"); return; }
            bedTarget = bed;
            Begin(Step.Sleeping, "자러 침대로 가는 중");
            navigator.GoTo(bed.transform.position, 0.3f);
        }

        // =====================================================================
        // #6. 도우미
        // =====================================================================

        void Begin(Step s, string label)
        {
            step = s;
            stepLabel = label;
        }

        // 바로 끝나는 행동(시작할 수 없음, 내려놓기 등)의 결과
        void Report(string result)
        {
            step = Step.None;
            LastResult = result;
            Debug.Log($"[NPC {speaker?.DisplayName}] 행동 결과: {result}", this);
            // 바로 끝난 행동은 두뇌를 다시 부르지 않는다. 부르면 "이미 거실에 있다 → 또 거실로 가기"가 LLM 호출 고리가 된다(T-8 시험, P-4).
            // 결과는 LastResult로 남아 다음에 생각할 때 "방금 한 일의 결과"로 들어간다.
            if (!starting) ActionFinished?.Invoke(result);
        }

        void Finish(string result)
        {
            navigator.Stop();
            Report(result);
        }

        string Destination() => stepLabel != null ? stepLabel.Replace("(으)로 가는 중", "").Replace("에게 가는 중", "").Replace("로 가는 중", "") : "목적지";

        // 이름으로 기억을 찾는다. 같은 이름이 여럿이면(사과 3개, 동명이인) 가장 가까운 것.
        Seen Recall(string name)
        {
            Seen best = null;
            float bestDist = float.MaxValue;
            foreach (var s in memory.Values)
            {
                if (s.target == null || s.name != name) continue;
                float d = Vector2.Distance(motor.FeetPosition, s.position) + (Mathf.Abs(s.position.y - motor.FeetPosition.y) > 1f ? 10f : 0f);
                if (d < bestDist) { bestDist = d; best = s; }
            }
            return best;
        }

        // 지금 보이면 진짜 위치를, 안 보이면 기억한 자리를. 기억이 지워졌으면 null(놓침).
        Vector2? LivePosition(Seen seen)
        {
            if (seen.target == null) return null;
            if (vision != null && vision.CanSee(seen.target)) seen.position = seen.target.transform.position;
            else if (!memory.ContainsKey(seen.target)) return null;
            return seen.position;
        }

        string FirstKnownFood()
        {
            foreach (var s in memory.Values)
                if (s.target != null && s.target.GetComponent<Food>() != null) return s.name;
            return null;
        }

        static Bed FindBed() => FindFirstObjectByType<Bed>();

        // 발이 그 방 바닥에 서 있고, 옆 경계에서 roomEnterMargin 이상 들어왔나.
        // 발은 바닥 높이와 같아서 0.1 올려 본다 (경계 y에 딱 걸리면 아래층 방으로 잡힐 수 있다).
        bool IsInside(Room room, Vector2 feet)
        {
            Rect r = room.Bounds;
            float y = feet.y + 0.1f;
            return y >= r.yMin && y <= r.yMax && feet.x >= r.xMin + roomEnterMargin && feet.x <= r.xMax - roomEnterMargin;
        }

        // =====================================================================
        // #7. LLM에 넘길 설명 (P-3: 인지한 것만)
        // =====================================================================

        public string DescribeForPrompt()
        {
            var sb = new StringBuilder();
            sb.Append("갈 수 있는 곳: ");
            var places = new List<string>();
            foreach (var room in Room.All) places.Add(room.DisplayName);
            if (FindBed() != null) places.Add(BedName);
            sb.AppendLine(string.Join(", ", places));

            var known = new List<string>();
            var counted = new Dictionary<string, int>();
            foreach (var s in memory.Values)
            {
                if (s.target == null) continue;
                bool visible = visibleBuffer.Contains(s.target);
                var room = Room.FindAt(s.position + Vector2.up * 0.5f);
                string where = room != null ? room.DisplayName : "어딘가";
                string key = $"{s.name}({(visible ? "지금 보임" : $"{where}에서 봤다")})";
                counted[key] = counted.TryGetValue(key, out int c) ? c + 1 : 1;
            }
            foreach (var kv in counted) known.Add(kv.Value > 1 ? $"{kv.Key} {kv.Value}개" : kv.Key);
            sb.AppendLine(known.Count > 0 ? $"아는 사람과 물건: {string.Join(", ", known)}" : "아는 사람과 물건: 없음");
            sb.AppendLine($"손에 든 것: {(interactor.Held != null ? interactor.Held.DisplayName : "없음")}");
            if (sleep != null && sleep.IsAsleep) sb.AppendLine("지금 자고 있다.");
            if (CurrentLabel != null) sb.AppendLine($"지금 하는 일: {CurrentLabel}");
            if (!string.IsNullOrEmpty(LastResult)) sb.AppendLine($"방금 한 일의 결과: {LastResult}");
            return sb.ToString();
        }
    }
}
