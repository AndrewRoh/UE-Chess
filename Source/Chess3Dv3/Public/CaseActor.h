// Fill out your copyright notice in the Description page of Project Settings.

#pragma once

#include "PieceActor.h"

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "CaseActor.generated.h"

class ABoardActor;

UCLASS()
class CHESS3DV3_API ACaseActor : public AActor
{
	GENERATED_BODY()
	
public:	
	UPROPERTY(BlueprintReadWrite)
		class UStaticMeshComponent* m_Mesh;
	UPROPERTY(BlueprintReadWrite)
		int m_X;
	UPROPERTY(BlueprintReadWrite)
		int m_Y;
	UPROPERTY(BlueprintReadWrite)
		APieceActor* m_Piece;

	UPROPERTY(BlueprintReadWrite)
		TSubclassOf<APieceActor> m_bpPawn;
	UPROPERTY(BlueprintReadWrite)
		TSubclassOf<APieceActor> m_bpRook;
	UPROPERTY(BlueprintReadWrite)
		TSubclassOf<APieceActor> m_bpKnight;
	UPROPERTY(BlueprintReadWrite)
		TSubclassOf<APieceActor> m_bpBishop;
	UPROPERTY(BlueprintReadWrite)
		TSubclassOf<APieceActor> m_bpQueen;
	UPROPERTY(BlueprintReadWrite)
		TSubclassOf<APieceActor> m_bpKing;

	UPROPERTY(BlueprintReadWrite)
		class UMaterial* m_WhitePieceMaterial;
	UPROPERTY(BlueprintReadWrite)
		class UMaterial* m_BlackPieceMaterial;
	UPROPERTY(BlueprintReadWrite)
		class UMaterial* m_OnMaterial;
	UPROPERTY(BlueprintReadWrite)
		class UMaterial* m_OffMaterial;
	UPROPERTY(BlueprintReadWrite)
		class UMaterial* m_HighlightMaterial;

	// Sets default values for this actor's properties
	ACaseActor();
	void Init(ABoardActor* board, int x, int y);
	void HighlightMaterial();
	void ResetHighlight();
	void SetMaterial(class UMaterial* material, bool saveAsDefault);
private:
	UFUNCTION()
		void ClickedLog(AActor* Target, FKey ButtonPressed);

	ABoardActor* m_Board;
	class UMaterial* m_DefaultMaterial;
	bool m_isHighlighted = false;
};
