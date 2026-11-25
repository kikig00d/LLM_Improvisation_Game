using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

namespace Improv.UI
{
    /// <summary>
    /// 대화창 (R-014, R-015, R-015a). 단어 사전에서 고른 단어로만 문장을 만든다.
    /// 화면만 담당한다. 누가 열고, 보낸 문장을 누가 말할지는 PlayerSpeechInput이 정한다.
    /// 조작: 글자를 치면 후보가 뜬다 → 스페이스/탭으로 고르기, ↑↓로 후보 바꾸기, 엔터로 보내기, Esc로 닫기.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class DialogueWindow : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [SerializeField] TextAsset wordList;          // Assets/Data/Dialogue/Words.txt
        [SerializeField] int maxCandidates = 8;
        [SerializeField] int maxHistoryLines = 60;
        [Tooltip("대화 기록 한 줄이 남는 시간 (초). R-016c, O-008 [가정]")]
        [SerializeField] float historyLifetime = 60f;
        [Tooltip("사라지기 전에 흐려지는 시간 (초)")]
        [SerializeField] float historyFadeDuration = 8f;
        [Tooltip("F-17 원인 찾기용: 입력칸 글자와 한글 조합이 바뀔 때마다 콘솔에 남긴다. 원인을 찾으면 끈다.")]
        [SerializeField] bool logTyping = true;
        [SerializeField] string defaultHint = "글자를 치면 후보가 나와요 · 스페이스/탭: 고르기 · ↑↓: 후보 바꾸기 · 엔터: 보내기 · Esc: 닫기";

        // =====================================================================
        // #2. 상태
        // =====================================================================

        UIDocument document;
        VisualElement window, chips, candidatesBox;
        ScrollView history;
        TextField input;
        Label hint;

        WordDictionary dictionary;
        readonly List<string> sentence = new();     // 고른 단어들. 고칠 수 없다 (R-015a)
        List<string> candidates = new();
        int selected;
        string composition = "";                    // 한글 조합 중인 글자 (예: "차"를 치는 중)

        public bool IsOpen { get; private set; }
        public WordDictionary Dictionary => dictionary;
        public IReadOnlyList<string> Candidates => candidates;
        public IReadOnlyList<string> Sentence => sentence;

        public event Action Opened;
        public event Action Closed;
        public event Action<string> Submitted;      // 완성된 문장을 보낸다

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Awake()
        {
            document = GetComponent<UIDocument>();
            if (wordList == null)
            {
                Debug.LogError("[DialogueWindow] 단어 사전(wordList)이 비어 있습니다.", this);
                enabled = false;
                return;
            }
            dictionary = WordDictionary.Parse(wordList.text);
        }

        // 사람 이름은 사전 파일이 아니라 지금 씬의 캐릭터 이름으로 채운다 (F-17). 개명하거나 사람이 늘면 다시 채운다.
        void RefreshNames()
        {
            if (dictionary == null) return;
            var current = new List<string>();
            foreach (var c in Improv.Characters.CharacterIdentity.All) current.Add(c.DisplayName);
            dictionary.SetNames(current);
            if (IsOpen) RefreshCandidates();
        }

        // UIDocument는 OnEnable 때 화면을 새로 만들기 때문에, 요소 찾기와 연결도 OnEnable에서 한다.
        void OnEnable()
        {
            VisualElement root = document.rootVisualElement;
            window = root.Q("dialogue-window");
            history = root.Q<ScrollView>("history");
            chips = root.Q("chips");
            input = root.Q<TextField>("word-input");
            candidatesBox = root.Q("candidates");
            hint = root.Q<Label>("hint");

            if (window == null || input == null)
            {
                Debug.LogError("[DialogueWindow] UXML에서 dialogue-window나 word-input을 찾지 못했습니다.", this);
                return;
            }

            // TrickleDown: 이벤트가 입력칸에 도착하기 "전에" 먼저 가로챈다.
            // 그래야 스페이스가 글자로 들어가거나 탭이 다른 칸으로 넘어가기 전에 처리할 수 있다.
            input.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            input.RegisterCallback<NavigationMoveEvent>(e => e.StopPropagation(), TrickleDown.TrickleDown);
            input.RegisterCallback<NavigationSubmitEvent>(e => e.StopPropagation(), TrickleDown.TrickleDown);
            input.RegisterValueChangedCallback(e =>
            {
                if (logTyping) Debug.Log($"[대화창 입력] 값 '{e.previousValue}' → '{e.newValue}', 조합 중 '{composition}', 커서 {input.cursorIndex}, 포커스 {input.panel?.focusController?.focusedElement == input}", this);
                RefreshCandidates();
            });

            if (Keyboard.current != null) Keyboard.current.onIMECompositionChange += OnImeComposition;
            Improv.Characters.CharacterIdentity.RosterChanged += RefreshNames;
            RefreshNames();

            SetVisible(IsOpen);
            RefreshAll();
        }

        void OnDisable()
        {
            if (Keyboard.current != null) Keyboard.current.onIMECompositionChange -= OnImeComposition;
            Improv.Characters.CharacterIdentity.RosterChanged -= RefreshNames;
        }

        // =====================================================================
        // #4. 열고 닫기, 기록
        // =====================================================================

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            sentence.Clear();
            ClearInput();
            composition = "";
            SetVisible(true);
            RefreshAll();
            // 화면에 나타난 다음 프레임에 입력칸에 커서를 둔다. 바로 부르면 아직 그려지지 않아 무시될 수 있다.
            input.schedule.Execute(() => input.Focus());
            Opened?.Invoke();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            sentence.Clear();                 // R-015a: 보내지 않은 문장은 버린다
            input.Blur();
            SetVisible(false);
            Closed?.Invoke();
        }

        /// <summary>대화 기록에 한 줄 추가한다. 플레이어 귀에 들린 말만 들어온다 (R-016a).</summary>
        public void AddHistory(string speakerName, string text, bool isSelf)
        {
            if (history == null) return;
            // userData: 요소마다 아무 값이나 달아 둘 수 있는 칸. 들은 시각을 적어 두고 휘발 계산에 쓴다.
            var line = new Label($"{speakerName}: {text}") { userData = Time.time };
            line.AddToClassList("history-line");
            if (isSelf) line.AddToClassList("history-line--self");
            history.Add(line);

            while (history.contentContainer.childCount > maxHistoryLines)
                history.contentContainer.RemoveAt(0);

            // 크기가 계산된 뒤에 맨 아래로 내린다.
            history.schedule.Execute(() => history.ScrollTo(line));
        }

        // R-016c: 대화는 영구히 남지 않는다. 오래된 줄은 흐려지다가 사라진다. 창이 닫혀 있어도 시간은 흐른다.
        void Update()
        {
            if (history == null) return;
            var lines = history.contentContainer;
            for (int i = lines.childCount - 1; i >= 0; i--)
            {
                VisualElement line = lines[i];
                if (line.userData is not float heardAt) continue;

                float age = Time.time - heardAt;
                if (age >= historyLifetime) { line.RemoveFromHierarchy(); continue; }

                float fadeStart = historyLifetime - historyFadeDuration;
                line.style.opacity = age <= fadeStart ? 1f : 1f - (age - fadeStart) / historyFadeDuration;
            }
        }

        // =====================================================================
        // #5. 키 입력
        // =====================================================================

        void OnKeyDown(KeyDownEvent e)
        {
            if (logTyping) Debug.Log($"[대화창 입력] 키 {e.keyCode} 글자 {(int)e.character} 값 '{input.value}' 커서 {input.cursorIndex}", this);
            // 키 하나를 누르면 "키 이벤트"와 "글자 이벤트"가 따로 온다. 글자 쪽의 공백·엔터·탭은 삼켜서 입력칸에 안 들어가게 한다.
            if (e.keyCode == KeyCode.None)
            {
                // '\b'(백스페이스 글자)도 삼킨다. 지우기는 아래 Backspace 키에서 직접 한다 (F-18).
                if (e.character == ' ' || e.character == '\n' || e.character == '\r' || e.character == '\t' || e.character == '\b')
                    e.StopImmediatePropagation();
                return;
            }

            switch (e.keyCode)
            {
                case KeyCode.Escape:
                    Close();
                    break;
                case KeyCode.Space:
                case KeyCode.Tab:
                    CommitSelected();
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    // 치던 글자가 있으면 먼저 그 단어를 고르고, 없으면 문장을 보낸다.
                    if (TypedText().Length > 0) CommitSelected();
                    else SendSentence();
                    break;
                case KeyCode.Backspace:
                    // F-18: 유니티 입력칸이 백스페이스를 받아도 글자를 지우지 않았다(코드로 키 이벤트를 보내 재현).
                    // 한글을 조합하는 중이면 입력기가 자모를 지우게 두고, 조합이 끝난 글자만 여기서 직접 지운다.
                    if (composition.Length > 0) return;
                    DeleteBackward();
                    break;
                case KeyCode.UpArrow:
                    MoveSelection(-1);
                    break;
                case KeyCode.DownArrow:
                    MoveSelection(1);
                    break;
                default:
                    return;   // 나머지 키(글자, 백스페이스)는 입력칸이 원래대로 처리한다
            }
            e.StopImmediatePropagation();
        }

        /// <summary>커서 앞 글자 하나를 지운다. 여러 글자를 골라 둔 상태면 고른 부분을 지운다.</summary>
        void DeleteBackward()
        {
            string value = input.value ?? "";
            // 커서와 선택 끝 중 작은 쪽이 시작, 큰 쪽이 끝. 커서가 글자 수를 넘어 있을 수도 있어 길이로 자른다.
            int start = Mathf.Clamp(Mathf.Min(input.cursorIndex, input.selectIndex), 0, value.Length);
            int end = Mathf.Clamp(Mathf.Max(input.cursorIndex, input.selectIndex), 0, value.Length);
            if (start == end)
            {
                if (start == 0) return;   // 지울 글자가 없다
                start--;
            }
            input.value = value.Remove(start, end - start);   // 값이 바뀌면 후보도 다시 계산된다
            input.SelectRange(start, start);
        }

        void OnImeComposition(IMECompositionString text)
        {
            if (logTyping) Debug.Log($"[대화창 입력] 조합 '{composition}' → '{text}', 값 '{input?.value}'", this);
            composition = text.ToString();
            RefreshCandidates();
        }

        // =====================================================================
        // #6. 단어 고르기와 보내기 (테스트에서 직접 부를 수 있게 public)
        // =====================================================================

        /// <summary>
        /// 입력칸을 비운다. 글자만 지우면 입력칸 속 커서·선택 위치가 옛 글자 길이에 남아,
        /// 다음 글자를 칠 때 유니티 내부에서 ArgumentOutOfRangeException이 난다(T-4 직접 확인 때 로그에서 발견). 커서도 맨 앞으로 돌린다.
        /// </summary>
        void ClearInput()
        {
            input.SetValueWithoutNotify("");
            input.SelectRange(0, 0);
        }

        // 입력칸 값에는 조합 중인 글자가 이미 들어 있을 때가 많다(대화창 입력 로그로 확인). 그때는 두 번 붙이지 않는다.
        public string TypedText()
        {
            string value = input?.value ?? "";
            return (composition.Length > 0 && !value.EndsWith(composition) ? value + composition : value).Trim();
        }

        public void CommitSelected()
        {
            if (candidates.Count == 0)
            {
                if (TypedText().Length > 0) ShowHint("사전에 없는 단어예요. 다른 말로 해 보세요.");
                return;
            }
            sentence.Add(candidates[selected]);
            ClearInput();
            composition = "";
            RefreshAll();
        }

        public void SendSentence()
        {
            if (sentence.Count == 0) return;
            string text = WordDictionary.JoinSentence(sentence);
            sentence.Clear();
            RefreshAll();
            Submitted?.Invoke(text);
        }

        /// <summary>테스트용: 한글 조합 없이 입력칸 글자를 바로 넣는다.</summary>
        public void SetTypedText(string text)
        {
            composition = "";
            input.value = text;   // 값이 바뀌면 후보도 다시 계산된다
        }

        void MoveSelection(int delta)
        {
            if (candidates.Count == 0) return;
            selected = (selected + delta + candidates.Count) % candidates.Count;
            RefreshCandidateButtons();
        }

        // =====================================================================
        // #7. 화면 갱신
        // =====================================================================

        void RefreshAll()
        {
            RefreshChips();
            RefreshCandidates();
        }

        void RefreshChips()
        {
            if (chips == null) return;
            chips.Clear();
            foreach (string word in sentence)
            {
                var chip = new Label(word);
                chip.AddToClassList("chip");
                chips.Add(chip);
            }
        }

        void RefreshCandidates()
        {
            candidates = dictionary?.FindCandidates(TypedText(), maxCandidates) ?? new List<string>();
            selected = 0;
            RefreshCandidateButtons();

            if (TypedText().Length > 0 && candidates.Count == 0) ShowHint("사전에 없는 단어예요. 다른 말로 해 보세요.");
            else ShowHint(defaultHint);
        }

        void RefreshCandidateButtons()
        {
            if (candidatesBox == null) return;
            candidatesBox.Clear();
            for (int i = 0; i < candidates.Count; i++)
            {
                int index = i;   // 람다가 반복 변수 i를 그대로 잡으면 모두 마지막 값이 되므로 복사해 둔다
                var button = new Button(() => { selected = index; CommitSelected(); input.Focus(); }) { text = candidates[i] };
                button.AddToClassList("candidate");
                if (i == selected) button.AddToClassList("candidate--selected");
                button.focusable = false;   // 버튼을 눌러도 입력칸의 커서를 빼앗지 않게
                candidatesBox.Add(button);
            }
        }

        void ShowHint(string text)
        {
            if (hint != null) hint.text = text;
        }

        void SetVisible(bool visible)
        {
            if (window != null) window.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
