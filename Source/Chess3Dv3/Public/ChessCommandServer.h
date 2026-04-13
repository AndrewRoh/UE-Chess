// Fill out your copyright notice in the Description page of Project Settings.

#pragma once

#include "CoreMinimal.h"
#include "Subsystems/GameInstanceSubsystem.h"
#include "HAL/Runnable.h"
#include "HAL/RunnableThread.h"
#include "Sockets.h"
#include "ChessCommandServer.generated.h"

class ABoardActor;

/** 백그라운드 스레드 — TCP Accept/Recv 루프 */
class FChessServerThread final : public FRunnable
{
public:
	explicit FChessServerThread(class UChessCommandSubsystem* InOwner);

	virtual bool   Init() override;
	virtual uint32 Run()  override;
	virtual void   Stop() override;

private:
	UChessCommandSubsystem* Owner;
	TAtomic<bool>           bStop{ false };
};

/**
 * TCP 커맨드 서버 — 게임 인스턴스 서브시스템으로 자동 생성됨.
 * WPF가 127.0.0.1:7777로 JSON Lines 명령을 전송하면 이 서브시스템이 수신 후
 * 게임 스레드에서 ABoardActor의 메서드를 호출한다.
 */
UCLASS()
class CHESS3DV3_API UChessCommandSubsystem : public UGameInstanceSubsystem
{
	GENERATED_BODY()

public:
	virtual void Initialize(FSubsystemCollectionBase& Collection) override;
	virtual void Deinitialize() override;

	/** 스레드에서 호출됨 — 게임 스레드로 전환 후 처리 */
	void ProcessCommand(const FString& JsonLine);

private:
	FChessServerThread* ServerRunnable = nullptr;
	FRunnableThread*    ServerThread   = nullptr;

	ABoardActor* FindBoard() const;
};
