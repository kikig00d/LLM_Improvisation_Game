using UnityEngine;

namespace Improv.World
{
    /// <summary>
    /// 이 충돌체를 지나는 소리가 얼마나 통과하는지 (R-014b, O-007).
    /// 붙이지 않은 구조물(벽 등)은 Hearing의 기본 통과율을 쓴다. 문은 Door가 열림/닫힘에 따라 따로 정한다.
    /// </summary>
    public class SoundObstacle : MonoBehaviour
    {
        [Tooltip("0 = 완전히 막음, 1 = 그대로 통과. 들리는 거리에 곱해진다.")]
        [SerializeField, Range(0f, 1f)] float transmission = 0.3f;

        public float Transmission => transmission;
    }
}
