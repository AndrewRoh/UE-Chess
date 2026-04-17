// Fill out your copyright notice in the Description page of Project Settings.

#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "BoardActor.generated.h"

class ACaseActor;
class APieceActor;

UENUM()
enum PieceColor : int
{
	BLACK,
	WHITE
};

UENUM()
enum EChessGameMode : uint8
{
	HOTSEAT,
	VS_AI,
	AI_VS_AI
};

UCLASS()
class CHESS3DV3_API ABoardActor : public AActor
{
	GENERATED_BODY()


public:
	UPROPERTY(EditAnywhere, BlueprintReadWrite)
	class USceneComponent* SceneComponent;

	UPROPERTY(BlueprintReadWrite)
	TSubclassOf<ACaseActor> BpCase;

	UPROPERTY(BlueprintReadWrite)
	TArray<ACaseActor*> cases;

	APieceActor* m_selectedPiece;

	TEnumAsByte<PieceColor> m_ActivePlayerColor = WHITE;

	// ── 게임 모드 ──────────────────────────────────────────────
	TEnumAsByte<EChessGameMode> m_GameMode = HOTSEAT;
	TEnumAsByte<PieceColor> m_PlayerColor = WHITE; // 인간 플레이어 색
	int m_AiLevel = 2;
	bool m_bAiThinking = false;

	// ── 이동 히스토리 (AI 컨텍스트용) ─────────────────────────
	TArray<FString> m_MoveHistory;

	ABoardActor();

	ACaseActor* GetCase(int x, int y);
	void EndTurn();

	// ── 게임 제어 (WPF 커맨드에서 호출) ───────────────────────
	void RestartGame();
	void NewGame(TEnumAsByte<PieceColor> playerColor, TEnumAsByte<EChessGameMode> mode, int aiLevel);

	// ── AI 지원 ────────────────────────────────────────────────
	FString GenerateFEN() const;
	TArray<FString> GetLegalMovesUCI() const;
	bool ExecuteMoveUCI(const FString& Uci);
	void TriggerAiTurnIfNeeded();

	/** TCP 경유로 WPF에서 도착한 AI 수 응답 처리 (게임 스레드에서 호출) */
	void OnAiMoveResponseTcp(bool bOk, const FString& MoveUci,
	                         const FString& CommentKo, const FString& ErrorMsg);

	/** 게임 종료 알림을 WPF로 전송 */
	void NotifyGameOver(const FString& Winner);

	/** 보드에서 해당 색 킹이 살아있는지 확인 */
	bool IsKingAlive(TEnumAsByte<PieceColor> Color) const;

	// ── 상태 JSON (WPF 피드백용) ───────────────────────────────
	FString GetStatusJson() const;

protected:
	virtual void BeginPlay() override;

private:
	void RequestAiMove();

	// AI 재시도용 타이머 (TCP 전송 실패 시)
	FTimerHandle m_AiRetryTimer;
	int          m_AiRetryCount = 0;
	static constexpr int MaxAiRetries = 3;
};
