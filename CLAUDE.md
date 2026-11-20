# 즉흥극 (Improv)

겨울 산장에 갇힌 캐릭터들 사이에서 LLM NPC와 제한된 언어로 대화하는 2D 사이드뷰 클로즈드 서클 게임.

## 기획서

- 원본은 [Docs/GDD.md](Docs/GDD.md) 하나뿐이다. 바탕화면 `방학LLM` 폴더의 기획서는 2026-11-20에 복사한 옛 사본이므로 고치지 않는다.
- 기능을 만들기 전에 해당 마일스톤(11장)과 관련 규칙(R-, E-, O- ID)을 읽는다.
- `[확정]`은 그대로, `[가정]`은 일단 그 값으로 구현하고, `[미정]`은 구현 전에 사용자에게 묻는다. 임의로 정하지 않는다.
- 마일스톤을 끝내면 11장의 상태를 갱신하고 13장 변경 이력에 한 줄 남긴다.

## 설계 원칙 (기획서 P-1~P-4)

- P-1. 시야와 청각은 플레이어와 NPC가 같은 인지 기능을 쓴다.
- P-2. 판정은 월드 데이터로 하고, 말풍선 같은 UI는 그 결과를 표시만 한다.
- P-3. LLM은 인지 필터를 통과한 정보만 받는다.
- P-4. LLM은 드물게, 고수준 판단에만 부르고 평소 행동은 코드로 처리한다.

## 코드 구조와 규칙

- `Assets/Scripts/World`: 방, 계단 같은 월드 데이터. `Assets/Scripts/Characters`: 플레이어와 NPC가 함께 쓰는 몸과 상태. `Assets/Scripts/Player`: 플레이어 전용 입력.
- 캐릭터의 몸(`CharacterMotor`)과 조종자(`PlayerInputDriver`, 나중에 NPC AI)를 분리한다. NPC는 같은 몸을 `SetMoveInput()`으로 움직인다.
- 이동은 "층 + x좌표" 모델이다. 걸을 때 y를 잠그고, 계단은 Kinematic으로 경로를 따라간다. 캐릭터 기준점(pivot)은 발이다.
- `MonoBehaviour`는 파일 하나에 하나, 파일 이름은 클래스 이름과 같게.
- 튜닝 값은 `[SerializeField]`로 Inspector에 노출하고, 필수 참조는 `Awake`에서 검사해 `Debug.LogError(..., this)`로 알린다.
- 입력은 프로젝트 전역 액션(`InputSystem.actions`, `Assets/InputSystem_Actions.inputactions`)을 쓴다. 옛 `Input.GetKey`는 쓰지 않는다.
- UI에 `OnGUI`를 쓰지 않는다.

## 주석 스타일

사용자는 코드를 한 줄씩 읽으며 자기 주석을 덧붙여 공부한다. 그에 맞춰:

- 긴 파일은 `// =====` 사이에 `// #1. 설정값` 같은 번호 구역으로 나눈다.
- 한국어 주석으로 무엇보다 **왜**를 적고, 기획서 ID를 함께 적는다.
- 사용자는 C 계열에 익숙하다. 새 C#·유니티 개념은 C에 빗대어 짧게 설명한다.

## 작업 요령

- 유니티 에디터는 Unity CLI(`unity`)와 `com.unity.pipeline` 패키지로 조종한다. 씬과 에셋은 YAML을 직접 고치지 말고, 열린 에디터에서 C#으로 바꾼다.
- 이 PC의 Windows PowerShell 5.1은 외부 프로그램 인자 속 큰따옴표를 망가뜨린다. C# 코드는 `unity command script eval --code` 대신 파일로 써서 `--file <경로>`로 넘긴다.
- 게임 화면 캡처: `unity command capture game_view --source camera --output-path <경로>`.
- 검증은 Play 모드에서 완료 기준대로 확인한다. 코드로 입력을 흉내 낸 테스트와 실제 키보드 조작 확인은 구분해서 보고한다.
- Unity 6000.3.14f1, URP 2D. 클라우드 연결은 꺼져 있다.
