// Fill out your copyright notice in the Description page of Project Settings.

#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "PieceActor.generated.h"

class ABoardActor;
class ACaseActor;
enum PieceColor;

UCLASS()
class CHESS3DV3_API APieceActor : public AActor
{
	GENERATED_BODY()
	
public:	
	int m_X;
	int m_Y;
	TEnumAsByte<PieceColor> m_Color;
	bool m_hasMoved = false;

	APieceActor();

	virtual void Tick(float DeltaTime) override;

	UFUNCTION()
		void Init(ABoardActor* board, int x, int y, TEnumAsByte<PieceColor> color, class UMaterial* material);
	UFUNCTION()
		void Move(ACaseActor* targetCase);

	// AI 전용 — 하이라이트 없이 이동
	void MoveAI(ACaseActor* targetCase);


	UPROPERTY(BlueprintReadWrite)
		class UMaterial* m_HighlightMaterial;
	virtual TArray<ACaseActor*> GetAccessibleCases();
protected:
	ABoardActor* m_Board;
private:
	UFUNCTION()
	void ClickedLog(AActor* Target, FKey ButtonPressed);

	void HighlightMaterial();
	void SetMaterial(class UMaterial* material, bool saveAsDefault);

	bool isValidMove(ACaseActor* targetCase);

	class UMaterial* m_DefaultMaterial;
	bool m_isHighlighted = false;

	// Move animation
	FVector m_MoveStartLocation;
	FVector m_MoveTargetLocation;
	float m_MoveElapsed = 0.f;
	bool m_bIsMoving = false;
};
