using Improv.Characters;
using UnityEngine;

namespace Improv.World
{
    /// <summary>
    /// 침대 (R-018). 클릭하면 그 캐릭터가 여기에 눕는다.
    /// 침대는 "누가 자고 있나"만 기억한다. 눕고 일어나는 몸의 동작은 CharacterSleep이 한다.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Bed : Interactable
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [Tooltip("누운 그림을 바닥에서 얼마나 올릴지 (침대 높이, 유닛)")]
        [SerializeField] float lieHeight = 0.45f;

        // =====================================================================
        // #2. 상태
        // =====================================================================

        public CharacterSleep Sleeper { get; set; }
        public float LieHeight => lieHeight;
        /// <summary>누울 때 몸(발 기준점)을 옮길 x좌표. 침대 가운데.</summary>
        public float SleepX => InteractionCollider.bounds.center.x;
        /// <summary>누운 그림의 아래 가운데가 닿을 곳 (월드 좌표). 매트리스 윗면 가운데.</summary>
        public Vector3 LieCenter => new(InteractionCollider.bounds.center.x, transform.position.y + lieHeight, 0f);

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Awake() => InteractionCollider = GetComponent<Collider2D>();

        // =====================================================================
        // #4. 상호작용
        // =====================================================================

        public override InteractionResult CanInteract(CharacterInteractor actor)
        {
            var sleep = actor.GetComponent<CharacterSleep>();
            return sleep == null ? InteractionResult.Busy : sleep.CanSleepIn(this);
        }

        public override InteractionResult Interact(CharacterInteractor actor)
        {
            var sleep = actor.GetComponent<CharacterSleep>();
            return sleep == null ? InteractionResult.Busy : sleep.SleepIn(this);
        }
    }
}
