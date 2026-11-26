# 즉흥극 (Improv)

겨울 산장에 갇힌 캐릭터들 사이에서 LLM NPC와 제한된 언어로 대화하는 2D 사이드뷰 클로즈드 서클 게임.

## 기획서와 진행 현황

- 지금 어디까지 왔고 다음에 무엇을 할지는 [Docs/PROGRESS.md](Docs/PROGRESS.md)를 먼저 읽는다. 마일스톤을 끝내거나 중요한 결정을 하면 갱신한다.
- 기획서 원본은 [Docs/GDD.md](Docs/GDD.md) 하나뿐이다. 바탕화면 `방학LLM` 폴더의 기획서는 2026-11-20에 복사한 옛 사본이므로 고치지 않는다.
- 기능을 만들기 전에 해당 마일스톤(11장)과 관련 규칙(R-, E-, O- ID)을 읽는다.
- `[확정]`은 그대로, `[가정]`은 일단 그 값으로 구현하고, `[미정]`은 구현 전에 사용자에게 묻는다. 임의로 정하지 않는다.
- 마일스톤을 끝내면 11장의 상태를 갱신하고 13장 변경 이력에 한 줄 남긴다.

## 설계 원칙 (기획서 P-1~P-4)

- P-1. 시야와 청각은 플레이어와 NPC가 같은 인지 기능을 쓴다.
- P-2. 판정은 월드 데이터로 하고, 말풍선 같은 UI는 그 결과를 표시만 한다.
- P-3. LLM은 인지 필터를 통과한 정보만 받는다.
- P-4. LLM은 드물게, 고수준 판단에만 부르고 평소 행동은 코드로 처리한다.
- NPC가 "반응"으로 보이는 행동(돌아보기, 대답할지 말지 등)은 코드로 강제하지 않는다. 엘더스크롤식 자동 반응 대신 LLM이 고르게 하고, 이미 하는 LLM 호출의 JSON 답에 선택지를 얹는다 (R-016b). 코드는 몸의 능력(`FaceTowards` 등)만 제공한다.

## 코드 구조와 규칙

- `Assets/Scripts/World`: 방, 계단 같은 월드 데이터. `Assets/Scripts/Characters`: 플레이어와 NPC가 함께 쓰는 몸과 상태(`Speech`: 입 `Speaker`, 귀 `Hearing` / `Sight`: 눈 `Vision`, 보일 수 있는 것 `SightTarget`). `Assets/Scripts/Player`: 플레이어 전용 입력. `Assets/Scripts/Npc`: NPC 두뇌와 캐릭터 시트. `Assets/Scripts/Llm`: LLM 연결. `Assets/Scripts/UI`: 대화창, 말풍선.
- 말은 대상을 고르지 않는다. `Speaker.Say()` → `SpeechSystem`이 청각 범위 안의 `Hearing`에만 전달한다. 플레이어 대화 기록과 NPC가 LLM에 넘기는 내용은 모두 귀를 통과한 말뿐이다 (P-1, P-3).
- 보는 것도 같다. `Vision.CanSee()`가 유일한 판정이고, 화면 안개(`FogOfWarView`), 클릭 대상, NPC가 LLM에 넘기는 "보이는 사람"이 모두 이 판정을 쓴다. 층 바닥처럼 충돌체 없이 시야만 막는 것은 `SightBlocker`, 몸은 막지만 눈은 안 막는 것은 `SeeThrough`.
- 사람은 `CharacterIdentity.Id`(바뀌지 않는 번호)로 구분한다. 이름은 바뀔 수 있는 꼬리표라(개명, 동명이인) 코드에서 이름을 키로 쓰지 않는다. 단, NPC가 기억하는 것은 들은 이름뿐이다(P-3). 플레이어 단어 사전의 이름은 지금 씬의 이름으로 게임 중 채운다.
- 시간은 `GameClock` 하나가 정하고 속도를 `Time.timeScale`로 건다. 그래서 `Time.time`/`Time.deltaTime`이 곧 게임 시간이다(멈추면 0). 실제 시간이 필요한 곳(깨우기 입력 지연 등)만 `Time.unscaledTime`을 쓰고, 실제 시간으로 오는 LLM 답은 `GameClock.IsPaused` 동안 들고 있다가 쓴다.
- 개인실과 스태프 룸은 떨어진 공간(`Room.detached`)이다(GDD R-030). 월드 오른쪽 먼 곳에 실제로 있고 `World/RoomPortal` 짝 문으로만 드나든다. 귀·눈·NPC 길 찾기는 공간이 다르면 그 문을 거친다(`RoomPortal.SpaceOf`, `NextOnWay`). 새 떨어진 방은 서로, 그리고 산장과 화면 하나 넘게 떨어뜨려 둔다.
- 방(`Room`)은 문·계단·천장 같은 경계로 닫힌 공간의 영역이다(GDD R-024, 지금은 손으로 그린 사각형). NPC가 방으로 가면 그 영역에 들어서는 것이 도착이다. NPC 행동은 LLM이 고르고 `NpcActions`(무엇을, 본 것 기억)와 `NpcNavigator`(층 + x 길 찾기, 문 열기)가 같은 몸으로 해낸다.
- NPC 시트는 판이 시작할 때 `CastGenerator`가 만든다(목록 `Assets/Data/Generation/CharacterTables.asset`, GDD R-026). 겹치는 과거의 진실은 생성기가 들고 있고, NPC의 LLM에는 자기 판본만 넘긴다. 시험할 때는 `usePreset`(기본 켬)으로 미리 써 둔 판(`Assets/Data/Characters/TestCast/TestCastPreset.asset`)을 로딩 없이 쓰고, 생성을 보려면 끈다. 손으로 쓴 옛 시트를 쓰려면 CastGenerator를 끈다.
- 관계도는 `Characters/RelationBook`(GDD R-027): 호감 = 묵은 감정 + 까닭 붙은 수정치, 애정 = 드러난 면모, 자기 자신은 "이제까지의 나". 값은 LLM이 JSON `relation`으로 고르고 코드는 기록·흐려짐·상쇄만 한다.
- 플레이어 캐릭터 메이킹(GDD R-029): `CastGenerator.playerMaking`이 켜져 있으면 판 시작 때 `UI/CharacterMakingWindow`가 뜨고, 다 쓴 답안지(`PersonaDraft`)가 NPC와 같은 `CastMember`가 되어 `Player/PlayerPersona`에 들어간 뒤 생성이 시작된다. 보기와 문장 틀은 `Assets/Data/Generation/MakingTemplates.asset`. 메이킹을 건너뛰고 시험하려면 playerMaking을 끈다.
- 능력치와 기능(GDD R-031)은 `Characters/CharacterAbilities`(모든 캐릭터에 붙음, 규칙은 `Assets/Data/Abilities/AbilityRules.asset`). 바탕값은 판 시작 때 생성기·메이킹이 넣고, 부상·질병은 까닭 붙은 수정치(`AddModifier`/`RemoveModifiers`)로 얹는다. 효과는 읽는 쪽(`CharacterVitals`, `Vision`)이 `Factor`로 정하고, 주사위로 성공·실패를 정하지 않는다. 새 캐릭터에도 붙이고 rules를 연결한다.
- 관계도 창은 `UI/RelationChartWindow`(R, GDD R-028): 플레이어의 짐작(`Player/PlayerRelationNotes`, 느낌 표식 + 메모)과 개발자 보기(NPC의 진짜 마음). 창이 열린 동안 끌 플레이어 조작은 `Open()`의 목록에 있다. 새 플레이어 입력 스크립트를 만들면 그 목록에도 넣는다.
- UI 위의 클릭이 월드로 넘어가지 않게 막을 요소에는 USS 클래스 `ui-solid`를 붙인다 (`UiPointer`).
- UI는 UI Toolkit(UXML/USS, `Assets/UI`)으로 만들고 글꼴은 Noto Sans KR을 쓴다. 튜닝 데이터(캐릭터 시트, LLM 설정)는 ScriptableObject 에셋(`Assets/Data`)에 둔다.
- 캐릭터의 몸(`CharacterMotor`)과 조종자(`PlayerInputDriver`, 나중에 NPC AI)를 분리한다. NPC는 같은 몸을 `SetMoveInput()`으로 움직인다.
- 이동은 "층 + x좌표" 모델이다. 걸을 때 y를 잠그고, 계단은 Kinematic으로 경로를 따라간다. 캐릭터 기준점(pivot)은 발이다.
- `MonoBehaviour`는 파일 하나에 하나, 파일 이름은 클래스 이름과 같게.
- 튜닝 값은 `[SerializeField]`로 Inspector에 노출하고, 필수 참조는 `Awake`에서 검사해 `Debug.LogError(..., this)`로 알린다.
- 입력은 프로젝트 전역 액션(`InputSystem.actions`, `Assets/InputSystem_Actions.inputactions`)을 쓴다. 옛 `Input.GetKey`는 쓰지 않는다.
- UI에 `OnGUI`를 쓰지 않는다.
- 상호작용 대상은 `Interactable`을 상속한다 (`Door`, `Carryable`). 캐릭터의 손은 `CharacterInteractor`이고, 결과는 `InteractionResult`로 이유까지 돌려준다.
- 운반 가능 오브젝트(`Carryable`)는 상자뿐 아니라 시체 등도 포함한다. 계단은 운반한 채로 쓸 수 있다.
- 행동마다 부작용 없는 미리보기(`PreviewInteract`, `PreviewDrop`, `Interactable.CanInteract`)를 두고, 실제 행동은 같은 판정을 거친다. 커서·강조 표시(`PlayerHoverFeedback`, `HoverHighlight`)는 미리보기만 보여준다.
- 새 상호작용 오브젝트에는 `HoverHighlight`를 붙이고 `Assets/Art/Materials/HoverSilhouette.mat`을 연결한다.
- Asset Store 무료 에셋은 Package Manager 내부 API(`script eval --internals true`, `IAssetStoreDownloadManager.Download(productId)`)로 받고 `AssetDatabase.ImportPackage(path, false)`로 가져올 수 있다. 계정에 먼저 추가돼 있어야 한다.

## 에셋

- Easy Chara(플레이어 외형), Pixel Adventure 1(상자 그림), DOTween(연출), Basic RPG Cursors(커서, `Assets/Cursors`)는 모두 **임시**다. 게임에 맞거나 대안이 없을 때만 쓴다.
- 로직은 그림에 의존하지 않는다. 외형은 캐릭터의 `Visual` 자식, 오브젝트의 `Visual` 자식으로만 두고 갈아 끼울 수 있게 한다.
- 무료 에셋을 새로 받는 것은 사용자가 허락했다 (유니티 에셋 스토어, 무료, 별점 4 이상, 적절할 때만).
- DOTween: 새 트윈을 만들기 **전에** `DOKill()`을 부른다. 만든 뒤에 부르면 방금 만든 트윈까지 죽는다.

## 주석 스타일

사용자는 코드를 한 줄씩 읽으며 자기 주석을 덧붙여 공부한다. 그에 맞춰:

- 긴 파일은 `// =====` 사이에 `// #1. 설정값` 같은 번호 구역으로 나눈다.
- 한국어 주석으로 무엇보다 **왜**를 적고, 기획서 ID를 함께 적는다.
- 사용자는 C 계열에 익숙하다. 새 C#·유니티 개념은 C에 빗대어 짧게 설명한다.

## 작업 요령

- 유니티 에디터는 Unity CLI(`unity`)와 `com.unity.pipeline` 패키지로 조종한다. 씬과 에셋은 YAML을 직접 고치지 말고, 열린 에디터에서 C#으로 바꾼다.
- 이 PC의 Windows PowerShell 5.1은 외부 프로그램 인자 속 큰따옴표를 망가뜨린다. C# 코드는 `unity command script eval --code` 대신 파일로 써서 `--file <경로>`로 넘긴다.
- `script eval` 코드에는 using을 쓸 수 없어서 확장 메서드는 정적 클래스 이름으로 부른다 (예: `UQueryExtensions.Q(root, "이름")`, `InputActionSetupExtensions.AddAction(map, ...)`).
- 새 스크립트를 썼는데 컴파일된 타입에 안 보이면(eval에서 CS0234), 유니티가 저장 도중의 빈 파일을 읽은 것일 수 있다. 재가져오기로 안 되면 `unity close .` 후 `unity open .`.
- 게임 화면 캡처: `unity command capture game_view --source camera --output-path <경로>`.
- 검증은 Play 모드에서 완료 기준대로 확인한다. 코드로 입력을 흉내 낸 테스트와 실제 키보드 조작 확인은 구분해서 보고한다.
- Unity 6000.3.14f1, URP 2D. 클라우드 연결은 꺼져 있다.
