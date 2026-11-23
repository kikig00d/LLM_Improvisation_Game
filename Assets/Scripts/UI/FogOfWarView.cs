using System.Collections.Generic;
using Improv.Characters.Sight;
using UnityEngine;

namespace Improv.UI
{
    /// <summary>
    /// 플레이어 눈에 보이는 것만 화면에 보여 준다 (R-010~R-013).
    /// - 시야 밖은 어둡게 덮는다. 산장 구조는 어두워도 알아볼 수 있게 반투명이다 (R-010).
    /// - 캐릭터와 상자(SightTarget)는 보일 때만 나타나고, 안 보이면 서서히 사라진다 (R-011, R-012).
    /// 판정은 전부 플레이어의 Vision이 하고, 여기는 그 결과를 그리기만 한다 (P-2).
    /// </summary>
    public class FogOfWarView : MonoBehaviour
    {
        // =====================================================================
        // #1. 설정값
        // =====================================================================

        [SerializeField] Vision viewer;                         // 플레이어의 눈
        [SerializeField] Color fogColor = new(0f, 0f, 0f, 0.55f);
        [SerializeField] int rayCount = 180;                    // 안개 경계를 그릴 광선 수. 많을수록 매끄럽다
        [SerializeField] float fadeSpeed = 6f;                  // 1초에 투명도가 바뀌는 양. 6이면 약 0.17초에 사라진다
        [SerializeField] int sortingOrder = 100;                // 다른 그림보다 위에 그린다
        [SerializeField] Material fogMaterial;                  // 비우면 URP 2D 기본 Unlit 셰이더로 만든다

        const float FarDistance = 60f;   // 안개를 화면 밖까지 넉넉히 덮을 거리

        // =====================================================================
        // #2. 상태
        // =====================================================================

        Mesh mesh;
        readonly List<float> angles = new();
        readonly List<Vector3> vertices = new();
        readonly List<Color> colors = new();
        readonly List<int> triangles = new();

        // 대상마다 지금 보이는 정도(0~1)와, 각 그림의 원래 투명도. 원래 색(NPC 색조 등)은 건드리지 않는다.
        readonly Dictionary<SightTarget, float> shown = new();
        readonly Dictionary<SpriteRenderer, float> baseAlpha = new();

        // =====================================================================
        // #3. 유니티 생명주기
        // =====================================================================

        void Awake()
        {
            if (viewer == null)
            {
                Debug.LogError("[FogOfWarView] viewer(플레이어의 Vision)가 비어 있습니다.", this);
                enabled = false;
                return;
            }

            // 안개 그림을 그릴 자식 오브젝트를 코드로 만든다. 월드 좌표를 그대로 쓰도록 원점에 둔다.
            var go = new GameObject("FogMesh");
            go.transform.SetParent(transform, false);
            go.transform.position = Vector3.zero;
            mesh = new Mesh { name = "FogOfWar" };
            mesh.MarkDynamic();   // 매 프레임 바꾸는 메시라고 엔진에 알려 준다
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = go.AddComponent<MeshRenderer>();
            if (fogMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shader == null) Debug.LogError("[FogOfWarView] 안개 셰이더를 찾지 못했습니다. fogMaterial을 지정하세요.", this);
                else fogMaterial = new Material(shader);
            }
            meshRenderer.sharedMaterial = fogMaterial;
            meshRenderer.sortingOrder = sortingOrder;
        }

        void LateUpdate()
        {
            UpdateTargets();
            BuildFogMesh();
        }

        // =====================================================================
        // #4. 캐릭터와 상자 숨기기 (R-011, R-012)
        // =====================================================================

        void UpdateTargets()
        {
            foreach (var target in SightTarget.All)
            {
                if (target.transform.IsChildOf(viewer.transform)) continue;   // 내 몸은 늘 보인다

                bool visible = viewer.CanSee(target);
                shown.TryGetValue(target, out float current);
                if (!shown.ContainsKey(target)) current = visible ? 1f : 0f;   // 처음 볼 때는 깜빡이지 않게 바로
                current = Mathf.MoveTowards(current, visible ? 1f : 0f, fadeSpeed * Time.deltaTime);
                shown[target] = current;

                foreach (var r in target.Renderers)
                {
                    if (r == null) continue;
                    if (!baseAlpha.TryGetValue(r, out float a)) { a = r.color.a; baseAlpha[r] = a; }
                    Color c = r.color;
                    c.a = a * current;
                    r.color = c;
                }
            }
        }

        /// <summary>테스트용: 이 대상이 지금 화면에 얼마나 보이나 (0~1).</summary>
        public float ShownAmount(SightTarget target) => shown.TryGetValue(target, out float v) ? v : 0f;

        // =====================================================================
        // #5. 시야 밖 어둡게 덮기 (R-010: 구조는 반투명하게 비친다)
        // =====================================================================

        void BuildFogMesh()
        {
            Vector2 eye = viewer.EyePosition;
            float gaze = viewer.GazeAngle;
            float halfFov = viewer.FieldOfView * 0.5f;

            // 광선 각도: 고르게 rayCount개 + 시야각 양 끝 바로 안팎. 끝에 광선을 더 둬야 부채꼴 경계가 또렷하다.
            angles.Clear();
            for (int i = 0; i < rayCount; i++) angles.Add(i * 360f / rayCount);
            foreach (float edge in new[] { gaze - halfFov, gaze + halfFov })
            {
                angles.Add(Mathf.Repeat(edge - 0.05f, 360f));
                angles.Add(Mathf.Repeat(edge + 0.05f, 360f));
            }
            angles.Sort();

            // 광선마다 "여기까지 보인다"는 안쪽 점과, 화면 밖 먼 점을 하나씩 만든다.
            vertices.Clear(); colors.Clear(); triangles.Clear();
            foreach (float angle in angles)
            {
                Vector2 dir = new(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                bool inFov = Mathf.Abs(Mathf.DeltaAngle(angle, gaze)) <= halfFov;
                float reach = viewer.VisibleDistance(dir, inFov ? viewer.ViewDistance : viewer.BackRadius);
                vertices.Add(eye + dir * reach);
                vertices.Add(eye + dir * FarDistance);
                colors.Add(fogColor);
                colors.Add(fogColor);
            }

            // 이웃한 두 광선 사이의 "보이는 곳 바깥" 사다리꼴을 삼각형 두 개로 채운다.
            int n = angles.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int inI = i * 2, outI = i * 2 + 1, inJ = j * 2, outJ = j * 2 + 1;
                triangles.Add(inI); triangles.Add(outJ); triangles.Add(outI);
                triangles.Add(inI); triangles.Add(inJ); triangles.Add(outJ);
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }
    }
}
