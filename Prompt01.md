# UE5 ↔ WPF(C#) 체스 게임 제어 시스템 구축 프롬프트

> 목적: **WPF(C#) 컨트롤 패널**에서 버튼 조작만으로 **UE5 체스 게임(Chess3Dv3)**의 흐름을 제어한다.  
> 핵심 기능: **재시작**, **백(WHITE)으로 시작**, **흑(BLACK)으로 시작**, **AI 대전**.

---

## 프롬프트 목적

Unreal Engine 5.1 C++ 체스 프로젝트(로컬 핫시트 기반)에 **외부 WPF 앱을 통한 게임 제어 채널**을 추가한다.  
WPF 앱은 “게임 진행 UI”가 아니라 **원격 리모컨/운영 패널**이며, UE5는 실제 게임 상태(보드/턴/룰)를 소유한다.

---

## 전제(프로젝트 컨텍스트)

- UE 프로젝트: `Chess3Dv3` (UE 5.1, C++)
- 핵심 액터/책임(예시)
  - `ABoardActor`: 64칸 소유, 현재 턴 색(`m_ActivePlayerColor`), 선택 피스(`m_selectedPiece`), `EndTurn()` 등
  - `ACaseActor`: 클릭 이동 로직 트리거
  - `APieceActor` 및 하위 클래스: 이동 가능 칸 계산은 `GetAccessibleCases()`
- 네트워크 멀티플레이는 하지 않는다(로컬 1PC 기준).

---

## 팀 구성(에이전틱 워크플로우)

| 역할 | 담당 |
|------|------|
| **시스템 아키텍트** | IPC 방식 선택, 메시지/상태 설계 |
| **UE5 전문가** | UE 측 “명령 수신 → 게임 제어” 구현 |
| **WPF/C# 전문가** | WPF 컨트롤 패널 UI + 송신 클라이언트 |
| **QA/리더** | 통합 체크리스트, 예외/경계조건 검증 |

---

## 요구 기능(사용자 스토리)

- **재시작**: 어떤 상황에서든 현재 대국을 종료하고 초기 배치로 재시작한다.
- **백으로 시작**: 새 게임을 시작하며 “플레이어가 백(WHITE)”이 되도록 설정한다.
- **흑으로 시작**: 새 게임을 시작하며 “플레이어가 흑(BLACK)”이 되도록 설정한다.
- **AI 대전**: 핫시트 대신 AI와 대국한다.
  - 최소 요구: “플레이어 색(WHITE/BLACK)” 선택 가능
  - 선택 요구: 난이도(예: 1~3), AI 생각 시간(예: ms) 옵션

---

## 추가 요구(중요) — `chess-api` 기능은 전부 `ChessControlPanel`로 흡수

AI 대전에서 “AI의 수 결정(LLM Client)” 및 그에 필요한 프롬프트/요청·응답 포맷/검증 로직은 **별도 `chess-api` 프로세스로 남기지 않는다.**  
기존 `C:\Users\WN-ND000202\works\ai_exe_01\chess-llm-project\chess-api` 프로젝트가 제공하던 기능을 **WPF 앱 `ChessControlPanel` 내부 모듈로 전부 이관**한다.

### 최종 런타임 구성(필수)

- **UE5 Chess3Dv3**
- **ChessControlPanel (WPF, 단일 실행 파일)**
  - UE 제어(재시작/새 게임/모드 전환)
  - AI 수 생성(LLM 호출 + 프롬프트 적용 + 응답 파싱)
  - (권장) UE가 AI 수를 요청할 수 있는 **로컬 HTTP 서버** 제공

### 이관 범위(필수)

- `chess-api/prompts`의 프롬프트 텍스트(예: `chess_system.txt`, `move_generation.txt`)를 `ChessControlPanel` 프로젝트에 포함
  - 권장: `ChessControlPanel/Prompts/` 폴더로 복사 후 **Embedded Resource** 또는 배포 파일로 포함
- LLM 호출 로직(모델 선택/프롬프트 조합/온도/토큰/리트라이 정책 등)을 `ChessControlPanel` 내 `LlmClient`로 구현
- “FEN + legalMoves → move 선택”의 도메인 로직을 `ChessControlPanel` 내 `MoveGenerationService`로 구현

---

## 프롬프트(LLM) 요구사항 — `ChessControlPanel/Prompts` 기준

`ChessControlPanel`에 포함된 프롬프트는 “FEN만 보고 임의의 수를 생성”하는 형태가 아니라, **UE가 계산한 합법 수(legal moves) 목록 중에서 정확히 1개를 선택**하도록 강제한다.

- 시스템 프롬프트 요구:
  - **응답은 JSON only**
  - **선택한 수는 제공된 legal moves 리스트의 문자열과 완전히 동일(문자 단위 일치)**
- move 생성 프롬프트 요구:
  - 입력에 `{fen}`, `{move_history}`, `{legal_moves}`가 들어간다
  - 응답 JSON 포맷(고정):
    - `{"candidates":[...],"evaluation":"...","move":"EXACT_MOVE_FROM_LIST","comment":"한국어 ..."}`

따라서 UE는 “AI 수 요청”을 보낼 때 **FEN + 합법 수 리스트(표기 포함) + (선택) 착수 히스토리**를 반드시 제공해야 한다.

---

## STEP 1 — 시스템 아키텍트: IPC 방식 선정 및 전체 구조

### 권장 방식

- **동일 PC 전용/가장 단순**: **Named Pipe** (신뢰성 높고 방화벽 이슈 적음)
- **확장성/원격 가능**: **TCP (127.0.0.1:7777)** (향후 원격 제어 가능)

체스 제어는 60Hz 실시간 입력이 아니라 **저빈도 “명령(command)”** 위주이므로 UDP는 우선순위가 낮다.

### 아키텍처(텍스트 다이어그램)

```
[ChessControlPanel (WPF)]
  ├─ UI: Restart / Start as White / Start as Black / VS AI
  ├─ CommandClient → UE 제어 (NamedPipeClientStream or TcpClient)
  ├─ Prompts (chess_system.txt, move_generation.txt)
  ├─ LlmClient (LLM 호출)
  ├─ MoveGenerationService (FEN + legalMoves → move)
  └─ (권장) LocalHttpServer : UE의 AI 수 요청을 수신/응답
                 ↕
[UE5 Chess3Dv3]
  ├─ CommandServer : WPF에서 오는 제어 명령 수신
  ├─ GameFacade : 보드/턴/모드 전환
  └─ AiTurnDriver : AI 턴에 ChessControlPanel(LocalHttpServer)로 요청
```

---

## STEP 2 — 메시지/프로토콜 설계(JSON Lines 권장)

### 규칙

- 인코딩: UTF-8
- 프레이밍: **한 줄 = 한 메시지** (JSON + `\n`)
- 버전: `v` 필드로 호환성 관리
- 멱등성: 재시작/새 게임은 `requestId`로 중복 처리 가능(선택)

### 명령 메시지 스키마(예시)

```json
{"v":1,"type":"command","cmd":"restart","requestId":"a1"}
{"v":1,"type":"command","cmd":"new_game","options":{"playerColor":"WHITE","mode":"HOTSEAT"},"requestId":"a2"}
{"v":1,"type":"command","cmd":"new_game","options":{"playerColor":"BLACK","mode":"HOTSEAT"},"requestId":"a3"}
{"v":1,"type":"command","cmd":"new_game","options":{"playerColor":"WHITE","mode":"VS_AI","aiLevel":2},"requestId":"a4"}
```

### (선택) 상태 피드백 메시지

```json
{"v":1,"type":"status","state":"idle|running","activeTurn":"WHITE","mode":"HOTSEAT|VS_AI","playerColor":"WHITE","lastError":null}
```

---

## STEP 3 — WPF/C# 전문가: 컨트롤 패널 구현 요구사항

### UI 요구사항

- 버튼 4개
  - `Restart`
  - `Start as White`
  - `Start as Black`
  - `Play vs AI` (플레이어 색/난이도 선택 포함 가능)
- 연결 상태 표시(Connected/Disconnected)
- 실패 시 토스트/라벨로 오류 표시(예: “UE가 실행 중인지 확인”)

### C# 구현 가이드(방식별)

- Named Pipe 권장 클래스
  - `NamedPipeClientStream`, `StreamWriter`(AutoFlush), `StreamReader`
- TCP 대안
  - `TcpClient`, `NetworkStream`, `StreamWriter`
- 메시지 전송 규칙
  - 버튼 클릭 시 위 JSON Lines 한 줄 전송
  - UI 스레드 블로킹 금지(비동기 `async/await` 사용)

---

## STEP 4 — UE5 전문가: 명령 수신 및 게임 제어(Facade) 설계

### UE 측 구성

- `AChessCommandReceiver`(액터) 또는 `UGameInstanceSubsystem` 형태로 상시 수신 루프 운영
- 수신 스레드 → 게임 스레드 전환 필수(UE는 대부분 게임 오브젝트 접근이 게임 스레드에서 안전)
  - 예: `AsyncTask(ENamedThreads::GameThread, ...)`

### 커맨드 라우팅(필수)

- `restart`
  - 현재 게임 오브젝트 정리(피스 제거/보드 초기화/턴 초기화)
- `new_game`
  - 옵션: `mode = HOTSEAT | VS_AI`
  - 옵션: `playerColor = WHITE | BLACK`
  - 옵션: `aiLevel`(선택)

### AI 대전 구현 가이드(필수: `ChessControlPanel` 내 llm_client)

VS_AI 모드에서 “AI의 수 선택”은 UE 내부 랜덤 로직이 아니라, **ChessControlPanel 내부의 LLM Client가 담당**한다.

#### UE ↔ ChessControlPanel 통신 방식(권장: 로컬 HTTP)

- 기본: `http://127.0.0.1:18080`
- ChessControlPanel은 실행 시 로컬 HTTP 서버를 열고 “AI 수 생성” 엔드포인트를 제공한다.
- UE는 AI 턴이 되었을 때
  - 현재 보드 상태를 직렬화(권장: FEN)
  - ChessControlPanel(LocalHttpServer)에 “다음 수 요청”을 보낸다
  - 응답으로 받은 수를 파싱(권장: UCI)하여 **기존 이동 실행 경로**로 적용한다

#### 요청/응답 포맷(예시, ChessControlPanel에서 그대로 지원)

요청(JSON) — **FEN + legalMoves 필수**:

```json
{
  "v": 1,
  "gameId": "optional-uuid",
  "fen": "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
  "playerColor": "BLACK",
  "mode": "VS_AI",
  "aiLevel": 2,
  "timeLimitMs": 1500,
  "moveHistory": ["e2e4", "e7e5"],
  "legalMoves": ["g1f3", "f1c4", "d2d4"]
}
```

응답(JSON) — **JSON only + move는 legalMoves 중 1개를 문자열 그대로**:

```json
{
  "ok": true,
  "candidates": ["g1f3", "d2d4", "f1c4"],
  "evaluation": "one sentence comparing candidates",
  "moveUci": "g1f3",
  "commentKo": "한국어로 선택 이유 (1~2문장)",
  "error": null
}
```

#### 타임아웃/재시도/폴백 정책(필수)

- 타임아웃: 기본 1.5~3초(옵션), 초과 시 UE는 다음 중 하나를 수행
  - (권장) **AI 기권 처리** 또는 “AI 오류”를 표시하고 게임을 일시정지
  - (선택) 합법 수 중 랜덤 1수 폴백(디버그용, 출시 빌드에서는 비권장)
- 실패(HTTP 실패/파싱 실패/불법 수) 시
  - 해당 수는 적용하지 말고 오류 로그를 남긴다
  - 1회 재시도 후 동일하면 위 정책으로 처리한다

#### “합법 수” 검증(필수)

`chess-api`가 준 `moveUci`는 반드시 UE 쪽 룰/상태로 검증해야 한다.

- UCI 예: `e2e4`, 프로모션 `e7e8q`
- 검증 방법(요구사항 수준)
  - UE가 보유한 현재 보드에서 “해당 수가 가능한지” 확인
  - 가능하면 적용, 불가능하면 거부(오류 처리)

추가로, 프롬프트 요구에 맞추려면 **UE가 제공한 `legalMoves`와 응답 `moveUci`가 문자 단위로 동일**해야 한다(표기 일치). UE는 `legalMoves` 생성 시

- UCI 표기(`e2e4`, 프로모션 `e7e8q`)를 기본으로 하되,
- 체크/메이트/프로모션 표기(+/#/=Q 등)가 섞이는 구현이라면 그 표기 규칙을 **UE와 chess-api가 동일**하게 맞춘다.

---

## STEP 5 — QA/리더: 수용 기준(Acceptance Criteria)

- **재시작**
  - 어떤 턴/선택 상태에서도 `Restart` 한 번으로 초기 배치로 복구
  - 하이라이트/선택 상태 초기화
- **백/흑 시작**
  - `Start as White` 실행 시 플레이어 색이 WHITE로 설정되고 첫 턴이 정상
  - `Start as Black` 실행 시 플레이어 색이 BLACK로 설정되고(필요 시 AI/상대 턴부터) 진행이 정상
- **AI 대전**
  - `VS_AI` 모드에서 AI가 자신의 턴에 자동으로 수를 둔다
  - AI 수는 `chess-api` 호출 결과로 결정되며, UE는 적용 전 합법 수 검증을 수행한다
  - `chess-api`가 오류/타임아웃/불법 수를 반환해도 UE가 크래시하지 않는다
- **안정성**
  - UE가 꺼져 있을 때 WPF가 크래시하지 않고 재연결 가능
  - 잘못된 JSON/알 수 없는 cmd 수신 시 UE가 크래시하지 않고 무시/에러 로그 처리

---

## 빠른 시작(권장 개발 순서)

1) UE: “명령 수신(더미)”만 먼저 붙여서 `restart/new_game` 로그가 찍히게 한다.  
2) WPF: 버튼 → 메시지 송신까지 완성하고, UE 로그로 end-to-end 확인한다.  
3) UE: `restart/new_game`이 실제로 보드/피스를 초기화하도록 연결한다.  
4) ChessControlPanel에 `chess-api` 기능(프롬프트/LLM 호출/수 생성)을 이관하고, 로컬 HTTP 서버(18080)를 붙인다.  
5) UE: `VS_AI`에서 AI 턴에 ChessControlPanel로 요청 → 응답 수를 적용하는 end-to-end를 붙인다.  
6) 불법 수/타임아웃/LLM 오류 처리까지 검증한다.  
7) (선택) UE → WPF 상태 피드백(현재 턴/모드/AI 오류)을 붙여 UX 개선.