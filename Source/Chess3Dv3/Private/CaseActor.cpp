// Fill out your copyright notice in the Description page of Project Settings.


#include "CaseActor.h"
#include "BoardActor.h"

ACaseActor::ACaseActor()
{
}

void ACaseActor::Init(ABoardActor* board, int x, int y)
{
	OnClicked.AddUniqueDynamic(this, &ACaseActor::ClickedLog);
	m_X = x; m_Y = y; m_Board = board;
	auto s = FString::FromInt(m_X);
	s += "/";
	s += FString::FromInt(m_Y);
	s += ": initialized case";
	GLog->Log(s);

	// Set case material
	TArray<UStaticMeshComponent*> currentMeshes;
	GetComponents<UStaticMeshComponent>(currentMeshes);
	for (auto mesh : currentMeshes)
	{
		class UMaterial* material = ((x + y) % 2)? m_OnMaterial: m_OffMaterial;
		SetMaterial(material, true);
	}

	// Add piece if necessary
	TSubclassOf<APieceActor> pieceClass = nullptr;
	if (x == 0 || x == 7)
	{
		if (y == 0 || y == 7)
			pieceClass = m_bpRook;
		else if (y == 1 || y == 6)
			pieceClass = m_bpKnight;
		else if (y == 2 || y == 5)
			pieceClass = m_bpBishop;
		else
		{
			if ((x == 0 || x == 7) && y == 4)
				pieceClass = m_bpQueen;
			else
				pieceClass = m_bpKing;
		}
	} else if (x == 1 || x == 6)
		pieceClass = m_bpPawn;

	if (pieceClass != nullptr)
	{
		UWorld* MyLevel = GetWorld();
		APieceActor* pieceActor = (APieceActor*)MyLevel->SpawnActor(pieceClass, &GetActorTransform());
		class UMaterial* pieceMaterial = (x < 2) ? m_BlackPieceMaterial : m_WhitePieceMaterial;
		TEnumAsByte<PieceColor> pieceColor = (x < 2)? BLACK: WHITE;
		pieceActor->Init(m_Board, x, y, pieceColor, pieceMaterial);
		m_Piece = pieceActor;
	}
}

void ACaseActor::SetMaterial(class UMaterial* material, bool saveAsDefault)
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

void ACaseActor::HighlightMaterial()
{
	if (m_isHighlighted)
		SetMaterial(m_DefaultMaterial, false);
	else
		SetMaterial(m_HighlightMaterial, false);
	m_isHighlighted = !m_isHighlighted;
}

void ACaseActor::ResetHighlight()
{
	if (m_isHighlighted)
	{
		SetMaterial(m_DefaultMaterial, false);
		m_isHighlighted = false;
	}
}

void ACaseActor::ClickedLog(AActor* Target, FKey ButtonPressed)
{
	// Move selected piece if case is valid for a move
	if (m_Board->m_selectedPiece != nullptr)
		m_Board->m_selectedPiece->Move(this);
	// TODO: unselect previous piece?
}