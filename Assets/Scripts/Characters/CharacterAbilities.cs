using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Improv.Characters
{
    /// <summary>
    /// 몸에 붙은 능력치와 기능 (GDD R-031). 플레이어와 NPC가 같은 것을 쓴다 (P-1).
    /// 바탕값(Base)은 판이 시작할 때 생성기나 메이킹 창이 넣는다(Apply). 게임 중 부상·질병·정신적 문제는
    /// 바탕값을 고치지 않고 까닭 붙은 수정치(AbilityModifier)로 얹는다. 그래야 후일담에서 "치료"로 걷어낼 수 있다 (R-031d).
    /// 기능의 실제 수준은 지금 능력치의 한계를 넘지 못한다: 다리를 다쳐 근력이 떨어지면 운반도 같이 떨어진다.
    /// 효과는 이 값을 읽는 쪽(CharacterVitals, Vision)이 정한다. 이 컴포넌트는 값만 가진다.
    /// </summary>
    public class CharacterAbilities : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [SerializeField] AbilityRules rules;

        // =====================================================================
        // #2. 상태
        // =====================================================================

        [SerializeField] AbilityScores baseScores = new();   // Inspector에서 보이게 (Play 중 확인용)
        [SerializeField] List<AbilityModifier> modifiers = new();

        public AbilityRules Rules => rules;
        public AbilityScores Base => baseScores;
        public IReadOnlyList<AbilityModifier> Modifiers => modifiers;

        /// <summary>값이 바뀌면 알린다 (관계도 창의 '나' 칸 등).</summary>
        public event Action Changed;

        void Awake()
        {
            if (rules == null) Debug.LogError($"[CharacterAbilities] '{name}'의 rules(AbilityRules)가 비어 있습니다.", this);
            baseScores.EnsureShape();
        }

        // =====================================================================
        // #3. 읽기
        // =====================================================================

        /// <summary>지금 능력치 = 바탕 + 수정치.</summary>
        public int Get(Ability a)
        {
            int v = baseScores[a];
            foreach (var m in modifiers) if (m.ability == a) v += m.value;
            return rules != null ? Mathf.Clamp(v, rules.abilityRange.x, rules.abilityRange.y) : v;
        }

        /// <summary>지금 기능 수준. 바탕 수준이 지금 능력치의 한계를 넘으면 한계까지만 (부상으로 떨어짐).</summary>
        public int Skill(string skill)
        {
            int level = baseScores.Skill(skill);
            if (level <= 0 || rules == null) return level;
            var def = rules.Find(skill);
            return def == null ? 0 : Mathf.Min(level, rules.CapFor(def, Get(def.ability)));
        }

        /// <summary>
        /// 효과 배율: 보통(10)에서 1점 멀어질 때마다 perPoint만큼. 기능은 수준 1마다 perSkill만큼 더한다.
        /// 둘을 더하므로 능력치가 기능을 메운다 (사용자 2026-11-26): 근력이 뛰어나면 운반 기술이 없어도 힘으로 때운다.
        /// 크킹 3에서 인생관 특전이 없어도 능력치가 높으면 되는 것과 같다. 예) 운반은 근력 2점 ≈ 기능 1.
        /// 예) 감각 13, 관찰 2, perPoint 0.03, perSkill 0.04 → 1 + 0.09 + 0.08 = 1.17
        /// </summary>
        public float Factor(Ability a, float perPoint, string skill = null, float perSkill = 0f, float min = 0.3f, float max = 2f)
        {
            float f = 1f + (Get(a) - AbilityNames.Average) * perPoint;
            if (skill != null) f += Skill(skill) * perSkill;
            return Mathf.Clamp(f, min, max);
        }

        // =====================================================================
        // #4. 바꾸기
        // =====================================================================

        /// <summary>판이 시작할 때 바탕값을 넣는다 (생성기, 메이킹).</summary>
        public void Apply(AbilityScores scores)
        {
            if (scores == null) return;
            baseScores = scores.Clone();
            baseScores.EnsureShape();
            Changed?.Invoke();
        }

        /// <summary>부상·질병·정신적 문제로 깎는다 (R-031d). 같은 까닭·같은 능력치는 하나로 덮어쓴다.</summary>
        public void AddModifier(string cause, Ability a, int value)
        {
            modifiers.RemoveAll(m => m.cause == cause && m.ability == a);
            if (value != 0) modifiers.Add(new AbilityModifier { cause = cause, ability = a, value = value });
            Changed?.Invoke();
        }

        /// <summary>그 까닭의 수정치를 모두 걷는다 (치료, 후일담 정산).</summary>
        public void RemoveModifiers(string cause)
        {
            if (modifiers.RemoveAll(m => m.cause == cause) > 0) Changed?.Invoke();
        }

        // =====================================================================
        // #5. 글로 찍기 (LLM 프롬프트, 관계도 창)
        // =====================================================================

        /// <summary>
        /// "근력 12, 민첩 8, …" 숫자로 넘긴다 (사용자 2026-11-25: 숫자 비교가 비유보다 LLM에 명확하다).
        /// 수정치는 까닭과 함께 붙인다. 기능은 가진 것만.
        /// </summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append("기능 능력치: ");
            Join(sb, 0, AbilityNames.FunctionalCount);
            sb.Append("\n상태 능력치: ");
            Join(sb, AbilityNames.FunctionalCount, AbilityNames.Count);
            var owned = new List<string>();
            foreach (var lv in baseScores.skills)
            {
                int now = Skill(lv.name);
                owned.Add(now == lv.level ? $"{lv.name} {now}" : $"{lv.name} {now}(본래 {lv.level})");
            }
            sb.Append("\n기능: ").Append(owned.Count > 0 ? string.Join(", ", owned) : "특별히 익힌 것 없음");
            return sb.ToString();
        }

        void Join(StringBuilder sb, int from, int to)
        {
            for (int i = from; i < to; i++)
            {
                var a = (Ability)i;
                if (i > from) sb.Append(", ");
                sb.Append(AbilityNames.Of(a)).Append(' ').Append(Get(a));
                foreach (var m in modifiers)
                    if (m.ability == a) sb.Append($"({m.cause} {m.value:+0;-0})");
            }
        }
    }
}
