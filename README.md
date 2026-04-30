# UE-Chess

로컬 2인용 3D 체스 게임. Unreal Engine 5.7 C++로 제작되었으며, WPF 컨트롤 패널과 Ollama LLM을 연동한 AI 대전 기능을 포함합니다.

![Gameplay](/Assets/gameplay.gif)

---

## 요구 사항

| 항목 | 버전 |
|------|------|
| Unreal Engine | 5.7.4 |
| Visual Studio | 2022 이상 (C++ 게임 개발 워크로드) |
| .NET SDK | 8.0 (WPF 컨트롤 패널) |
| Ollama | 최신 버전 ([ollama.com](https://ollama.com)) |
| 권장 LLM 모델 | `gemma4:latest` (또는 호환 모델) |

> AI 대전 기능 없이 일반 대전만 사용할 경우 Ollama는 필요하지 않습니다.

---

## 프로젝트 구조

```
UE-Chess/
├── Source/Chess3Dv3/          # UE5 C++ 소스
│   ├── Public/                # 헤더 파일
│   └── Private/               # 구현 파일
├── Content/                   # UE5 에셋 (Blueprint, Mesh, Material)
├── ChessControlPanel/         # WPF 컨트롤 패널 (.NET 8)
│   ├── Prompts/               # AI 레벨별 프롬프트 파일
│   ├── LlmClient.cs           # Ollama HTTP 클라이언트
│   ├── MoveGenerationService.cs  # 수 선택 로직 (레벨별 전략)
│   ├── AiLevel.cs             # AI 레벨 정의 및 파라미터 프로필
│   ├── ChessDtos.cs           # TCP 통신 DTO
│   └── FenAnalyzer.cs         # FEN 파싱 유틸리티
└── Chess3Dv3.uproject
```

---

## 빌드 및 실행

### UE5 프로젝트

1. `Chess3Dv3.uproject` 우클릭 → **Generate Visual Studio project files**
2. Visual Studio에서 `Chess3Dv3Editor Development Win64` 빌드
3. UE5 에디터에서 Play

### WPF 컨트롤 패널

```bash
cd ChessControlPanel
dotnet run
```

또는 Visual Studio / Rider에서 `ChessControlPanel.csproj` 열어서 실행.

---

## 게임플레이

- 마우스 클릭으로 말을 선택하면 이동 가능한 칸이 하이라이트됩니다.
- 하이라이트된 칸을 클릭하면 이동합니다.
- 두 플레이어가 번갈아 수를 둡니다.
- 어느 한쪽 킹이 포획되면 게임이 종료됩니다.

### 미구현 규칙

- 캐슬링 (Castling)
- 앙파상 (En passant)
- 폰 프로모션 (Promotion)
- 체크메이트 강제 (체크 상황에서 방어 강제 없음)

---

## AI 대전 시스템

WPF 컨트롤 패널이 UE5와 TCP로 통신하며 AI 수 결정을 담당합니다. Ollama를 통해 로컬 LLM을 호출합니다.

### 통신 구조

```
[UE5 게임]  ←──TCP 7777──→  [WPF 컨트롤 패널]  ──HTTP──→  [Ollama LLM]
```

- **UE5 → WPF**: 현재 FEN, 합법 수 목록, 수 기록을 JSON으로 전송
- **WPF → LLM**: 레벨별 프롬프트 + 국면 정보로 최선의 수 요청
- **LLM → WPF**: UCI 형식의 수 + 한국어 코멘트 반환
- **WPF → UE5**: 선택된 수를 TCP로 전송

### AI 레벨

| 레벨 | 명칭 | ELO 목표 | Temperature | 사고 깊이 | 특징 |
|------|------|----------|-------------|-----------|------|
| 1 | 초보 | 800 ~ 1200 | 0.7 ~ 0.8 | 1 ~ 2수 | 단순 캡처 선호, 실수 허용 |
| 2 | 중급 | 1400 ~ 1800 | 0.3 ~ 0.5 | 2 ~ 3수 | 전술 패턴 인식, 블런더 최소화 |
| 3 | 고급 | 2000+ | 0.1 ~ 0.3 | 3 ~ 5수 | 깊은 계산, 전략적 판단 |

- 게임 국면(오프닝/미들게임/엔드게임)에 따라 Temperature가 동적으로 조정됩니다.
- 각 레벨마다 독립적인 시스템 프롬프트와 수 생성 템플릿을 사용합니다.
- LLM 호출 3회 실패 시 합법 수 중 랜덤으로 폴백합니다.

### 대전 모드

| 모드 | 설명 |
|------|------|
| VS AI (백 플레이) | 플레이어가 백, AI가 흑으로 대전 |
| VS AI (흑 플레이) | 플레이어가 흑, AI가 백으로 대전 |
| AI VS AI Auto Play | 백 AI와 흑 AI가 자동으로 대전. 게임 종료 후 자동 재시작. 백/흑 AI 레벨 독립 설정 가능 |

### Ollama 설정

기본값: `http://172.20.64.76:11434`, 모델 `gemma4:latest`

변경하려면 `ChessControlPanel/LlmClient.cs`의 생성자 기본값을 수정하세요.

```csharp
public LlmClient(
    string ollamaHost = "http://localhost:11434",
    string model      = "gemma4:latest", ...)
```

---

## WPF 컨트롤 패널 기능

- **UE5 연결 상태** 및 **Ollama 상태** 실시간 표시
- **Restart**: 현재 게임 초기화
- **VS AI 대전 시작**: 레벨 선택 후 백/흑 선택
- **AI VS AI Auto Play**: 백·흑 레벨 독립 선택, 자동 연속 대전, 중지 시 현재 게임 자연 종료
- **마지막 AI 수 디버그 패널**: 수, 응답 레벨, 응답 시간(ms), 폴백 여부, AI 코멘트 표시

---

## 아키텍처 메모

- **좌표계**: UE 보드 x=0(흑 백랭크=rank8) ~ x=7(백 백랭크=rank1), y=0(a파일) ~ y=7(h파일)
- **UCI 변환**: `file = 'a' + y`, `rank = 8 - x`
- **King/Queen 위치**: 이 프로젝트에서 King은 y=3(d파일), Queen은 y=4(e파일) — 표준 체스와 반대
- **게임 종료 감지**: 합법 수 없음 또는 킹 포획 시 `game_over` 메시지 전송
