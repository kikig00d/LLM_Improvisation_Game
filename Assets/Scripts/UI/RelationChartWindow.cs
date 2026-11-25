using System.Collections.Generic;
using Improv.Characters;
using Improv.Npc;
using Improv.Player;
using Improv.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Improv.UI
{
    /// <summary>
    /// 관계도 창 (계획서 T-9, GDD R-028).
    /// - 플레이어 보기: 플레이어가 본 적 있는 사람만 칸으로 나온다(P-1, P-3). 칸 둘을 차례로 눌러 "A가 B를 어떻게 여기나"를
    ///   느낌 표식과 메모로 적는다. 플레이어의 짐작일 뿐, NPC의 진짜 마음과는 따로다.
    /// - 개발자 보기: 에디터와 개발 빌드에서만. NPC의 진짜 호감·애정 숫자(RelationBook)를 주황 화살표로 겹쳐 그린다.
    /// R로 열고 닫는다(Esc도 닫음). 열려 있는 동안 시계를 멈추고 플레이어 조작을 끈다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class RelationChartWindow : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [SerializeField] string toggleActionName = "Game/Relations";
        [SerializeField] DialogueWindow dialogueWindow;   // 비우면 씬에서 찾는다. 대화 입력 중에는 R이 글자다
        [Tooltip("플레이어가 본 사람을 몇 초마다 확인할지 (실제 초)")]
        [SerializeField] float seenCheckInterval = 0.5f;

        static readonly Color[] MarkColors =
        {
            new(0.6f, 0.6f, 0.65f),      // 모름 (그리지 않음)
            new(0.90f, 0.30f, 0.30f),    // 미움
            new(0.95f, 0.62f, 0.25f),    // 꺼림
            new(0.70f, 0.72f, 0.78f),    // 무덤덤
            new(0.40f, 0.82f, 0.45f),    // 호감
            new(0.95f, 0.50f, 0.75f),    // 친애
        };
        readonly List<Button> markButtons = new();   // 표식 단추. 고른 것을 밝게 보인다
        static readonly Color DevColor = new(1f, 0.65f, 0.1f, 0.9f);

        // =====================================================================
        // #2. 상태
        // =====================================================================

        UIDocument document;
        VisualElement overlay, graphArea, nodeLayer, labelLayer, editor, markRow;
        RelationGraphElement graph;
        Label pairLabel, hintLabel;
        TextField memoField;
        Button devButton, swapButton, clearButton;
        InputAction toggleAction;

        public readonly PlayerRelationNotes notes = new();
        readonly HashSet<int> seen = new();          // 플레이어가 한 번이라도 본 사람 id
        readonly List<Improv.Characters.Sight.SightTarget> seenBuffer = new();
        Improv.Characters.Sight.Vision playerVision;
        int playerId;
        float seenTimer;

        readonly Dictionary<int, Vector2> nodePos = new();   // id → 칸 가운데 (graphArea 안 좌표)
        int selectedFrom = -1, selectedTo = -1, firstPick = -1;
        bool devView;
        bool clockWasRunning;
        readonly List<(Behaviour b, bool wasEnabled)> pausedControls = new();

        public bool IsOpen { get; private set; }

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Awake()
        {
            document = GetComponent<UIDocument>();
            toggleAction = InputSystem.actions?.FindAction(toggleActionName);
            if (toggleAction == null) Debug.LogError($"[RelationChartWindow] 입력 액션 '{toggleActionName}'을 찾지 못했습니다.", this);
            if (dialogueWindow == null) dialogueWindow = FindFirstObjectByType<DialogueWindow>();
        }

        void OnEnable()
        {
            var root = document.rootVisualElement;
            overlay = root.Q("relations-overlay");
            graphArea = root.Q("graph-area");
            editor = root.Q("editor");
            markRow = root.Q("mark-row");
            pairLabel = root.Q<Label>("pair-label");
            hintLabel = root.Q<Label>("hint");
            memoField = root.Q<TextField>("memo");
            devButton = root.Q<Button>("dev-button");
            swapButton = root.Q<Button>("swap-button");
            clearButton = root.Q<Button>("clear-button");
            if (overlay == null || graphArea == null || memoField == null)
            {
                Debug.LogError("[RelationChartWindow] UXML에서 relations-overlay, graph-area, memo를 찾지 못했습니다.", this);
                enabled = false;
                return;
            }

            // 그리는 판(화살표) → 칸 → 글자 순으로 쌓는다. 나중에 넣은 것이 위에 그려진다.
            graph = new RelationGraphElement { name = "arrows" };
            graph.style.position = Position.Absolute;
            graph.style.left = 0; graph.style.top = 0; graph.style.right = 0; graph.style.bottom = 0;
            nodeLayer = Layer("nodes");
            labelLayer = Layer("arrow-labels");
            graphArea.Add(graph);
            graphArea.Add(nodeLayer);
            graphArea.Add(labelLayer);
            graphArea.RegisterCallback<GeometryChangedEvent>(_ => Rebuild());
            graphArea.RegisterCallback<PointerDownEvent>(e => { if (e.target == graphArea) { firstPick = -1; Rebuild(); } });

            for (int i = 1; i < PlayerRelationNotes.MarkNames.Length; i++)
            {
                var mark = (PlayerRelationNotes.Mark)i;   // 람다가 붙잡을 값은 반복문 안에서 복사해 둔다
                var b = new Button(() => SetMark(mark)) { text = PlayerRelationNotes.MarkNames[i] };
                b.AddToClassList("mark-button");
                b.style.borderBottomColor = MarkColors[i];
                markRow.Add(b);
                markButtons.Add(b);
            }
            memoField.maxLength = PlayerRelationNotes.MaxMemoLength;
            memoField.RegisterValueChangedCallback(e =>
            {
                if (selectedFrom < 0) return;
                notes.Get(selectedFrom, selectedTo).memo = e.newValue;
                Rebuild();
            });
            // Esc는 입력칸에 있어도 창을 닫는다. 다른 키는 입력칸이 먹는다.
            memoField.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Escape) { Close(); e.StopPropagation(); } }, TrickleDown.TrickleDown);
            swapButton.clicked += () => { if (selectedFrom >= 0) Select(selectedTo, selectedFrom); };
            clearButton.clicked += () => { if (selectedFrom >= 0) { notes.Clear(selectedFrom, selectedTo); Select(selectedFrom, selectedTo); } };
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            devButton.clicked += () => { devView = !devView; Rebuild(); };
#else
            devButton.style.display = DisplayStyle.None;   // 출시 빌드에서는 진짜 마음을 볼 수 없다
#endif
            SetVisible(false);
        }

        VisualElement Layer(string layerName)
        {
            var e = new VisualElement { name = layerName, pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            e.style.left = 0; e.style.top = 0; e.style.right = 0; e.style.bottom = 0;
            return e;
        }

        void Start()
        {
            // 플레이어 = id 0 (CharacterIdentity). 플레이어의 눈으로 본 사람만 칸이 된다 (P-1).
            var player = CharacterIdentity.Find(0);
            if (player != null) { playerId = player.Id; playerVision = player.GetComponent<Improv.Characters.Sight.Vision>(); }
            seen.Add(playerId);
        }

        void Update()
        {
            seenTimer -= Time.unscaledDeltaTime;   // 시계가 멈춰도 본 사람은 센다
            if (seenTimer <= 0f) { seenTimer = seenCheckInterval; CollectSeen(); }

            if (toggleAction == null) return;
            bool pressed = toggleAction.WasPressedThisFrame();
            if (Keyboard.current != null && IsOpen && Keyboard.current.escapeKey.wasPressedThisFrame) { Close(); return; }
            if (!pressed) return;
            if (IsOpen)
            {
                // 메모를 치는 중이면 R은 글자다 (두벌식에서 R = ㄱ).
                if (memoField.panel?.focusController?.focusedElement == memoField) return;
                Close();
            }
            else Open();
        }

        void CollectSeen()
        {
            if (playerVision == null) return;
            playerVision.CollectVisible(seenBuffer);
            bool added = false;
            foreach (var t in seenBuffer)
            {
                var id = t.GetComponent<CharacterIdentity>();
                if (id != null && seen.Add(id.Id)) added = true;
            }
            if (added && IsOpen) Rebuild();
        }

        // =====================================================================
        // #4. 열고 닫기
        // =====================================================================

        public void Open()
        {
            if (IsOpen) return;
            if (dialogueWindow != null && dialogueWindow.IsOpen) return;   // 대화 입력 중 R은 글자다
            var clock = GameClock.Instance;
            if (clock != null && clock.IsPlayerSleeping) return;              // 자는 동안은 열지 않는다 [가정]
            IsOpen = true;
            CollectSeen();

            // 추리하는 동안 산장의 시간은 멈춘다 (사용자 2026-11-25). 닫으면 원래 속도로.
            clockWasRunning = clock != null && !clock.IsPaused;
            if (clockWasRunning) clock.TogglePause();

            // 메모를 치는 동안 WASD로 걷거나 P로 시간을 바꾸면 안 된다. 플레이어 조작을 모두 꺼 둔다 (대화창 E-003과 같은 방식).
            pausedControls.Clear();
            foreach (var b in FindObjectsByType<Behaviour>(FindObjectsSortMode.None))
                if (b is PlayerInputDriver || b is PlayerClickInteraction || b is PlayerHoverFeedback || b is PlayerSpeechInput
                    || b is PlayerUseInput || b is PlayerGazeInput || b is GameSpeedInput)
                {
                    pausedControls.Add((b, b.enabled));
                    b.enabled = false;
                }

            firstPick = -1;
            SetVisible(true);
            Select(-1, -1);
            graphArea.schedule.Execute(Rebuild);   // 크기가 정해진 다음 프레임에 칸을 놓는다
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            memoField.Blur();
            SetVisible(false);
            foreach (var (b, wasEnabled) in pausedControls) if (b != null) b.enabled = wasEnabled;
            pausedControls.Clear();
            var clock = GameClock.Instance;
            if (clockWasRunning && clock != null && clock.IsPaused) clock.TogglePause();
        }

        void SetVisible(bool visible) => overlay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        // =====================================================================
        // #5. 고르기와 적기
        // =====================================================================

        void OnNodeClicked(int id)
        {
            if (firstPick < 0 || firstPick == id) { firstPick = firstPick == id ? -1 : id; Rebuild(); return; }
            Select(firstPick, id);
            firstPick = -1;
        }

        void Select(int from, int to)
        {
            selectedFrom = from; selectedTo = to;
            bool has = from >= 0 && to >= 0;
            editor.SetEnabled(has);
            if (has)
            {
                pairLabel.text = $"{NameOf(from)} → {NameOf(to)}";
                notes.TryGet(from, to, out var n);
                memoField.SetValueWithoutNotify(n?.memo ?? "");
            }
            else
            {
                pairLabel.text = "사람 둘을 차례로 누르세요";
                memoField.SetValueWithoutNotify("");
            }
            Rebuild();
        }

        void SetMark(PlayerRelationNotes.Mark mark)
        {
            if (selectedFrom < 0) return;
            var n = notes.Get(selectedFrom, selectedTo);
            n.mark = n.mark == mark ? PlayerRelationNotes.Mark.Unknown : mark;   // 같은 걸 다시 누르면 지운다
            Rebuild();
        }

        string NameOf(int id)
        {
            if (id == playerId) return "나";
            return CharacterIdentity.Find(id)?.DisplayName ?? "?";
        }

        // =====================================================================
        // #6. 그리기
        // =====================================================================

        void Rebuild()
        {
            if (!IsOpen || graph == null) return;
            float w = graphArea.resolvedStyle.width, h = graphArea.resolvedStyle.height;
            if (float.IsNaN(w) || w < 10f) return;

            // 칸에 놓을 사람: 플레이어가 본 사람. 개발자 보기에서는 모두.
            var ids = new List<int>();
            foreach (var c in CharacterIdentity.All) if (devView || seen.Contains(c.Id)) ids.Add(c.Id);
            ids.Sort();   // 순서가 늘 같아야 칸 자리가 흔들리지 않는다

            // 타원 위에 고르게 놓는다. 맨 위(12시)부터 시계 방향.
            nodePos.Clear();
            Vector2 center = new(w * 0.5f, h * 0.5f);
            float rx = w * 0.5f - 70f, ry = h * 0.5f - 60f;
            for (int i = 0; i < ids.Count; i++)
            {
                float angle = -Mathf.PI / 2f + 2f * Mathf.PI * i / Mathf.Max(1, ids.Count);
                nodePos[ids[i]] = ids.Count == 1 ? center : center + new Vector2(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * ry);
            }

            nodeLayer.Clear();
            labelLayer.Clear();
            graph.arrows.Clear();

            // 플레이어의 짐작 화살표
            foreach (var kv in notes.All)
            {
                var (from, to) = kv.Key;
                var n = kv.Value;
                if (n.IsEmpty || !nodePos.ContainsKey(from) || !nodePos.ContainsKey(to)) continue;
                var arrow = new RelationGraphElement.Arrow
                {
                    from = nodePos[from], to = nodePos[to], side = 7f, width = (from == selectedFrom && to == selectedTo) ? 5f : 3f,
                    color = n.mark == PlayerRelationNotes.Mark.Unknown ? new Color(0.8f, 0.8f, 0.85f, 0.8f) : MarkColors[(int)n.mark],
                };
                graph.arrows.Add(arrow);
                string memo = n.memo.Length > 10 ? n.memo.Substring(0, 10) + "…" : n.memo;
                string text = n.mark == PlayerRelationNotes.Mark.Unknown ? memo : (memo.Length > 0 ? $"{PlayerRelationNotes.MarkNames[(int)n.mark]} · {memo}" : PlayerRelationNotes.MarkNames[(int)n.mark]);
                AddArrowLabel(arrow, text, "arrow-label", from, to);
            }

            // 개발자 보기: NPC의 진짜 마음 (R-027). 주황색, 플레이어 화살표보다 더 비켜 긋는다.
            if (devView)
                foreach (var brain in FindObjectsByType<NpcDialogueBrain>(FindObjectsSortMode.None))
                {
                    var book = brain.Relations;
                    if (book == null) continue;
                    foreach (var r in book.All)
                    {
                        if (r.targetId == book.selfId || !nodePos.ContainsKey(book.selfId) || !nodePos.ContainsKey(r.targetId)) continue;
                        var arrow = new RelationGraphElement.Arrow { from = nodePos[book.selfId], to = nodePos[r.targetId], side = 20f, width = 2f, color = DevColor };
                        graph.arrows.Add(arrow);
                        string text = $"호감 {r.Affinity(Time.time)}" + (r.faces.Count > 0 ? $" 애정 {r.Affection(Time.time)}" : "");
                        AddArrowLabel(arrow, text, "arrow-label--dev", book.selfId, r.targetId, 14f);
                    }
                }

            // 사람 칸
            foreach (int id in ids)
            {
                var node = new VisualElement();
                node.AddToClassList("node");
                if (id == playerId) node.AddToClassList("node--self");
                if (id == firstPick || id == selectedFrom || id == selectedTo) node.AddToClassList("node--picked");
                node.style.left = nodePos[id].x - graph.nodeRadius;
                node.style.top = nodePos[id].y - graph.nodeRadius;
                node.Add(new Label(NameOf(id)) { pickingMode = PickingMode.Ignore });
                if (devView)
                {
                    // 개발자 보기: 이제까지의 나(자기 자신에 대한 호감)
                    foreach (var brain in FindObjectsByType<NpcDialogueBrain>(FindObjectsSortMode.None))
                        if (brain.Relations != null && brain.Relations.selfId == id)
                        {
                            var self = new Label($"나 {brain.Relations.Get(id).Affinity(Time.time)}") { pickingMode = PickingMode.Ignore };
                            self.AddToClassList("node-dev");
                            node.Add(self);
                        }
                }
                int captured = id;
                node.RegisterCallback<PointerDownEvent>(e => { OnNodeClicked(captured); e.StopPropagation(); });
                nodeLayer.Add(node);
            }

            graph.MarkDirtyRepaint();   // 화살표 다시 그리기
            var current = selectedFrom >= 0 && notes.TryGet(selectedFrom, selectedTo, out var sel) ? sel.mark : PlayerRelationNotes.Mark.Unknown;
            for (int i = 0; i < markButtons.Count; i++)
                markButtons[i].EnableInClassList("mark-button--on", (int)current == i + 1);
            hintLabel.text = firstPick >= 0
                ? $"{NameOf(firstPick)}이(가) 누구를 어떻게 여기나? 둘째 사람을 누르세요"
                : "사람 둘을 차례로 눌러 화살표를 고릅니다 · R/Esc 닫기";
            devButton.text = devView ? "개발자 보기 끄기" : "개발자 보기";
        }

        void AddArrowLabel(RelationGraphElement.Arrow arrow, string text, string cls, int from, int to, float extraSide = 12f)
        {
            if (string.IsNullOrEmpty(text)) return;
            // 플레이어 글자는 출발 쪽 3분의 1, 개발자 글자는 3분의 2 지점. 같은 화살표에서도 서로 안 겹친다.
            Vector2 mid = RelationGraphElement.PointAt(arrow, cls == "arrow-label" ? 0.33f : 0.62f, extraSide);
            var label = new Label(text);
            label.AddToClassList("arrow-label");
            if (cls != "arrow-label") label.AddToClassList(cls);
            label.style.left = mid.x;
            label.style.top = mid.y;
            // 플레이어 화살표 글자를 누르면 그 화살표를 고른다
            if (cls == "arrow-label") label.RegisterCallback<PointerDownEvent>(e => { Select(from, to); e.StopPropagation(); });
            else label.pickingMode = PickingMode.Ignore;
            labelLayer.Add(label);
        }
    }
}
