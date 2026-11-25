using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Improv.Characters
{
    // =====================================================================
    // 관계도: 한 캐릭터가 남(과 자기 자신)에게 품은 마음 (IDEAS I-12, GDD R-027).
    // 일반 C# 클래스라 MonoBehaviour가 아니다. 지금은 NPC 두뇌가 들고 있고, 나중에 플레이어와 관계도 창(T-9)도 같은 것을 쓴다 (P-1).
    //
    // 호감 = 묵은 감정(게임 전, 기반값) + 까닭이 붙은 수정치들(게임 중). 합이 같아도 내역이 다르면 다른 관계다(60 ≠ 60-5+5).
    // 애정 = 본능적으로 끌리는 정도. 구슬 도장을 굴려 찍듯, 그 사람의 면모가 드러난 만큼만 찍힌다. 안 드러난 면은 0이다.
    // 자기 자신도 "이제까지의 나"라는 상대로 똑같이 적는다 (사용자 2026-11-25: 자존감 대신 스스로를 타자화).
    // 사람은 id로 구분한다(F-17). 이름은 글로 찍을 때만 그때그때 찾는다.
    // =====================================================================

    /// <summary>까닭이 붙은 마음 하나. "말다툼함 -5".</summary>
    public class FeelingMod
    {
        public string why;
        public float value;        // fadeStart 때의 값. 지금 값은 Current(now)
        public float fadeStart;    // 게임 초(Time.time)
        public float fadeEnd;      // 이때 0이 된다. lasting이면 쓰지 않는다
        public bool lasting;       // 오래 남는 마음(선을 넘은 일, 드러난 면모)
        public FeelingMod cancels; // 상쇄한 짝. 둘 다 그대로 보이지만 함께 빨리 흐려진다

        public float Current(float now)
        {
            if (lasting) return value;
            float span = fadeEnd - fadeStart;
            if (span <= 0f) return 0f;
            return value * Mathf.Clamp01(1f - (now - fadeStart) / span);
        }

        // 지금 값으로 다시 시작점을 잡고, 남은 시간에 배율을 곱한다. 상쇄될 때 쓴다.
        public void Rescale(float now, float timeFactor)
        {
            if (lasting) return;
            float remaining = Mathf.Max(0f, fadeEnd - now);
            value = Current(now);
            fadeStart = now;
            fadeEnd = now + remaining * timeFactor;
        }
    }

    /// <summary>한 상대에 대한 마음.</summary>
    public class Relation
    {
        public readonly int targetId;
        public int oldFeeling = 50;        // 묵은 감정 (0~100). 게임 전부터 품은 것
        public string oldWhy = "";         // 묵은 감정의 까닭 ("용서 못 함")
        public readonly List<FeelingMod> mods = new();    // 호감 수정치
        public readonly List<FeelingMod> faces = new();   // 애정: 드러난 면모 ("웃는 얼굴이 좋음 +8")

        public Relation(int targetId) { this.targetId = targetId; }

        public int Affinity(float now)
        {
            float sum = oldFeeling;
            foreach (var m in mods) sum += m.Current(now);
            return Mathf.Clamp(Mathf.RoundToInt(sum), 0, 100);
        }

        // 아무 면모도 안 드러났으면 보통(50)이다. 도장이 아직 안 찍혔다.
        public int Affection(float now)
        {
            float sum = 50f;
            foreach (var f in faces) sum += f.Current(now);
            return Mathf.Clamp(Mathf.RoundToInt(sum), 0, 100);
        }
    }

    public class RelationBook
    {
        // =====================================================================
        // #1. 설정값 (GDD O-014 [가정])
        // =====================================================================

        public float fadeSeconds = 1800f;      // 가벼운 마음이 사라지기까지 (게임 초). 1800 = 게임 1.5일 (하루 1200초)
        public int lastingAt = 20;             // 이 크기 이상이면 흐려지지 않는다 (선을 넘은 일)
        public int maxChange = 50;             // LLM이 고른 값을 이 범위로 자른다 (안전장치)
        public float cancelSpeedUp = 0.5f;     // 다 상쇄되면 남은 시간이 이 비율만큼 줄어든다

        // =====================================================================
        // #2. 상태
        // =====================================================================

        public readonly int selfId;
        readonly Dictionary<int, Relation> relations = new();   // C의 해시 테이블: id → 관계
        public IEnumerable<Relation> All => relations.Values;

        public RelationBook(int selfId) { this.selfId = selfId; }

        public Relation Get(int targetId)
        {
            if (!relations.TryGetValue(targetId, out var r)) relations[targetId] = r = new Relation(targetId);
            return r;
        }

        public bool Has(int targetId) => relations.ContainsKey(targetId);

        public void SetOld(int targetId, int value, string why)
        {
            var r = Get(targetId);
            r.oldFeeling = Mathf.Clamp(value, 0, 100);
            r.oldWhy = why ?? "";
        }

        // =====================================================================
        // #3. 마음 바꾸기
        // =====================================================================

        /// <summary>
        /// 마음 하나를 더한다. 값은 LLM이 그 캐릭터로서 느낀 만큼 고른 것이다(사용자 2026-11-25: 남이 보기엔 작은 일도 당사자에겐 선을 넘을 수 있다).
        /// cancels가 앞서 붙은 까닭과 같고 부호가 반대면 상쇄한다: 둘 다 보이지만, 상쇄된 만큼 함께 빨리 흐려진다.
        /// </summary>
        public FeelingMod Add(int targetId, bool affection, string why, int value, string cancels, float now)
        {
            value = Mathf.Clamp(value, -maxChange, maxChange);
            if (value == 0 || string.IsNullOrWhiteSpace(why)) return null;
            var r = Get(targetId);
            Prune(r, now);
            var list = affection ? r.faces : r.mods;
            // 이미 적힌 까닭을 다시 적으면 버린다 (I-12 첫 시험: LLM이 묵은 감정을 새 일처럼 되풀이했다).
            if (Same(why, r.oldWhy) || list.Exists(m => Same(m.why, why))) return null;
            var mod = new FeelingMod
            {
                why = why.Trim(), value = value, fadeStart = now, fadeEnd = now + fadeSeconds,
                // 드러난 면모는 도장처럼 남는다. 호감은 크기가 선을 넘으면 남는다.
                lasting = affection || Mathf.Abs(value) >= lastingAt,
            };

            if (!affection && !string.IsNullOrWhiteSpace(cancels))
            {
                var other = list.Find(m => m.cancels == null && Math.Sign(m.Current(now)) == -Math.Sign(value) && Same(m.why, cancels));
                if (other != null)
                {
                    // 상쇄된 몫 = 둘 중 작은 크기. 각자 크기에 비해 얼마나 상쇄됐나에 따라 남은 시간이 줄어든다 (I-12 답 a).
                    float c = Mathf.Min(Mathf.Abs(other.Current(now)), Mathf.Abs(value));
                    if (c > 0f)
                    {
                        other.Rescale(now, 1f - cancelSpeedUp * c / Mathf.Abs(other.Current(now)));
                        mod.Rescale(now, 1f - cancelSpeedUp * c / Mathf.Abs(value));
                        other.cancels = mod;
                        mod.cancels = other;
                    }
                }
            }
            list.Add(mod);
            return mod;
        }

        static bool Same(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            a = a.Trim(); b = b.Trim();
            return a == b || a.Contains(b) || b.Contains(a);
        }

        // 다 흐려진 마음은 지운다.
        void Prune(Relation r, float now)
        {
            r.mods.RemoveAll(m => Mathf.Abs(m.Current(now)) < 0.5f);
            r.faces.RemoveAll(m => Mathf.Abs(m.Current(now)) < 0.5f);
        }

        // =====================================================================
        // #4. 글로 찍기 (프롬프트용)
        // =====================================================================

        /// <summary>
        /// 규격 줄로 찍는다. 숫자를 함께 넘긴다(사용자 2026-11-25: 비유보다 숫자 비교를 LLM이 더 명확히 이해한다).
        /// 예: "- 박건우: 호감 20 = 묵은 감정 25(용서 못 함) - 5(말다툼함) / 애정 50"
        /// </summary>
        public void Describe(StringBuilder sb, Func<int, string> nameOf, float now)
        {
            WriteLine(sb, "이제까지의 나", Get(selfId), now);
            foreach (var r in relations.Values)
            {
                if (r.targetId == selfId) continue;
                string name = nameOf(r.targetId);
                if (!string.IsNullOrEmpty(name)) WriteLine(sb, name, r, now);
            }
        }

        void WriteLine(StringBuilder sb, string name, Relation r, float now)
        {
            Prune(r, now);
            sb.Append("- ").Append(name).Append(": 호감 ").Append(r.Affinity(now)).Append(" = 묵은 감정 ").Append(r.oldFeeling);
            if (!string.IsNullOrWhiteSpace(r.oldWhy)) sb.Append('(').Append(r.oldWhy.Trim()).Append(')');
            foreach (var m in r.mods) AppendMod(sb, m, now);
            if (r.faces.Count > 0)
            {
                sb.Append(" / 애정 ").Append(r.Affection(now)).Append(" = 50");
                foreach (var f in r.faces) AppendMod(sb, f, now);
            }
            sb.AppendLine();
        }

        static void AppendMod(StringBuilder sb, FeelingMod m, float now)
        {
            int v = Mathf.RoundToInt(m.Current(now));
            if (v == 0) return;
            sb.Append(v > 0 ? " + " : " - ").Append(Mathf.Abs(v)).Append('(').Append(m.why).Append(')');
        }
    }
}
