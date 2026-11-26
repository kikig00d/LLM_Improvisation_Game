using System;
using System.Collections.Generic;
using Improv.Characters;
using Improv.Llm;
using Improv.Npc.Generation;
using Improv.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace Improv.UI
{
    /// <summary>
    /// 캐릭터 메이킹 창 (GDD R-029~R-029c, IDEAS I-11). 판이 시작할 때 산장 위에 덮개로 뜬다.
    /// 서술형 시험지처럼 문장 틀의 빈칸을 채운다: [보기]는 목록에서 고르고, 개인 서사는 문장 틀을 다이얼에서 골라 짧은 빈칸만 친다.
    /// 칸마다 주사위, "모두 무작위", "LLM이 빈칸 채워 줘"가 있고, 채워진 것은 고쳐도 된다 (사용자 2026-11-26).
    /// "시작"을 누르면 비운 필수 칸을 무작위로 채우고 PlayerPersona에 넘긴다. 그러면 CastGenerator가 NPC를 이 사람에 맞춰 만든다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class CharacterMakingWindow : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [SerializeField] PlayerPersona player;
        [Tooltip("'LLM이 빈칸 채워 줘'의 생각 단계. 빈칸 채우기는 빨라야 해서 끈다")]
        [SerializeField] string fillReasoning = "none";
        [SerializeField] int fillMaxTokens = 1500;
        [SerializeField] int fillTimeoutSeconds = 90;

        const string EmptyChoice = "(빈칸)";
        const string BondAuto = "(맡김)";

        // =====================================================================
        // #2. 상태
        // =====================================================================

        VisualElement overlay;
        ScrollView form;
        Label status;
        Button randomButton, llmButton, startButton;

        CastGenerator generator;
        CharacterTables tables;
        MakingTemplates templates;
        PersonaDraft draft;
        readonly HashSet<string> takenNames = new();
        readonly HashSet<string> takenJobs = new();
        readonly System.Random rng = new();
        string openDial;        // 지금 펼친 문장 틀 다이얼 ("line0.what" 같은 열쇠). null이면 닫힘
        bool busy;              // LLM을 기다리는 중
        readonly List<(Behaviour b, bool wasEnabled)> pausedControls = new();

        public bool IsOpen { get; private set; }

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            overlay = root.Q("making-overlay");
            form = root.Q<ScrollView>("form");
            status = root.Q<Label>("making-status");
            randomButton = root.Q<Button>("random-button");
            llmButton = root.Q<Button>("llm-button");
            startButton = root.Q<Button>("start-button");
            if (overlay == null || form == null || startButton == null)
            {
                Debug.LogError("[CharacterMakingWindow] Making.uxml에서 making-overlay, form, start-button을 찾지 못했습니다.", this);
                enabled = false;
                return;
            }
            randomButton.clicked += () => { if (busy) return; PersonaDice.All(draft, tables, templates, takenNames, takenJobs, rng); openDial = null; Rebuild(); };
            llmButton.clicked += () => { if (!busy) _ = FillWithLlm(); };   // _ = : 기다리지 않고 던져 둔다 (결과는 함수 안에서 처리)
            startButton.clicked += Confirm;
            overlay.style.display = DisplayStyle.None;
        }

        void Start()
        {
            if (player == null) player = FindFirstObjectByType<PlayerPersona>();
            generator = CastGenerator.Instance;
            // 생성기가 플레이어를 기다릴 때만 연다. 메이킹을 끈 판에서는 창이 뜨지 않는다.
            if (generator == null || player == null || !generator.WaitingForPlayer) return;
            tables = generator.Tables;
            templates = generator.Templates;
            draft = player.Draft;
            draft.EnsureShape(tables, templates);
            generator.Taken(takenNames, takenJobs);
            Open();
        }

        void Open()
        {
            IsOpen = true;
            // 쓰는 동안 WASD로 걷거나 P로 시간을 바꾸면 안 된다. 플레이어 조작을 모두 꺼 둔다 (관계도 창과 같은 목록).
            pausedControls.Clear();
            foreach (var b in FindObjectsByType<Behaviour>(FindObjectsSortMode.None))
                if (b is PlayerInputDriver || b is PlayerClickInteraction || b is PlayerHoverFeedback || b is PlayerSpeechInput
                    || b is PlayerUseInput || b is PlayerGazeInput || b is GameSpeedInput || b is PlayerSleepInput)
                {
                    pausedControls.Add((b, b.enabled));
                    b.enabled = false;
                }
            overlay.style.display = DisplayStyle.Flex;
            status.text = "";
            Rebuild();
        }

        void Close()
        {
            IsOpen = false;
            overlay.style.display = DisplayStyle.None;
            foreach (var (b, wasEnabled) in pausedControls) if (b != null) b.enabled = wasEnabled;
            pausedControls.Clear();
        }

        // =====================================================================
        // #4. 단추
        // =====================================================================

        void Confirm()
        {
            if (busy) return;
            PersonaDice.Required(draft, tables, templates, takenNames, takenJobs, rng);
            var identity = player.GetComponent<CharacterIdentity>();
            var member = draft.ToMember(tables, templates, identity != null ? identity.Id : 0);
            Debug.Log($"[메이킹] {string.Join(" ", PersonaDraft.Sentences(member))} / 지난 일: {string.Join(" / ", member.personal.ConvertAll(PastFormat.Line))} / 인연: {(member.bond == "" ? "맡김" : member.bond)}", this);
            Close();
            player.Confirm(member);   // 생성기가 이것을 기다리고 있다
        }

        async Awaitable FillWithLlm()
        {
            busy = true;
            SetButtons(false);
            status.text = "LLM이 빈칸을 채우는 중...";
            try
            {
                var options = new LlmRequestOptions { ReasoningEffort = fillReasoning, MaxTokens = fillMaxTokens, TimeoutSeconds = fillTimeoutSeconds };
                bool ok = await PersonaFiller.FillAsync(generator.Llm.CreateClient(), draft, tables, templates, takenNames, options, destroyCancellationToken);
                status.text = ok ? "빈칸을 채웠습니다. 마음에 안 드는 칸은 고치거나 굴리세요." : "LLM이 채우지 못했습니다. 다시 누르거나 주사위를 쓰세요.";
            }
            catch (OperationCanceledException) { return; }
            finally
            {
                busy = false;
                if (this != null) { SetButtons(true); Rebuild(); }   // Play를 멈춰 창이 사라졌으면 건드리지 않는다
            }
        }

        void SetButtons(bool on)
        {
            randomButton.SetEnabled(on);
            llmButton.SetEnabled(on);
            startButton.SetEnabled(on);
            form.SetEnabled(on);
        }

        // =====================================================================
        // #5. 시험지 그리기. 고르는 칸이 바뀌면 통째로 다시 그린다 (치는 칸은 다시 그리지 않아 입력이 끊기지 않는다).
        // =====================================================================

        void Rebuild()
        {
            if (!IsOpen) return;
            float scroll = form.scrollOffset.y;
            form.Clear();
            bool female = draft.IsFemale;

            Section("페르소나");
            var r = Row("①");
            Text(r, "나는");
            Field(r, draft.name, templates.nameMaxLength, v => draft.name = v, "이름", "mk-field--name");
            Text(r, ",");
            var ages = new List<string>();
            for (int a = tables.ageRange.x; a <= tables.ageRange.y; a++) ages.Add(a.ToString());
            Drop(r, ages, null, draft.age > 0 ? draft.age.ToString() : "", v => draft.age = v == "" ? 0 : int.Parse(v));
            Text(r, "살");
            Drop(r, new List<string> { PersonaDraft.Male, PersonaDraft.Female }, null, draft.gender, v => draft.gender = v);
            Text(r, "이다.");
            Dice(r, () => { PersonaDice.Gender(draft, rng); PersonaDice.Name(draft, tables, takenNames, rng); PersonaDice.Age(draft, tables, rng); });

            r = Row("②");
            // 받침에 따라 조사가 갈려서(전학생이었다 / 책벌레였다) 문장 대신 "칸: 값"으로 둔다.
            Text(r, "어린 시절:");
            Drop(r, tables.childhoods.ConvertAll(c => c.title), null, draft.childhood, v => draft.childhood = v);
            Dice(r, () => PersonaDice.Childhood(draft, tables, rng));

            r = Row("③");
            Text(r, "지금 나는");
            Drop(r, tables.adulthoods.FindAll(a => !(a.unique && takenJobs.Contains(a.title))).ConvertAll(a => a.title), null, draft.adulthood, v => draft.adulthood = v);
            Text(r, "이다.");
            Dice(r, () => PersonaDice.Adulthood(draft, tables, takenJobs, rng));

            r = Row("④");
            Text(r, "집에서의 자리:");
            Drop(r, templates.familyPlaces, f => MakingTemplates.Gendered(f, female), draft.family, v => draft.family = v);
            Dice(r, () => PersonaDice.Family(draft, templates, rng));

            r = Row("⑤");
            Text(r, "산장에 온 까닭:");
            Drop(r, tables.arrivalReasons, null, draft.arrival, v => draft.arrival = v);
            Dice(r, () => PersonaDice.Arrival(draft, tables, rng));

            // ⑥⑦은 연기의 주석이다. 능력치나 기능에는 쓰지 않는다 (사용자 2026-11-26).
            r = Row("⑥");
            Text(r, "성격:");
            for (int i = 0; i < draft.traits.Length; i++)
            {
                int k = i;   // 람다가 붙잡을 값은 반복문 안에서 복사한다
                var options = tables.traits.FindAll(t => t.name == draft.traits[k] || (Array.IndexOf(draft.traits, t.name) < 0 && !Conflicts(t, k))).ConvertAll(t => t.name);
                Drop(r, options, null, draft.traits[k], v => draft.traits[k] = v);
            }
            Dice(r, () => { for (int i = 0; i < draft.traits.Length; i++) draft.traits[i] = ""; for (int i = 0; i < draft.traits.Length; i++) PersonaDice.Trait(draft, tables, i, rng); });

            r = Row("⑦");
            Text(r, "믿는 것:");
            for (int i = 0; i < draft.beliefs.Length; i++)
            {
                int k = i;
                var options = tables.beliefs.FindAll(b => b == draft.beliefs[k] || Array.IndexOf(draft.beliefs, b) < 0);
                Drop(r, options, null, draft.beliefs[k], v => draft.beliefs[k] = v);
            }
            Dice(r, () => { for (int i = 0; i < draft.beliefs.Length; i++) draft.beliefs[i] = ""; for (int i = 0; i < draft.beliefs.Length; i++) PersonaDice.Belief(draft, tables, i, rng); });

            r = Row("⑧");
            Text(r, "산장에 온 사람 중 한 명은 내");
            Drop(r, tables.lightPasts.ConvertAll(p => p.title), null, draft.bond, v => draft.bond = v, BondAuto);
            Text(r, "이다.");
            Dice(r, () => PersonaDice.Bond(draft, tables, rng));

            AddAbilities();

            Section("개인 서사 (비워도 된다)");
            for (int i = 0; i < draft.personal.Length; i++) AddLine(i);

            form.schedule.Execute(() => form.scrollOffset = new Vector2(0, scroll));   // 다시 그려도 보던 자리를 지킨다
        }

        // ⑨ 능력치와 기능 (R-031c). 정해진 합 안에서 +/-로 옮긴다. 기능 점수는 그 계열 능력치에서 나온다 (마운트 앤 블레이드 1편식).
        void AddAbilities()
        {
            var rules = tables.abilityRules;
            if (rules == null) return;
            var s = draft.abilities;
            s.EnsureShape();
            Section("능력치와 기능 (10이 보통 사람)");
            AbilityGroup(rules, s, 0, AbilityNames.FunctionalCount, rules.functionalBudget, "기능 능력치: 힘으로 문 열기, 글 읽기 같은 판정에 쓴다", true);
            AbilityGroup(rules, s, AbilityNames.FunctionalCount, AbilityNames.Count, rules.stateBudget, "상태 능력치: 스트레스, 지침, 병을 버티는 힘", false);
        }

        void AbilityGroup(AbilityRules rules, AbilityScores s, int from, int to, int budget, string caption, bool withSkills)
        {
            int sum = 0;
            for (int i = from; i < to; i++) sum += s.values[i];
            int left = budget - sum;
            var head = Row(from == 0 ? "⑨" : "");
            Text(head, $"{caption} (남은 점수 {left})");
            if (from == 0) Dice(head, () => PersonaDice.Abilities(draft, tables, rng));

            for (int i = from; i < to; i++)
            {
                var a = (Ability)i;
                var row = Row("");
                Text(row, $"{AbilityNames.Of(a)} {s[a]}");
                var minus = SmallButton(row, "−", () => { s[a]--; Touched(rules, s); });
                minus.SetEnabled(s[a] > rules.makingRange.x);
                var plus = SmallButton(row, "+", () => { s[a]++; Touched(rules, s); });
                plus.SetEnabled(left > 0 && s[a] < rules.makingRange.y);
                Text(row, AbilityNames.Band(s[a]));
                if (!withSkills) continue;

                // 이 능력치 계열의 기능들. 계열 점수 = 능력치 / 2, 한 기능의 한계 = 능력치 / 3 [가정 O-018]
                int points = rules.PointsFor(s[a]) - rules.SpentOn(s, a);
                Text(row, $"| 기능 점수 {points}:");
                foreach (var def in rules.skills)
                {
                    if (def.ability != a) continue;
                    int cap = rules.CapFor(def, s[a]);
                    int level = s.Skill(def.name);
                    Text(row, cap == 0 ? $"{def.name} (계열 {def.require} 필요)" : $"{def.name} {level}/{cap}");
                    if (cap == 0) continue;
                    var sm = SmallButton(row, "−", () => { s.SetSkill(def.name, level - 1); Touched(rules, s); });
                    sm.SetEnabled(level > 0);
                    var sp = SmallButton(row, "+", () => { s.SetSkill(def.name, level + 1); Touched(rules, s); });
                    sp.SetEnabled(points > 0 && level < cap);
                }
            }
        }

        // 손으로 고친 순간부터 내 값이다(시작할 때 주사위로 덮지 않는다). 능력치를 내리면 넘친 기능을 깎는다.
        void Touched(AbilityRules rules, AbilityScores s)
        {
            s.set = true;
            rules.Clamp(s);
        }

        bool Conflicts(Trait t, int slot)
        {
            for (int i = 0; i < draft.traits.Length; i++)
            {
                if (i == slot || draft.traits[i] == "") continue;
                var have = tables.traits.Find(x => x.name == draft.traits[i]);
                if (t.conflicts.Contains(draft.traits[i]) || (have != null && have.conflicts.Contains(t.name))) return true;
            }
            return false;
        }

        // 개인 서사 한 줄: [때] | {곳} / 나 → 무엇을 / 어떻게 / 왜
        void AddLine(int index)
        {
            var line = draft.personal[index];
            var box = new VisualElement();
            box.AddToClassList("mk-line");
            form.Add(box);

            var head = new VisualElement();
            head.AddToClassList("mk-row");
            box.Add(head);
            Text(head, $"{index + 1}.");
            Drop(head, templates.times, null, line.when, v => line.when = v);
            Text(head, "|");
            Field(head, line.where, templates.placeMaxLength, v => line.where = v, "곳", "mk-field--place");
            Dice(head, () => PersonaDice.Line(line, templates, rng));
            var clear = new Button(() => { draft.personal[index] = new PersonalLine(); Rebuild(); }) { text = "지우기" };
            clear.AddToClassList("mk-small");
            head.Add(clear);

            AddPick(box, "나 →", line.what, templates.whatTemplates, $"line{index}.what");
            AddPick(box, "어떻게:", line.how, templates.howTemplates, $"line{index}.how");
            AddPick(box, "왜:", line.why, templates.whyTemplates, $"line{index}.why");
        }

        // 문장 틀 하나. 안 골랐으면 "틀 고르기", 골랐으면 틀의 글자와 빈칸을 번갈아 놓는다.
        void AddPick(VisualElement parent, string caption, TemplatePick pick, List<string> list, string key)
        {
            var row = new VisualElement();
            row.AddToClassList("mk-row");
            parent.Add(row);
            var cap = new Label(caption);
            cap.AddToClassList("mk-caption");
            row.Add(cap);

            if (pick.IsEmpty || pick.t >= list.Count)
            {
                var choose = new Button(() => { openDial = openDial == key ? null : key; Rebuild(); }) { text = openDial == key ? "닫기" : "틀 고르기 ▸" };
                choose.AddToClassList("mk-pick-button");
                row.Add(choose);
            }
            else
            {
                var parts = MakingTemplates.Split(list[pick.t]);
                int blank = 0;
                for (int i = 0; i < parts.Count; i++)
                {
                    if (i % 2 == 0) { if (parts[i].Length > 0) Text(row, parts[i]); continue; }
                    int k = blank++;
                    if (k >= pick.b.Length) break;
                    Field(row, pick.b[k], templates.blankMaxLength, v => pick.b[k] = v, parts[i], null);
                }
                Small(row, "틀 바꾸기", () => openDial = openDial == key ? null : key);
                Small(row, "굴림", () => PersonaDice.Blanks(pick, list, templates, rng));
                Small(row, "빼기", () => pick.Choose(-1, list));
            }

            if (openDial != key) return;
            // 다이얼: 문장 틀을 옆으로 늘어놓고 하나를 누른다 (사용자 2026-11-26).
            var dial = new ScrollView(ScrollViewMode.Horizontal);
            dial.AddToClassList("mk-dial");
            for (int i = 0; i < list.Count; i++)
            {
                int t = i;
                var chip = new Button(() => { pick.Choose(t, list); openDial = null; Rebuild(); }) { text = MakingTemplates.Preview(list[i]) };
                chip.AddToClassList("mk-chip");
                dial.Add(chip);
            }
            parent.Add(dial);
        }

        // =====================================================================
        // #6. 칸 만드는 도우미
        // =====================================================================

        void Section(string title)
        {
            var l = new Label(title);
            l.AddToClassList("mk-section");
            form.Add(l);
        }

        VisualElement Row(string number)
        {
            var row = new VisualElement();
            row.AddToClassList("mk-row");
            var n = new Label(number);
            n.AddToClassList("mk-num");
            row.Add(n);
            form.Add(row);
            return row;
        }

        static void Text(VisualElement parent, string text)
        {
            var l = new Label(text);
            l.AddToClassList("mk-text");
            parent.Add(l);
        }

        /// <summary>보기 고르는 칸. values는 저장할 값, display는 보이는 글(비우면 값 그대로). 첫 보기는 빈칸이다.</summary>
        void Drop(VisualElement parent, List<string> values, Func<string, string> display, string current, Action<string> set, string emptyText = EmptyChoice)
        {
            var shown = new List<string> { emptyText };
            foreach (var v in values) shown.Add(display != null ? display(v) : v);
            int index = values.IndexOf(current);
            var drop = new DropdownField(shown, index + 1);
            drop.AddToClassList("mk-drop");
            drop.RegisterValueChangedCallback(e =>
            {
                int i = shown.IndexOf(e.newValue) - 1;   // 보이는 글로 값의 자리를 찾는다
                set(i >= 0 && i < values.Count ? values[i] : "");
                openDial = null;
                Rebuild();
            });
            parent.Add(drop);
        }

        static void Field(VisualElement parent, string value, int maxLength, Action<string> set, string placeholder, string extraClass)
        {
            var field = new TextField { value = value ?? "", maxLength = maxLength, tooltip = placeholder };
            field.textEdition.placeholder = placeholder;
            field.AddToClassList("mk-field");
            if (extraClass != null) field.AddToClassList(extraClass);
            field.RegisterValueChangedCallback(e => set(e.newValue.Trim()));
            parent.Add(field);
        }

        void Dice(VisualElement parent, Action roll) => Small(parent, "굴림", roll);

        void Small(VisualElement parent, string text, Action act) => SmallButton(parent, text, act);

        Button SmallButton(VisualElement parent, string text, Action act)
        {
            var b = new Button(() => { act(); Rebuild(); }) { text = text };
            b.AddToClassList("mk-small");
            parent.Add(b);
            return b;
        }
    }
}
