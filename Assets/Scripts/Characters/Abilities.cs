using System;
using System.Collections.Generic;

namespace Improv.Characters
{
    // =====================================================================
    // 능력치와 기능의 값 (GDD R-031, IDEAS I-10). MonoBehaviour가 아닌 데이터라 한 파일에 모은다.
    // 틀은 마운트 앤 블레이드 1편식: 능력치가 높을수록 그 계열 기능에 쓸 점수가 늘고, 기능 수준은 능력치가 허락하는 데까지.
    // 숫자 감각은 크킹 3식: 10이 보통 사람 (사용자 2026-11-26).
    // =====================================================================

    /// <summary>
    /// 능력치 7개. 앞의 넷은 판정에 쓰는 '기능 능력치', 뒤의 셋은 몸과 마음의 버팀에만 관여하는 '상태 능력치'다.
    /// (C의 enum과 같다. 배열 번호로도 쓴다: values[(int)Ability.Strength])
    /// </summary>
    public enum Ability
    {
        Strength,    // 근력
        Agility,     // 민첩
        Intellect,   // 지능
        Senses,      // 감각
        Willpower,   // 정신력: 스트레스를 버티는 힘
        Health,      // 건강: 병·부상에 대한 저항. 건장한 노인은 높다
        Endurance,   // 체력: 지치지 않는 힘. 멸치 청년도 높을 수 있다
    }

    public static class AbilityNames
    {
        public const int Count = 7;
        public const int FunctionalCount = 4;   // 0~3이 기능 능력치, 4~6이 상태 능력치
        public static readonly string[] Korean = { "근력", "민첩", "지능", "감각", "정신력", "건강", "체력" };

        public static string Of(Ability a) => Korean[(int)a];
        public static bool IsFunctional(Ability a) => (int)a < FunctionalCount;

        public static bool TryParse(string korean, out Ability a)
        {
            int i = Array.IndexOf(Korean, korean);
            a = (Ability)Math.Max(0, i);
            return i >= 0;
        }

        /// <summary>크킹 3의 구간 말. 10이 보통이다 [가정 O-018].</summary>
        public static string Band(int value) =>
            value <= 4 ? "아주 낮음" : value <= 8 ? "낮음" : value <= 12 ? "보통" : value <= 16 ? "좋음" : "뛰어남";
    }

    /// <summary>한 사람의 타고난(또는 지금까지 쌓은) 능력치와 기능 수준. 시트와 답안지에 저장된다.</summary>
    [Serializable]
    public class AbilityScores
    {
        public bool set;                                  // false면 아직 안 정함(옛 시트). 생성기가 굴린다
        public int[] values = { 10, 10, 10, 10, 10, 10, 10 };
        public List<SkillLevel> skills = new();

        public int this[Ability a]
        {
            get => values != null && (int)a < values.Length ? values[(int)a] : 10;
            set { EnsureShape(); values[(int)a] = value; }
        }

        public int Skill(string name)
        {
            var s = skills.Find(x => x.name == name);
            return s != null ? s.level : 0;
        }

        public void SetSkill(string name, int level)
        {
            var s = skills.Find(x => x.name == name);
            if (level <= 0) { if (s != null) skills.Remove(s); return; }
            if (s == null) skills.Add(new SkillLevel { name = name, level = level });
            else s.level = level;
        }

        public void EnsureShape()
        {
            if (values == null || values.Length != AbilityNames.Count)
            {
                var v = new int[AbilityNames.Count];
                for (int i = 0; i < v.Length; i++) v[i] = values != null && i < values.Length ? values[i] : 10;
                values = v;
            }
            skills ??= new List<SkillLevel>();
        }

        public AbilityScores Clone()
        {
            var c = new AbilityScores { set = set, values = (int[])values.Clone() };
            foreach (var s in skills) c.skills.Add(new SkillLevel { name = s.name, level = s.level });
            return c;
        }
    }

    [Serializable]
    public class SkillLevel
    {
        public string name;
        public int level;
    }

    /// <summary>
    /// 게임 중 능력치를 깎는 것 하나: 부상, 질병, 정신적 문제 (사용자 2026-11-26). 까닭이 붙어 있어 후일담에서 치료할 수 있다.
    /// 관계도의 수정치(R-027)와 같은 모양이다: 바탕값은 그대로 두고 위에 얹는다.
    /// </summary>
    [Serializable]
    public class AbilityModifier
    {
        public string cause;    // "다리 부상", "감기", "공포증"
        public Ability ability;
        public int value;       // 보통 음수
    }
}
