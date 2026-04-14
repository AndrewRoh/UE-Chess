// Fill out your copyright notice in the Description page of Project Settings.


#include "KingPieceActor.h"
#include "BoardActor.h"
#include "CaseActor.h"

TArray<ACaseActor*> AKingPieceActor::GetAccessibleCases()
{
	TArray<ACaseActor*> accessibleCases;
	for (int deltaX = -1; deltaX < 2; deltaX++)
	{
		for (int deltaY = -1; deltaY < 2; deltaY++)
		{
			if (deltaX == 0 && deltaY == 0)
				continue;

			int x = m_X + deltaX, y = m_Y + deltaY;
			if (x >= 0 && x < 8 && y >= 0 && y < 8)
			{
				ACaseActor* targetCase = m_Board->GetCase(x, y);
				// Avoid case with a piece
				if (targetCase && (targetCase->m_Piece == nullptr
					|| targetCase->m_Piece->m_Color != m_Color))
					accessibleCases.Add(targetCase);
			}
			// else: 범위 밖 칸은 건너뜀 (break → continue)
		}
	}

	// Roque
	if (!m_hasMoved)
	{
		ACaseActor* rookCase = m_Board->GetCase(m_X, 0);
		if (rookCase->m_Piece != nullptr && !rookCase->m_Piece->m_hasMoved)
		{
			bool isValidRoque = true;
			int y = 1;
			while (y < m_Y && isValidRoque)
			{
				isValidRoque = (m_Board->GetCase(m_X, y)->m_Piece == nullptr);
				y++;
			}
			if (isValidRoque)
				accessibleCases.Add(m_Board->GetCase(m_X, m_Y - 2));

		}

		rookCase = m_Board->GetCase(m_X, 7);
		if (rookCase->m_Piece != nullptr && !rookCase->m_Piece->m_hasMoved)
		{
			bool isValidRoque = true;
			int y = m_Y + 1;
			while (y < 8 && isValidRoque)
			{
				isValidRoque = (m_Board->GetCase(m_X, y)->m_Piece == nullptr);
				y++;
			}
			if (isValidRoque)
				accessibleCases.Add(m_Board->GetCase(m_X, m_Y + 2));
		}
	}

	// Avoid mate cases
	// TODO: make something better than that...
	if (m_Color == m_Board->m_ActivePlayerColor)
	{
		for (auto cs : m_Board->cases)
		{
			if (cs->m_Piece != nullptr && cs->m_Piece->m_Color != m_Color)
			{
				for (auto invalidCase : cs->m_Piece->GetAccessibleCases())
				{
					accessibleCases.Remove(invalidCase);
				}
			}
		}
	}

	return accessibleCases;
}