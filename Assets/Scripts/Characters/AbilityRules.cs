using System;
using System.Collections.Generic;
using UnityEngine;

namespace Improv.Characters
{
    /// <summary>
    /// 능력치와 기능의 규칙 (GDD R-031, O-018). 기능 목록과 점수 규칙을 에셋에 둔다 (Assets/Data/Abilities/AbilityRules.asset).
    /// 마운트 앤 블레이드 1편식: 능력치 2점마다 그 계열 기능 점수 1점, 기능 수준은 능력치의 3분의 1까지 [가정].
    /// </summary>
    [CreateAssetMenu(fileName = "AbilityRules", menuName = "Improv/Ability Rules")]
    public class AbilityRules : ScriptableObject
    {
        // =====================================================================
        // #1. 기능 목록
        // =====================================================================

        [Tooltip("기능마다 어느 능력치 계열인가, 배우려면 그 능력치가 얼마 이상이어야 하나")]
        public List<SkillDef> skills = new();

        // =====================================================================
        // #2. 점수 규칙 [가정 O-018]
        // =====================================================================

        [Tooltip("능력치 몇 점마다 그 계열 기능 점수 1점이 생기나 (10 → 5점)")]
        public int pointsPerSkillPoint = 2;
        [Tooltip("기능 수준의 한계 = 능력치 / 이 값 (10 → 3)")]
        public int capDivisor = 3;
        [Tooltip("능력치가 가질 수 있는 범위")]
        public Vector2Int abilityRange = new(1, 30);

        [Header("캐릭터 메이킹 (R-031c)")]
        // 기능 능력치와 상태 능력치가 한 주머니를 함께 쓴다 (사용자 2026-11-26): 건강을 깎아 지능에 몰아주는 '병약한 천재'가 되게.
        [Tooltip("능력치 일곱의 합. 보통 사람 10 × 7")]
        public int budget = 70;
        [Tooltip("메이킹에서 한 능력치를 이 범위 밖으로 못 옮긴다")]
        public Vector2Int makingRange = new(4, 16);

        [Header("NPC 생성 (R-031b)")]
        [Tooltip("보통 사람(10)에서 위아래로 흔드는 폭. 두 번 굴려 더하므로 가운데가 잦다")]
        public int spread = 2;

        // =====================================================================
        // #3. 계산 (판정은 모두 여기 한 곳에서. 코드 어디서나 같은 답이 나오게)
        // =====================================================================

        public SkillDef Find(string skill) => skills.Find(s => s.name == skill);

        /// <summary>그 계열 기능에 쓸 수 있는 점수.</summary>
        public int PointsFor(int abilityValue) => Mathf.Max(0, abilityValue) / Mathf.Max(1, pointsPerSkillPoint);

        /// <summary>그 기능이 오를 수 있는 한계. 요구 능력치에 못 미치면 0이다.</summary>
        public int CapFor(SkillDef def, int abilityValue) =>
            def == null || abilityValue < def.require ? 0 : Mathf.Max(0, abilityValue) / Mathf.Max(1, capDivisor);

        /// <summary>계열 점수 중 이미 쓴 것.</summary>
        public int SpentOn(AbilityScores s, Ability a)
        {
            int spent = 0;
            foreach (var lv in s.skills)
            {
                var def = Find(lv.name);
                if (def != null && def.ability == a) spent += lv.level;
            }
            return spent;
        }

        /// <summary>기능 수준이 한계나 계열 점수를 넘으면 깎는다. 능력치를 내린 뒤 부른다 (메이킹, 생성).</summary>
        public void Clamp(AbilityScores s)
        {
            s.EnsureShape();
            for (int i = 0; i < AbilityNames.Count; i++) s.values[i] = Mathf.Clamp(s.values[i], abilityRange.x, abilityRange.y);
            s.skills.RemoveAll(lv => Find(lv.name) == null);
            foreach (var lv in s.skills) lv.level = Mathf.Min(lv.level, CapFor(Find(lv.name), s[Find(lv.name).ability]));
            for (int a = 0; a < AbilityNames.FunctionalCount; a++)
            {
                // 계열 점수를 넘으면 뒤에서부터 깎는다
                int over = SpentOn(s, (Ability)a) - PointsFor(s.values[a]);
                for (int i = s.skills.Count - 1; i >= 0 && over > 0; i--)
                {
                    if (Find(s.skills[i].name).ability != (Ability)a) continue;
                    int cut = Mathf.Min(over, s.skills[i].level);
                    s.skills[i].level -= cut;
                    over -= cut;
                }
            }
            s.skills.RemoveAll(lv => lv.level <= 0);
        }

        /// <summary>
        /// NPC와 주사위의 능력치 굴리기 (R-031b). 보통(10)에서 흔들고, 직업·유년기의 보정을 더한 뒤, 기능 점수를 쓴다.
        /// favored(직업이 잘하는 기능)부터 채우고, 남은 점수는 같은 계열의 기능에 무작위로.
        /// </summary>
        public AbilityScores Roll(System.Random rng, IEnumerable<AbilityBonus> bonuses, IEnumerable<string> favored)
        {
            var s = new AbilityScores { set = true };
            for (int i = 0; i < AbilityNames.Count; i++) s.values[i] = AbilityNames.Average + rng.Next(-spread, spread + 1) + rng.Next(-spread, spread + 1);
            if (bonuses != null) foreach (var b in bonuses) s[b.ability] += b.value;
            Clamp(s);
            SpendSkills(s, rng, favored);
            return s;
        }

        /// <summary>남은 기능 점수를 쓴다. 능력치는 건드리지 않는다.</summary>
        public void SpendSkills(AbilityScores s, System.Random rng, IEnumerable<string> favored)
        {
            var order = new List<SkillDef>();
            if (favored != null) foreach (var f in favored) { var d = Find(f); if (d != null && !order.Contains(d)) order.Add(d); }
            var rest = new List<SkillDef>(skills);
            rest.RemoveAll(order.Contains);
            for (int i = rest.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (rest[i], rest[j]) = (rest[j], rest[i]); }
            order.AddRange(rest);
            // 잘하는 기능은 한계까지, 나머지는 한 점씩 돌아가며. 그래야 한 기능에 몰리지 않는다.
            int favoredCount = order.Count - rest.Count;
            for (int i = 0; i < favoredCount; i++) Raise(s, order[i], int.MaxValue);
            for (bool any = true; any;)
            {
                any = false;
                for (int i = favoredCount; i < order.Count; i++) any |= Raise(s, order[i], 1);
            }
        }

        bool Raise(AbilityScores s, SkillDef def, int amount)
        {
            int left = PointsFor(s[def.ability]) - SpentOn(s, def.ability);
            int room = CapFor(def, s[def.ability]) - s.Skill(def.name);
            int add = Mathf.Min(amount, Mathf.Min(left, room));
            if (add <= 0) return false;
            s.SetSkill(def.name, s.Skill(def.name) + add);
            return true;
        }
    }

    [Serializable]
    public class SkillDef
    {
        public string name;
        public Ability ability;
        [Tooltip("이 기능을 1이라도 가지려면 계열 능력치가 이만큼은 돼야 한다")]
        public int require;
        [Tooltip("코드가 실제로 쓰는 곳. 비어 있으면 LLM의 연기·판단 참고로만 쓰인다")]
        public string usedBy;
    }

    /// <summary>배경(직업, 유년기)이 주는 능력치 보정 하나.</summary>
    [Serializable]
    public class AbilityBonus
    {
        public Ability ability;
        public int value;
    }
}
