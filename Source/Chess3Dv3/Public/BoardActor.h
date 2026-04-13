// Fill out your copyright notice in the Description page of Project Settings.

#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "Interfaces/IHttpRequest.h"
#include "Interfaces/IHttpResponse.h"
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
	VS_AI
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

	// ── 상태 JSON (WPF 피드백용) ───────────────────────────────
	FString GetStatusJson() const;

protected:
	virtual void BeginPlay() override;

private:
	void RequestAiMove();
	void OnAiMoveResponse(FHttpRequestPtr Request, FHttpResponsePtr Response, bool bWasSuccessful);
};
