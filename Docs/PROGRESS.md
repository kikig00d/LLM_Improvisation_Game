# 진행 현황

> 새 세션이나 대화 압축(/compact) 뒤에 이어서 작업하기 위한 요약. 마지막 갱신: 2026-11-20
> 규칙과 수치의 원본은 [GDD.md](GDD.md), 작업 규칙은 [../CLAUDE.md](../CLAUDE.md)다. 여기는 "지금 어디까지 왔나"만 적는다.

## 현재 상태

- 브랜치: `main`. M1, M2와 마우스 피드백까지 합쳐졌다. 작업할 때는 마일스톤마다 `feature/m<번호>-<이름>` 브랜치를 만들고, 사용자가 직접 확인한 뒤 main에 fast-forward로 합친다.
- 씬: `Assets/Scenes/Lodge.unity` 하나. 1층 거실, 2층 개인실, 계단, 계단참과 개인실 사이의 문, 상자 3개(Box_A·B는 거실, Box_C는 개인실), 플레이어.
- M3까지 main에 합쳐졌다 (2026-11-20 사용자 확인).
- 다음 할 일: **Q2 모델 선택**(사용자 결정 대기) 후 **M4 (NPC 1명과 대화)**. M4 전에 사용자에게 NPC 인지 노트를 다시 받고, 비밀을 쉽게 털어놓는 문제를 프롬프트로 다룬다.

## M3 진행 기록 (2026-11-20)

- Ollama 0.40.16 설치(winget, 사용자 계정 설치라 관리자 권한 불필요). 실행 파일 `%LOCALAPPDATA%\Programs\Ollama\ollama.exe`. 서버는 자동 실행되어 `http://localhost:11434`.
- 받은 모델: `llama3.1:8b`(4.9GB), `exaone3.5:7.8b`(4.8GB), `qwen3:8b`(5.2GB).
- 코드: `Assets/Scripts/Llm/` — `ILlmClient`(갈아 끼우는 약속), `OpenAiCompatibleClient`(/v1/chat/completions), `LlmDebugLog`(요청·응답·시간을 Console과 `Logs/llm_debug.log`에), `LlmConsoleTester`(M3 확인·모델 비교용, M4에서 지움). 씬의 `LlmTester` 오브젝트에 붙어 있고 Play 시작 때 자동으로 보낸다.
- qwen3는 생각하는 모델이라 `reasoning_effort: "none"`으로 생각을 끈다. 다른 모델은 이 값을 무시한다 (확인함).
- 측정: 세 모델 모두 짧은 대사 0.2~0.3초, 첫 호출(모델 적재) 4~5초. VRAM 12GB에는 한 번에 한 모델만 올라가므로 모델을 바꾸면 다시 4~5초.
- E-004: 1.5초가 넘으면 `(생각 중...)` 로그. E-005: 없는 모델(HTTP 404), 꺼진 서버(연결 실패) 모두 빨간 오류 + NPC "..." 확인.
- 1차 품질 관찰(온도 0.7, 각 1회라 표본이 작음): 세 모델 모두 비밀(열쇠 분실)을 쉽게 털어놓는다. llama는 존댓말이 섞이고 띄어쓰기가 틀린다. exaone은 말투가 가장 자연스럽지만 괄호 규칙을 어겼다. qwen3는 말투가 투박하고 둘러대기도 했다.

## 완료한 마일스톤

| ID | 내용 | 핵심 파일 |
|---|---|---|
| M1 | "층 + x좌표" 이동, 계단 오르내리기, 방 추적 | `CharacterMotor`, `PlayerInputDriver`, `Stairs`, `Room`, `RoomPresence` |
| M2 | 문 여닫기, 운반(계단 포함), 손이 닿는 거리·가로막힘·겹침 판정 | `Interactable`, `Door`, `Carryable`, `CharacterInteractor`, `PlayerClickInteraction`, `CharacterAnimator` |
| M2+ | 하얀 빛 강조, 커서 아이콘, 놓일 자리 미리보기, 겹치면 빈자리로 밀어 놓기(R-007b) | `HoverHighlight`, `PlayerHoverFeedback`, `InteractableLookup`, `SpriteSilhouette.shader` |
| M3 | 로컬 LLM 연결(Ollama), 화이트박스 디버그 로그, E-004·E-005 | `ILlmClient`, `OpenAiCompatibleClient`, `LlmDebugLog`, `LlmConsoleTester` |

## M3 결정 사항 (2026-11-20, 사용자 승인)

- 실행 방식: **Ollama 서버** (OpenAI 호환 HTTP, 기본 주소 `http://localhost:11434/v1`). 유니티는 연결 부분만 갈아 끼우면 되게 짠다. 배포 방식은 나중에 다시 정한다.
- 모델: 라마 8B급 + 한국어에 강한 7~9B급 1~2개를 받아, 같은 왈도체 대화로 **품질과 응답 시간**을 비교한 뒤 사용자가 고른다.
- 설치: Ollama를 공식 배포처(winget 등)에서 설치하고 모델을 받는 것까지 허락받았다 (도구 약 1GB 안팎, 모델 개당 약 5GB). 관리자 권한 창은 사용자가 승인한다.
- 화이트박스: 사용자는 LLM이 무엇을 받고 무엇을 답했는지 직접 보고 싶어 한다. 유니티 안에 **요청, 응답, 소요 시간 디버그 로그**를 만든다. Ollama 자체의 채팅 창은 모델을 손으로 시험할 때 쓴다.
- M3 완료 기준(GDD 11장): 유니티에서 로컬 LLM에 문장을 보내고 받은 답이 Console에 찍힌다. 응답 시간도 재서 Q3(수면 가속과 LLM 처리량) 판단 자료로 남긴다.

## PC 환경

- Windows 11, i9-13900, RAM 32GB, **RTX 4070 (VRAM 12GB)**. 8B급 모델은 쾌적, 14B급까지 가능.
- Unity 6000.3.14f1 (URP 2D), Unity CLI 1.0.1, Git. 유니티 클라우드 연결은 꺼져 있다 (생성 때 키링 오류로 실패, 필요 없어서 보류).
- Ollama 0.40.16 설치됨 (위 M3 진행 기록 참고).

## 작업하며 얻은 교훈

- **검증은 중간 상태까지 찍는다.** 도착 위치만 본 테스트가 계단 순간이동 버그를 놓쳤고, 사용자가 직접 플레이로 잡았다.
- **실제 입력 흉내:** `InputSystem.QueueStateEvent`로 마우스 이벤트를 넣고 다음 프레임이 처리하게 둔다. 같은 호출 안에서 `InputSystem.Update()`를 부르면 `WasPressedThisFrame`이 놓친다.
- **실패 이유는 화면에 보여야 한다.** 내려놓기가 NoSpace로 계속 실패했지만 콘솔에만 찍혀서 사용자는 "안 된다"고만 느꼈다. 이후 미리보기와 커서로 해결했다.
- 사용자의 직접 플레이 기록은 에디터 로그(`%LOCALAPPDATA%\Unity\Editor\Editor.log`)에서 `[Player]` 줄로 찾을 수 있다.

## 열린 질문 (GDD 12장 요약)

- Q2 모델 선택 (M3에서 비교)
- Q3 수면 가속이 NPC 수 × LLM 응답 시간에 묶이는 문제 (M6 전에. NPC 인지 구상의 IDLE/ROUTINE 상태로 LLM 호출을 줄이는 방향)
- Q4 줌 방식, Q5 대화 대상 정하는 법, Q7 시야 값, Q8 상태바 값
- E-002의 예외(사다리 등) 목록

## 구상 노트 위치

- 사용자의 원래 구상은 바탕화면 `방학LLM` 폴더에 있다 (즉흥극_*.txt 등). 바탕화면의 기획서 사본은 옛 버전이니 고치지 않는다.
- NPC 인지 구상("멍 때리기의 미학": 인지 필터, IDLE~EMERGENCY 상태, 집중도 0~1에 따른 기억 해상도, 말풍선 겹침과 청각)은 대화에서 공유받았고, 초기 구조에 영향을 주는 부분만 설계 원칙 P-1~P-4로 반영했다. 나머지는 M4 이후 해당 단계에서 사용자에게 노트를 다시 받는다.
