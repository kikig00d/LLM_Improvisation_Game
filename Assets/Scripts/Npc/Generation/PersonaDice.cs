using System;
using System.Collections.Generic;

namespace Improv.Npc.Generation
{
    /// <summary>
    /// 캐릭터 메이킹의 주사위 (GDD R-029a). 칸 하나, 또는 답안지 전체를 보기 목록에서 무작위로 채운다.
    /// NPC 생성(CastGenerator)과 같은 표를 쓰고, 서로 부딪히는 특성(수다쟁이 + 과묵함)은 고르지 않는다.
    /// </summary>
    public static class PersonaDice
    {
        public static void Gender(PersonaDraft d, Random rng) => d.gender = rng.Next(2) == 0 ? PersonaDraft.Male : PersonaDraft.Female;

        public static void Age(PersonaDraft d, CharacterTables t, Random rng) => d.age = rng.Next(t.ageRange.x, t.ageRange.y + 1);

        public static void Name(PersonaDraft d, CharacterTables t, ICollection<string> taken, Random rng)
        {
            // 성별을 정했으면 거기에 맞는 이름에서. 산장에 이미 있는 이름은 피한다.
            var pool = t.names.FindAll(n => !taken.Contains(n.name) && n.name != d.name && (d.gender == "" || n.female == d.IsFemale));
            if (pool.Count > 0) d.name = pool[rng.Next(pool.Count)].name;
        }

        public static void Childhood(PersonaDraft d, CharacterTables t, Random rng) => d.childhood = Other(t.childhoods.ConvertAll(c => c.title), d.childhood, rng);

        public static void Adulthood(PersonaDraft d, CharacterTables t, ICollection<string> takenJobs, Random rng)
        {
            // 하나뿐인 직업(산장 관리인)을 프리셋 NPC가 이미 맡았으면 고르지 않는다.
            var pool = t.adulthoods.FindAll(a => !(a.unique && takenJobs.Contains(a.title))).ConvertAll(a => a.title);
            d.adulthood = Other(pool, d.adulthood, rng);
        }

        public static void Family(PersonaDraft d, MakingTemplates m, Random rng) => d.family = Other(m.familyPlaces, d.family, rng);

        public static void Arrival(PersonaDraft d, CharacterTables t, Random rng)
        {
            var job = t.adulthoods.Find(a => a.title == d.adulthood);
            d.arrival = job != null && !string.IsNullOrWhiteSpace(job.fixedArrival) ? job.fixedArrival : Other(t.arrivalReasons, d.arrival, rng);
        }

        public static void Trait(PersonaDraft d, CharacterTables t, int index, Random rng)
        {
            var pool = t.traits.FindAll(x =>
                x.name != d.traits[index] && Array.IndexOf(d.traits, x.name) < 0 &&
                !Array.Exists(d.traits, have => have != d.traits[index] && (x.conflicts.Contains(have) || (t.traits.Find(h => h.name == have)?.conflicts.Contains(x.name) ?? false))));
            if (pool.Count > 0) d.traits[index] = pool[rng.Next(pool.Count)].name;
        }

        public static void Belief(PersonaDraft d, CharacterTables t, int index, Random rng)
        {
            var pool = t.beliefs.FindAll(b => Array.IndexOf(d.beliefs, b) < 0);
            if (pool.Count > 0) d.beliefs[index] = pool[rng.Next(pool.Count)];
        }

        public static void Bond(PersonaDraft d, CharacterTables t, Random rng)
        {
            var pool = t.lightPasts.ConvertAll(p => p.title);
            pool.Add("");   // 생성기에 맡기기도 보기 중 하나다
            d.bond = Other(pool, d.bond, rng);
        }

        /// <summary>개인 서사 한 줄. 어떻게·왜는 가끔 비운다 (모든 일에 까닭이 붙지는 않는다).</summary>
        public static void Line(PersonalLine line, MakingTemplates m, Random rng)
        {
            line.when = Other(m.times, line.when, rng);
            line.where = Other(m.places, line.where, rng);
            Template(line.what, m.whatTemplates, m, rng);
            if (rng.NextDouble() < 0.5) Template(line.how, m.howTemplates, m, rng); else line.how.Choose(-1, m.howTemplates);
            if (rng.NextDouble() < 0.7) Template(line.why, m.whyTemplates, m, rng); else line.why.Choose(-1, m.whyTemplates);
        }

        public static void Template(TemplatePick pick, List<string> templates, MakingTemplates m, Random rng)
        {
            if (templates.Count == 0) return;
            pick.Choose(rng.Next(templates.Count), templates);
            Blanks(pick, templates, m, rng);
        }

        /// <summary>고른 틀의 빈칸만 꼬리표에 맞는 말로 채운다.</summary>
        public static void Blanks(TemplatePick pick, List<string> templates, MakingTemplates m, Random rng)
        {
            if (pick.IsEmpty) return;
            var labels = MakingTemplates.BlankLabels(templates[pick.t]);
            for (int i = 0; i < labels.Count && i < pick.b.Length; i++)
            {
                var words = m.WordsFor(labels[i]);
                if (words.Count > 0) pick.b[i] = words[rng.Next(words.Count)];
            }
        }

        /// <summary>답안지 전체를 새로 굴린다 ("모두 무작위").</summary>
        public static void All(PersonaDraft d, CharacterTables t, MakingTemplates m, ICollection<string> takenNames, ICollection<string> takenJobs, Random rng)
        {
            Gender(d, rng);
            Name(d, t, takenNames, rng);
            Age(d, t, rng);
            Childhood(d, t, rng);
            Adulthood(d, t, takenJobs, rng);
            Family(d, m, rng);
            Arrival(d, t, rng);
            for (int i = 0; i < d.traits.Length; i++) d.traits[i] = "";
            for (int i = 0; i < d.traits.Length; i++) Trait(d, t, i, rng);
            for (int i = 0; i < d.beliefs.Length; i++) d.beliefs[i] = "";
            for (int i = 0; i < d.beliefs.Length; i++) Belief(d, t, i, rng);
            // 개인 서사는 한두 줄만. 셋 다 채우면 설정이 무거워진다.
            int lines = 1 + rng.Next(2);
            for (int i = 0; i < d.personal.Length; i++)
            {
                d.personal[i] = new PersonalLine();
                if (i < lines) Line(d.personal[i], m, rng);
            }
            d.bond = "";
        }

        /// <summary>꼭 있어야 하는 칸 중 빈 것만 굴린다. 시작할 때 부른다. 개인 서사와 인연은 비워도 된다.</summary>
        public static void Required(PersonaDraft d, CharacterTables t, MakingTemplates m, ICollection<string> takenNames, ICollection<string> takenJobs, Random rng)
        {
            if (d.gender == "") Gender(d, rng);
            if (string.IsNullOrWhiteSpace(d.name)) Name(d, t, takenNames, rng);
            if (d.age <= 0) Age(d, t, rng);
            if (d.childhood == "") Childhood(d, t, rng);
            if (d.adulthood == "") Adulthood(d, t, takenJobs, rng);
            if (d.family == "") Family(d, m, rng);
            if (d.arrival == "") Arrival(d, t, rng);
            for (int i = 0; i < d.traits.Length; i++) if (d.traits[i] == "") Trait(d, t, i, rng);
            for (int i = 0; i < d.beliefs.Length; i++) if (d.beliefs[i] == "") Belief(d, t, i, rng);
        }

        // 지금 값과 다른 것을 고른다. 주사위를 눌렀는데 그대로면 고장 난 것처럼 보인다.
        static string Other(List<string> pool, string current, Random rng)
        {
            var list = pool.FindAll(x => x != current);
            if (list.Count == 0) list = pool;
            return list.Count == 0 ? current : list[rng.Next(list.Count)];
        }
    }
}
