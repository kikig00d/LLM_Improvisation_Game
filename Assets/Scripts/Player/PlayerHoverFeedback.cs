using Improv.Characters;
using Improv.World;
using UnityEngine;

namespace Improv.Player
{
    /// <summary>
    /// 마우스 커서가 가리키는 곳을 보고, 클릭하면 어떻게 될지 미리 보여준다.
    ///  - 상호작용 대상 위: 가능하면 하얀 빛 + 손 커서, 불가능하면 회색 테두리 + 금지 커서
    ///  - 무언가를 들고 빈 곳 위: 실제로 놓일 자리에 반투명 미리보기 + 내려놓기 커서 (불가능하면 붉게)
    /// 판정은 CharacterInteractor의 Preview 함수가 한다. 여기서는 보여주기만 한다. (설계 원칙 P-2)
    /// </summary>
    [RequireComponent(typeof(CharacterInteractor), typeof(PlayerClickInteraction))]
    public class PlayerHoverFeedback : MonoBehaviour
    {
        // ==========================================
        // #1. 설정값
        // ==========================================

        [System.Serializable]
        public struct CursorIcon
        {
            public Texture2D texture;   // 비어 있으면 운영체제 기본 커서를 쓴다
            public Vector2 hotspot;     // 그림에서 "클릭되는 점"의 픽셀 위치 (왼쪽 위 기준)
        }

        [SerializeField] CursorIcon defaultCursor;
        [SerializeField] CursorIcon interactCursor;     // 상호작용 가능
        [SerializeField] CursorIcon unavailableCursor;  // 대상이지만 지금은 불가능
        [SerializeField] CursorIcon dropCursor;         // 들고 있는 것을 놓을 수 있음

        [SerializeField] Color ghostValidColor = new(1f, 1f, 1f, 0.45f);
        [SerializeField] Color ghostInvalidColor = new(1f, 0.3f, 0.3f, 0.35f);

        // ==========================================
        // #2. 상태
        // ==========================================

        CharacterInteractor interactor;
        PlayerClickInteraction click;
        HoverHighlight highlighted;       // 지금 빛나고 있는 대상
        SpriteRenderer ghost;             // 내려놓을 자리 미리보기
        Texture2D appliedCursor;          // 같은 커서를 매 프레임 다시 설정하지 않으려고 기억

        // 테스트와 디버그용: 마지막으로 판단한 결과
        public InteractionResult LastPreview { get; private set; }
        public string LastCursorName { get; private set; } = "default";

        // ==========================================
        // #3. 유니티 생명주기
        // ==========================================

        void Awake()
        {
            interactor = GetComponent<CharacterInteractor>();
            click = GetComponent<PlayerClickInteraction>();

            ghost = new GameObject("DropGhost").AddComponent<SpriteRenderer>();
            ghost.enabled = false;
        }

        void Update() => Refresh(click.PointerWorldPosition());

        void OnDisable()
        {
            SetHighlight(null, HoverHighlight.State.None);
            if (ghost != null) ghost.enabled = false;
            ApplyCursor(defaultCursor, "default");
        }

        // ==========================================
        // #4. 판단과 표시 (테스트에서 좌표를 직접 넣을 수 있게 public)
        // ==========================================

        public void Refresh(Vector2 worldPoint)
        {
            var target = click.FindVisibleTarget(worldPoint);

            if (target != null)
            {
                ghost.enabled = false;
                LastPreview = interactor.PreviewInteract(target);
                bool ok = LastPreview == InteractionResult.Done;
                SetHighlight(target.GetComponent<HoverHighlight>(), ok ? HoverHighlight.State.Available : HoverHighlight.State.Unavailable);
                ApplyCursor(ok ? interactCursor : unavailableCursor, ok ? "interact" : "unavailable");
                return;
            }

            SetHighlight(null, HoverHighlight.State.None);

            if (interactor.Held == null)
            {
                ghost.enabled = false;
                LastPreview = InteractionResult.NothingHeld;
                ApplyCursor(defaultCursor, "default");
                return;
            }

            LastPreview = interactor.PreviewDrop(worldPoint, out var dropPosition);
            ShowGhost(LastPreview, dropPosition, worldPoint);
            bool canDrop = LastPreview == InteractionResult.Done;
            ApplyCursor(canDrop ? dropCursor : unavailableCursor, canDrop ? "drop" : "unavailable");
        }

        void ShowGhost(InteractionResult result, Vector2 dropPosition, Vector2 worldPoint)
        {
            var source = interactor.Held.MainRenderer;
            // 다른 층을 가리키면 미리보기를 숨긴다. 어디에 놓일지 보여줄 수 없으니까.
            if (source == null || result == InteractionResult.WrongFloor || result == InteractionResult.Busy)
            {
                ghost.enabled = false;
                return;
            }

            bool ok = result == InteractionResult.Done;
            // 성공이면 실제로 놓일 자리, 실패면 가리킨 자리(같은 층 바닥)에 보여준다.
            Vector2 floor = ok ? dropPosition : new Vector2(worldPoint.x, interactor.GetComponent<CharacterMotor>().FeetPosition.y);
            var offset = source.transform.position - interactor.Held.transform.position;

            ghost.enabled = true;
            ghost.sprite = source.sprite;
            ghost.transform.position = (Vector3)floor + offset;
            ghost.transform.localScale = source.transform.lossyScale;
            ghost.sortingOrder = 6;
            ghost.color = ok ? ghostValidColor : ghostInvalidColor;
        }

        void SetHighlight(HoverHighlight target, HoverHighlight.State state)
        {
            if (highlighted != null && highlighted != target) highlighted.SetState(HoverHighlight.State.None);
            highlighted = target;
            if (highlighted != null) highlighted.SetState(state);
        }

        void ApplyCursor(CursorIcon icon, string label)
        {
            LastCursorName = label;
            if (icon.texture == appliedCursor) return;
            appliedCursor = icon.texture;
            // texture가 null이면 운영체제 기본 커서로 돌아간다.
            Cursor.SetCursor(icon.texture, icon.hotspot, CursorMode.Auto);
        }
    }
}
