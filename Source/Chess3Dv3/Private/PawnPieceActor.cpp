// Fill out your copyright notice in the Description page of Project Settings.


#include "PawnPieceActor.h"
#include "BoardActor.h"
#include "CaseActor.h"

TArray<ACaseActor*> APawnPieceActor::GetAccessibleCases()
{
	TArray<ACaseActor*> accessibleCases;

	const int direction = (m_Color == BLACK) ? 1 : -1;
	const int frontX    = m_X + direction;

	// 1칸 전진
	if (frontX >= 0 && frontX < 8)
	{
		ACaseActor* frontCase = m_Board->GetCase(frontX, m_Y);
		if (frontCase && frontCase->m_Piece == nullptr)
		{
			accessibleCases.Add(frontCase);

			// 2칸 전진 (첫 이동, 앞이 비어있을 때만)
			const int front2X = m_X + 2 * direction;
			if (!m_hasMoved && front2X >= 0 && front2X < 8)
			{
				ACaseActor* front2Case = m_Board->GetCase(front2X, m_Y);
				if (front2Case && front2Case->m_Piece == nullptr)
					accessibleCases.Add(front2Case);
			}
		}

		// 대각선 캡처
		if (m_Y > 0)
		{
			ACaseActor* diagCase = m_Board->GetCase(frontX, m_Y - 1);
			if (diagCase && diagCase->m_Piece != nullptr
				&& diagCase->m_Piece->m_Color != m_Color)
				accessibleCases.Add(diagCase);
		}
		if (m_Y < 7)
		{
			ACaseActor* diagCase = m_Board->GetCase(frontX, m_Y + 1);
			if (diagCase && diagCase->m_Piece != nullptr
				&& diagCase->m_Piece->m_Color != m_Color)
				accessibleCases.Add(diagCase);
		}
	}

	// TODO: en passant
	// TODO: promotion

	return accessibleCases;
}