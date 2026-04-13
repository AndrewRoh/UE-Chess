// Fill out your copyright notice in the Description page of Project Settings.


#include "PieceActor.h"
#include "BoardActor.h"
#include "CaseActor.h"

static constexpr float MoveDuration = 0.7f;

APieceActor::APieceActor()
{
	PrimaryActorTick.bCanEverTick = true;
	OnClicked.AddUniqueDynamic(this, &APieceActor::ClickedLog);
}

void APieceActor::Tick(float DeltaTime)
{
	Super::Tick(DeltaTime);

	if (!m_bIsMoving)
		return;

	m_MoveElapsed += DeltaTime;
	const float Alpha = FMath::Clamp(m_MoveElapsed / MoveDuration, 0.f, 1.f);

	// Smoothstep: 3t² - 2t³
	const float Smooth = Alpha * Alpha * (3.f - 2.f * Alpha);
	SetActorLocation(FMath::Lerp(m_MoveStartLocation, m_MoveTargetLocation, Smooth));

	if (Alpha >= 1.f)
	{
		m_bIsMoving = false;
		m_Board->EndTurn();
	}
}

void APieceActor::Init(ABoardActor* board, int x, int y, TEnumAsByte<PieceColor> color, class UMaterial* material)
{
	m_Board = board;
	m_X = x;
	m_Y = y;
	m_Color = color;
	SetMaterial(material, true);
}

void APieceActor::SetMaterial(class UMaterial* material, bool saveAsDefault)
{
	TArray<UStaticMeshComponent*> pieceMeshes;
	GetComponents<UStaticMeshComponent>(pieceMeshes);
	for (auto pieceMesh : pieceMeshes)
	{
		pieceMesh->SetMaterial(0, material);
	}
	if (saveAsDefault)
		m_DefaultMaterial = material;
}

void APieceActor::HighlightMaterial()
{
	if (m_isHighlighted)
		SetMaterial(m_DefaultMaterial, false);
	else
		SetMaterial(m_HighlightMaterial, false);
	m_isHighlighted = !m_isHighlighted;

	TArray<ACaseActor*> cs = GetAccessibleCases();
	for (auto c : cs)
	{
		c->HighlightMaterial();
	}
}

TArray<ACaseActor*> APieceActor::GetAccessibleCases()
{
	// TODO: make this a pure virtual ?
	TArray<ACaseActor*> accessibleCases;

	return accessibleCases;
}

void APieceActor::ClickedLog(AActor* Target, FKey ButtonPressed)
{
	// Piece selection

	if (m_Color == m_Board->m_ActivePlayerColor)
	{
		if (m_Board->m_selectedPiece != nullptr)
			m_Board->m_selectedPiece->HighlightMaterial();
		// Highlight current piece and accessible cases
		HighlightMaterial();
		m_Board->m_selectedPiece = this;
	}
	// Move on a piece
	else if (m_Board->m_selectedPiece != nullptr)
		m_Board->m_selectedPiece->Move(m_Board->GetCase(m_X, m_Y));
	// TODO: unselect previous piece? on invalid click? On right click?
}

bool APieceActor::isValidMove(ACaseActor* targetCase)
{
	int targetX = targetCase->m_X, targetY = targetCase->m_Y;
	for (auto cs : GetAccessibleCases())
	{
		if (cs->m_X == targetX && cs->m_Y == targetY)
			return true;
	}
	return false;
}

void APieceActor::Move(ACaseActor* targetCase)
{
	if (m_bIsMoving)
		return;

	if (isValidMove(targetCase))
	{
		HighlightMaterial();

		// 인간 플레이어 수를 이동 히스토리에 기록 (AI 컨텍스트용)
		// UCI: file = 'a'+y, rank = 8-x
		FString Uci = FString::Printf(TEXT("%c%d%c%d"),
			TEXT('a') + m_Y,             8 - m_X,
			TEXT('a') + targetCase->m_Y, 8 - targetCase->m_X);
		m_Board->m_MoveHistory.Add(Uci);

		m_Board->GetCase(m_X, m_Y)->m_Piece = nullptr;
		// TODO: make something cleaner than just destroying
		// "Destroy" opponent piece if any
		if (targetCase->m_Piece != nullptr)
			targetCase->m_Piece->Destroy();
		targetCase->m_Piece = this;
		m_X = targetCase->m_X;
		m_Y = targetCase->m_Y;
		m_hasMoved = true;

		// Start animated movement — EndTurn() is called from Tick when complete
		m_MoveStartLocation = GetActorLocation();
		m_MoveTargetLocation = targetCase->GetActorLocation();
		m_MoveElapsed = 0.f;
		m_bIsMoving = true;
	}
}

void APieceActor::MoveAI(ACaseActor* targetCase)
{
	if (m_bIsMoving) return;

	if (isValidMove(targetCase))
	{
		// 하이라이트 없이 이동 (AI 전용)
		m_Board->GetCase(m_X, m_Y)->m_Piece = nullptr;
		if (targetCase->m_Piece != nullptr)
			targetCase->m_Piece->Destroy();
		targetCase->m_Piece = this;
		m_X = targetCase->m_X;
		m_Y = targetCase->m_Y;
		m_hasMoved = true;

		m_MoveStartLocation = GetActorLocation();
		m_MoveTargetLocation = targetCase->GetActorLocation();
		m_MoveElapsed = 0.f;
		m_bIsMoving = true;
	}
}