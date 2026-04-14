// Fill out your copyright notice in the Description page of Project Settings.

#include "BoardActor.h"
#include "CaseActor.h"
#include "PieceActor.h"
#include "PawnPieceActor.h"
#include "RookPieceActor.h"
#include "KnightPieceActor.h"
#include "BishopPieceActor.h"
#include "QueenPieceActor.h"
#include "KingPieceActor.h"
#include "ChessCommandServer.h"

#include "Json.h"
#include "Kismet/GameplayStatics.h"

ABoardActor::ABoardActor()
{
	SceneComponent = CreateDefaultSubobject<USceneComponent>(TEXT("SceneComponent"));
	SetRootComponent(SceneComponent);

	BpCase = ACaseActor::StaticClass();
}

void ABoardActor::BeginPlay()
{
	Super::BeginPlay();

	if (IsValid(ABoardActor::StaticClass()) && IsValid(BpCase))
	{
		UWorld* MyLevel = GetWorld();
		if (IsValid(MyLevel))
		{
			FVector SpawnLocation = GetActorLocation();
			FRotator SpawnRotation = GetActorRotation();
			FVector SpawnScale = GetActorScale3D();

			if (cases.Num() == 0)
			{
				for (int x = 0; x < 8; x++)
				{
					for (int y = 0; y < 8; y++)
					{
						FVector CaseLocation = SpawnLocation + FVector(x * 100.0, y * 100.0, 0.0);
						FTransform CaseTransform(SpawnRotation, CaseLocation, SpawnScale);
						ACaseActor* caseActor = (ACaseActor*)MyLevel->SpawnActor(BpCase, &CaseTransform);
						caseActor->Init(this, x, y);
						cases.Add(caseActor);
					}
				}
			}
		}
	}
}

ACaseActor* ABoardActor::GetCase(int x, int y)
{
	if (x < 0 || x > 7 || y < 0 || y > 7)
	{
		UE_LOG(LogTemp, Error, TEXT("GetCase: 범위 초과 (%d, %d)"), x, y);
		return nullptr;
	}
	return cases[x * 8 + y];
}

void ABoardActor::EndTurn()
{
	m_selectedPiece = nullptr;
	m_ActivePlayerColor = (m_ActivePlayerColor == WHITE) ? BLACK : WHITE;
	TriggerAiTurnIfNeeded();
}

// ── 게임 제어 ───────────────────────────────────────────────────

void ABoardActor::RestartGame()
{
	// 선택 상태 초기화
	m_selectedPiece = nullptr;
	m_bAiThinking = false;
	m_AiRetryCount = 0;
	GetWorldTimerManager().ClearTimer(m_AiRetryTimer);
	m_MoveHistory.Empty();

	// 모든 말 제거 + 케이스 하이라이트 초기화
	for (ACaseActor* cs : cases)
	{
		if (!IsValid(cs)) continue;
		cs->ResetHighlight();
		if (IsValid(cs->m_Piece))
		{
			cs->m_Piece->Destroy();
			cs->m_Piece = nullptr;
		}
	}

	// 턴 초기화
	m_ActivePlayerColor = WHITE;

	// 케이스 재초기화 (말 재배치)
	for (ACaseActor* cs : cases)
	{
		if (IsValid(cs))
			cs->Init(this, cs->m_X, cs->m_Y);
	}
}

void ABoardActor::NewGame(TEnumAsByte<PieceColor> playerColor,
                          TEnumAsByte<EChessGameMode> mode,
                          int aiLevel)
{
	m_PlayerColor = playerColor;
	m_GameMode    = mode;
	m_AiLevel     = aiLevel;

	RestartGame();

	// VS_AI이고 AI가 먼저 두는 경우 (플레이어가 BLACK → AI = WHITE → 먼저)
	TriggerAiTurnIfNeeded();
}

// ── AI 턴 트리거 ─────────────────────────────────────────────

void ABoardActor::TriggerAiTurnIfNeeded()
{
	if (m_bAiThinking) return;

	if (m_GameMode == VS_AI && m_ActivePlayerColor != m_PlayerColor)
	{
		RequestAiMove();
	}
	else if (m_GameMode == AI_VS_AI)
	{
		// AI_VS_AI: 양쪽 모두 AI가 둠
		RequestAiMove();
	}
}

// ── FEN 생성 ─────────────────────────────────────────────────
// 좌표계: x=0 → 흑 백랭크(rank 8), x=7 → 백 백랭크(rank 1)
//          y=0 → a파일,            y=7 → h파일

static TCHAR GetFenChar(APieceActor* Piece)
{
	TCHAR c = TEXT('?');
	if      (Cast<APawnPieceActor>  (Piece)) c = TEXT('p');
	else if (Cast<ARookPieceActor>  (Piece)) c = TEXT('r');
	else if (Cast<AKnightPieceActor>(Piece)) c = TEXT('n');
	else if (Cast<ABishopPieceActor>(Piece)) c = TEXT('b');
	else if (Cast<AQueenPieceActor> (Piece)) c = TEXT('q');
	else if (Cast<AKingPieceActor>  (Piece)) c = TEXT('k');

	if (Piece->m_Color == WHITE)
		c = FChar::ToUpper(c);
	return c;
}

FString ABoardActor::GenerateFEN() const
{
	FString Fen;

	// 1. 말 배치
	for (int x = 0; x < 8; x++)   // x=0 = rank 8 (흑 백랭크)
	{
		int Empty = 0;
		for (int y = 0; y < 8; y++)   // y=0 = a파일
		{
			ACaseActor* cs = cases[x * 8 + y];
			if (!IsValid(cs) || !IsValid(cs->m_Piece))
			{
				Empty++;
			}
			else
			{
				if (Empty > 0) { Fen += FString::FromInt(Empty); Empty = 0; }
				TCHAR ch = GetFenChar(cs->m_Piece);
				Fen.AppendChar(ch);
			}
		}
		if (Empty > 0) Fen += FString::FromInt(Empty);
		if (x < 7) Fen.AppendChar(TEXT('/'));
	}

	// 2. 활성 색
	Fen += (m_ActivePlayerColor == WHITE) ? TEXT(" w ") : TEXT(" b ");

	// 3. 캐슬링 권리
	FString Castling;
	ACaseActor* WKCase = cases[7 * 8 + 4];
	if (IsValid(WKCase) && IsValid(WKCase->m_Piece)
		&& Cast<AKingPieceActor>(WKCase->m_Piece)
		&& !WKCase->m_Piece->m_hasMoved)
	{
		ACaseActor* WKR = cases[7 * 8 + 7];
		if (IsValid(WKR) && IsValid(WKR->m_Piece)
			&& Cast<ARookPieceActor>(WKR->m_Piece) && !WKR->m_Piece->m_hasMoved)
			Castling += TEXT("K");

		ACaseActor* WQR = cases[7 * 8 + 0];
		if (IsValid(WQR) && IsValid(WQR->m_Piece)
			&& Cast<ARookPieceActor>(WQR->m_Piece) && !WQR->m_Piece->m_hasMoved)
			Castling += TEXT("Q");
	}
	ACaseActor* BKCase = cases[0 * 8 + 4];
	if (IsValid(BKCase) && IsValid(BKCase->m_Piece)
		&& Cast<AKingPieceActor>(BKCase->m_Piece)
		&& !BKCase->m_Piece->m_hasMoved)
	{
		ACaseActor* BKR = cases[0 * 8 + 7];
		if (IsValid(BKR) && IsValid(BKR->m_Piece)
			&& Cast<ARookPieceActor>(BKR->m_Piece) && !BKR->m_Piece->m_hasMoved)
			Castling += TEXT("k");

		ACaseActor* BQR = cases[0 * 8 + 0];
		if (IsValid(BQR) && IsValid(BQR->m_Piece)
			&& Cast<ARookPieceActor>(BQR->m_Piece) && !BQR->m_Piece->m_hasMoved)
			Castling += TEXT("q");
	}
	Fen += Castling.IsEmpty() ? TEXT("-") : Castling;

	// 4. 앙파상(미구현), 하프무브, 풀무브
	Fen += FString::Printf(TEXT(" - 0 %d"), (m_MoveHistory.Num() / 2) + 1);

	return Fen;
}

// ── UCI 합법 수 목록 ─────────────────────────────────────────

TArray<FString> ABoardActor::GetLegalMovesUCI() const
{
	TArray<FString> Moves;
	for (ACaseActor* cs : cases)
	{
		if (!IsValid(cs) || !IsValid(cs->m_Piece)) continue;
		if (cs->m_Piece->m_Color != m_ActivePlayerColor) continue;

		int fromX = cs->m_X, fromY = cs->m_Y;
		TArray<ACaseActor*> Targets = cs->m_Piece->GetAccessibleCases();
		for (ACaseActor* t : Targets)
		{
			FString Uci = FString::Printf(TEXT("%c%d%c%d"),
				TEXT('a') + fromY, 8 - fromX,
				TEXT('a') + t->m_Y, 8 - t->m_X);
			Moves.Add(Uci);
		}
	}
	return Moves;
}

// ── UCI 수 실행 ─────────────────────────────────────────────

bool ABoardActor::ExecuteMoveUCI(const FString& Uci)
{
	if (Uci.Len() < 4)
	{
		UE_LOG(LogTemp, Warning, TEXT("Chess AI: UCI too short: %s"), *Uci);
		return false;
	}

	int fromY    = Uci[0] - TEXT('a');
	int fromRank = Uci[1] - TEXT('0');
	int fromX    = 8 - fromRank;
	int toY      = Uci[2] - TEXT('a');
	int toRank   = Uci[3] - TEXT('0');
	int toX      = 8 - toRank;

	if (fromX < 0 || fromX > 7 || fromY < 0 || fromY > 7 ||
		toX   < 0 || toX   > 7 || toY   < 0 || toY   > 7)
	{
		UE_LOG(LogTemp, Warning, TEXT("Chess AI: UCI out of range: %s"), *Uci);
		return false;
	}

	ACaseActor* FromCase = GetCase(fromX, fromY);
	if (!IsValid(FromCase) || !IsValid(FromCase->m_Piece))
	{
		UE_LOG(LogTemp, Warning, TEXT("Chess AI: No piece at from-square: %s"), *Uci);
		return false;
	}

	TArray<FString> Legal = GetLegalMovesUCI();
	if (!Legal.Contains(Uci))
	{
		UE_LOG(LogTemp, Warning, TEXT("Chess AI: Move not in legal list: %s"), *Uci);
		return false;
	}

	ACaseActor* ToCase = GetCase(toX, toY);
	FromCase->m_Piece->MoveAI(ToCase);
	m_MoveHistory.Add(Uci);
	return true;
}

// ── TCP 경유 AI 수 요청 ──────────────────────────────────────
// HTTP 대신 기존 포트 7777 TCP 연결을 양방향으로 사용.
// UE5 → WPF : {"v":1,"type":"ai_move_request","fen":"...","legal_moves":[...],...}
// WPF → UE5 : {"v":1,"type":"ai_move_response","ok":true,"moveUci":"e2e4","commentKo":"..."}

void ABoardActor::RequestAiMove()
{
	m_bAiThinking = true;
	GetWorldTimerManager().ClearTimer(m_AiRetryTimer);

	FString Fen = GenerateFEN();
	TArray<FString> LegalMoves = GetLegalMovesUCI();

	if (LegalMoves.IsEmpty())
	{
		UE_LOG(LogTemp, Warning, TEXT("Chess AI: 합법 수 없음 — 게임 종료"));
		m_bAiThinking = false;
		NotifyGameOver();
		return;
	}

	// 요청 JSON 빌드
	TSharedPtr<FJsonObject> ReqJson = MakeShared<FJsonObject>();
	ReqJson->SetNumberField(TEXT("v"),    1);
	ReqJson->SetStringField(TEXT("type"), TEXT("ai_move_request"));
	ReqJson->SetStringField(TEXT("fen"),  Fen);

	TArray<TSharedPtr<FJsonValue>> MovesArr;
	for (const FString& M : LegalMoves)
		MovesArr.Add(MakeShared<FJsonValueString>(M));
	ReqJson->SetArrayField(TEXT("legal_moves"), MovesArr);

	TArray<TSharedPtr<FJsonValue>> HistArr;
	for (const FString& H : m_MoveHistory)
		HistArr.Add(MakeShared<FJsonValueString>(H));
	ReqJson->SetArrayField(TEXT("move_history"), HistArr);

	ReqJson->SetNumberField(TEXT("ai_level"),      m_AiLevel);
	ReqJson->SetNumberField(TEXT("time_limit_ms"), 3000.0);

	// WPF가 ReadLineAsync()로 수신하므로 반드시 한 줄(개행 없는) 압축 JSON이어야 함
	FString Body;
	TSharedRef<TJsonWriter<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>> Writer =
		TJsonWriterFactory<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>::Create(&Body);
	FJsonSerializer::Serialize(ReqJson.ToSharedRef(), Writer);

	// 기존 TCP 연결(포트 7777)을 통해 WPF로 전송
	UGameInstance* GI = GetGameInstance();
	UChessCommandSubsystem* Sub = GI
		? GI->GetSubsystem<UChessCommandSubsystem>() : nullptr;

	if (!Sub || !Sub->SendToWpf(Body))
	{
		UE_LOG(LogTemp, Error, TEXT("Chess AI: TCP 전송 실패 (WPF 미연결?) — retry %d/%d"),
			m_AiRetryCount + 1, MaxAiRetries);
		m_bAiThinking = false;
		if (m_AiRetryCount < MaxAiRetries)
		{
			++m_AiRetryCount;
			GetWorldTimerManager().SetTimer(
				m_AiRetryTimer, this, &ABoardActor::RequestAiMove, 2.0f, false);
		}
		else
		{
			UE_LOG(LogTemp, Error, TEXT("Chess AI: 최대 재시도 초과 — AI 턴 스킵"));
			m_AiRetryCount = 0;
		}
		return;
	}

	UE_LOG(LogTemp, Log, TEXT("Chess AI: TCP 요청 전송 — FEN: %s"), *Fen);
}

// ── TCP 응답 수신 (ChessCommandServer가 게임 스레드에서 호출) ──

void ABoardActor::OnAiMoveResponseTcp(bool bOk, const FString& MoveUci,
                                       const FString& CommentKo,
                                       const FString& ErrorMsg)
{
	m_bAiThinking  = false;
	m_AiRetryCount = 0;
	GetWorldTimerManager().ClearTimer(m_AiRetryTimer);

	if (!bOk)
	{
		UE_LOG(LogTemp, Error, TEXT("Chess AI: 응답 오류 — %s"), *ErrorMsg);
		return;
	}

	UE_LOG(LogTemp, Log, TEXT("Chess AI: Move=%s  Comment=%s"), *MoveUci, *CommentKo);

	if (!ExecuteMoveUCI(MoveUci))
	{
		UE_LOG(LogTemp, Error,
			TEXT("Chess AI: 잘못된 수 '%s' — 재시도"), *MoveUci);
		// 잘못된 수: 재시도
		if (m_AiRetryCount < MaxAiRetries)
		{
			++m_AiRetryCount;
			m_bAiThinking = true; // 재시도 동안 다른 입력 차단
			GetWorldTimerManager().SetTimer(
				m_AiRetryTimer, this, &ABoardActor::RequestAiMove, 0.5f, false);
		}
	}
}

// ── 게임 종료 알림 ────────────────────────────────────────────

void ABoardActor::NotifyGameOver()
{
	FString Winner = (m_ActivePlayerColor == WHITE) ? TEXT("BLACK") : TEXT("WHITE");
	FString Msg = FString::Printf(
		TEXT("{\"v\":1,\"type\":\"game_over\",\"winner\":\"%s\"}"), *Winner);

	UGameInstance* GI = GetGameInstance();
	UChessCommandSubsystem* Sub = GI
		? GI->GetSubsystem<UChessCommandSubsystem>() : nullptr;
	if (Sub)
		Sub->SendToWpf(Msg);

	UE_LOG(LogTemp, Log, TEXT("Chess: 게임 종료 — winner=%s"), *Winner);
}

// ── 상태 JSON ────────────────────────────────────────────────

FString ABoardActor::GetStatusJson() const
{
	FString State = m_bAiThinking ? TEXT("thinking") : TEXT("running");
	FString Turn  = (m_ActivePlayerColor == WHITE) ? TEXT("WHITE") : TEXT("BLACK");
	FString Mode;
	if      (m_GameMode == VS_AI)    Mode = TEXT("VS_AI");
	else if (m_GameMode == AI_VS_AI) Mode = TEXT("AI_VS_AI");
	else                             Mode = TEXT("HOTSEAT");
	FString Color = (m_PlayerColor == WHITE) ? TEXT("WHITE") : TEXT("BLACK");

	return FString::Printf(
		TEXT("{\"v\":1,\"type\":\"status\",\"state\":\"%s\","
			 "\"activeTurn\":\"%s\",\"mode\":\"%s\","
			 "\"playerColor\":\"%s\",\"aiThinking\":%s}"),
		*State, *Turn, *Mode, *Color,
		m_bAiThinking ? TEXT("true") : TEXT("false"));
}
