using UnityEngine;

namespace Improv.World
{
    /// <summary>
    /// 먹을 수 있는 것 표시 (R-020 배고픔). 운반 가능 오브젝트(Carryable)에 함께 붙인다.
    /// 들어서 옮길 수 있고(창고에 쌓아 두기, 숨기기 등), 든 채로 먹으면 배고픔이 차고 사라진다 (CharacterVitals.TryEatHeld).
    /// 그래서 "창고에 식량이 없다" 같은 소 사건의 재료가 된다.
    /// </summary>
    [RequireComponent(typeof(Carryable))]
    public class Food : MonoBehaviour
    {
        [Tooltip("먹으면 차는 배고픔 (0~100). O-005 [가정]")]
        [SerializeField] float nutrition = 35f;

        public float Nutrition => nutrition;
    }
}
