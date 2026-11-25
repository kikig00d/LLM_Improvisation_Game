using Improv.Characters;
using UnityEngine;

namespace Improv.Player
{
    /// <summary>
    /// 플레이어가 죽으면(R-022) 모든 플레이어 입력을 끈다. 패배 화면은 VitalsView가 그린다.
    /// 나중에 후일담(Epilogue, GDD 상태표)으로 넘기는 일도 여기서 한다.
    /// </summary>
    [RequireComponent(typeof(CharacterVitals))]
    public class PlayerDeathHandler : MonoBehaviour
    {
        CharacterVitals vitals;

        void Awake() => vitals = GetComponent<CharacterVitals>();
        void OnEnable() => vitals.Died += OnDied;
        void OnDisable() => vitals.Died -= OnDied;

        void OnDied()
        {
            // 같은 오브젝트의 플레이어 입력을 모두 끈다. 나(이 스크립트)는 남겨 둔다.
            foreach (var b in GetComponents<MonoBehaviour>())
                if (b != this && b.GetType().Namespace == typeof(PlayerDeathHandler).Namespace) b.enabled = false;
        }
    }
}
