// Fill out your copyright notice in the Description page of Project Settings.

#include "ChessCommandServer.h"
#include "BoardActor.h"

#include "Async/Async.h"
#include "SocketSubsystem.h"
#include "Interfaces/IPv4/IPv4Address.h"
#include "IPAddress.h"
#include "Json.h"
#include "Kismet/GameplayStatics.h"

// ── FChessServerThread ────────────────────────────────────────

FChessServerThread::FChessServerThread(UChessCommandSubsystem* InOwner)
	: Owner(InOwner)
{}

bool FChessServerThread::Init()
{
	return true;
}

bool FChessServerThread::SendToClient(const FString& JsonLine)
{
	FScopeLock Lock(&SendMutex);
	if (!ConnSocket) return false;

	FString Line = JsonLine + TEXT("\n");
	FTCHARToUTF8 Conv(*Line);
	int32 Sent = 0;
	return ConnSocket->Send(
		reinterpret_cast<const uint8*>(Conv.Get()), Conv.Length(), Sent);
}

uint32 FChessServerThread::Run()
{
	ISocketSubsystem* SS = ISocketSubsystem::Get(PLATFORM_SOCKETSUBSYSTEM);

	// 리스너 소켓 생성
	FSocket* Listener = SS->CreateSocket(NAME_Stream, TEXT("ChessServer"), false);
	if (!Listener)
	{
		UE_LOG(LogTemp, Error, TEXT("ChessServer: 소켓 생성 실패"));
		return 1;
	}

	Listener->SetNonBlocking(true);
	Listener->SetReuseAddr(true);

	TSharedRef<FInternetAddr> Addr = SS->CreateInternetAddr();
	Addr->SetAnyAddress();
	Addr->SetPort(7777);

	if (!Listener->Bind(*Addr))
	{
		UE_LOG(LogTemp, Error, TEXT("ChessServer: 포트 7777 바인드 실패"));
		SS->DestroySocket(Listener);
		return 1;
	}

	Listener->Listen(1);
	UE_LOG(LogTemp, Log, TEXT("ChessServer: 포트 7777 대기 중..."));

	FString  Accum;
	uint8    Buf[4097];

	while (!bStop)
	{
		// 새 연결 수락
		if (!ConnSocket)
		{
			bool bPending = false;
			if (Listener->HasPendingConnection(bPending) && bPending)
			{
				FSocket* Accepted = Listener->Accept(TEXT("WPF"));
				if (Accepted)
				{
					Accepted->SetNonBlocking(true);
					{
						FScopeLock Lock(&SendMutex);
						ConnSocket = Accepted;
					}
					UE_LOG(LogTemp, Log, TEXT("ChessServer: WPF 연결됨"));
				}
			}
		}

		// 수신 루프
		if (ConnSocket)
		{
			uint32 PendingSize = 0;
			while (ConnSocket->HasPendingData(PendingSize) && PendingSize > 0 && !bStop)
			{
				int32 BytesRead = 0;
				int32 ToRead = FMath::Min((int32)PendingSize, (int32)(sizeof(Buf) - 1));
				bool bRecvOk = ConnSocket->Recv(Buf, ToRead, BytesRead,
					ESocketReceiveFlags::None);

				if (!bRecvOk || BytesRead <= 0)
				{
					// 연결 끊김
					{
						FScopeLock Lock(&SendMutex);
						ConnSocket->Close();
						SS->DestroySocket(ConnSocket);
						ConnSocket = nullptr;
					}
					Accum.Empty();
					UE_LOG(LogTemp, Log, TEXT("ChessServer: WPF 연결 종료"));
					break;
				}

				// UTF-8 → FString
				Buf[BytesRead] = 0;
				FString Chunk(ANSI_TO_TCHAR(reinterpret_cast<ANSICHAR*>(Buf)));
				Accum += Chunk;

				// 개행 단위로 명령 처리
				int32 NL;
				while (Accum.FindChar(TEXT('\n'), NL))
				{
					FString Line = Accum.Left(NL).TrimStartAndEnd();
					if (!Line.IsEmpty())
						Owner->ProcessCommand(Line);
					Accum = Accum.Mid(NL + 1);
				}
			}
		}

		FPlatformProcess::Sleep(0.05f);
	}

	// 정리
	{
		FScopeLock Lock(&SendMutex);
		if (ConnSocket)
		{
			ConnSocket->Close();
			SS->DestroySocket(ConnSocket);
			ConnSocket = nullptr;
		}
	}
	Listener->Close();
	SS->DestroySocket(Listener);

	UE_LOG(LogTemp, Log, TEXT("ChessServer: 스레드 종료"));
	return 0;
}

void FChessServerThread::Stop()
{
	bStop = true;
}

// ── UChessCommandSubsystem ────────────────────────────────────

void UChessCommandSubsystem::Initialize(FSubsystemCollectionBase& Collection)
{
	Super::Initialize(Collection);

	ServerRunnable = new FChessServerThread(this);
	ServerThread   = FRunnableThread::Create(
		ServerRunnable,
		TEXT("ChessCommandServer"),
		0,
		TPri_BelowNormal);
}

void UChessCommandSubsystem::Deinitialize()
{
	if (ServerRunnable) ServerRunnable->Stop();
	if (ServerThread)   { ServerThread->WaitForCompletion(); delete ServerThread; }
	if (ServerRunnable) { delete ServerRunnable; }
	ServerThread   = nullptr;
	ServerRunnable = nullptr;

	Super::Deinitialize();
}

bool UChessCommandSubsystem::SendToWpf(const FString& JsonLine)
{
	if (!ServerRunnable) return false;
	return ServerRunnable->SendToClient(JsonLine);
}

ABoardActor* UChessCommandSubsystem::FindBoard() const
{
	UWorld* World = GetGameInstance() ? GetGameInstance()->GetWorld() : nullptr;
	if (!World) return nullptr;

	TArray<AActor*> Found;
	UGameplayStatics::GetAllActorsOfClass(World, ABoardActor::StaticClass(), Found);
	return (Found.Num() > 0) ? Cast<ABoardActor>(Found[0]) : nullptr;
}

void UChessCommandSubsystem::ProcessCommand(const FString& JsonLine)
{
	// JSON 파싱은 백그라운드 스레드에서 수행 (FJsonSerializer는 스레드 세이프)
	TSharedPtr<FJsonObject> JsonObj;
	TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(JsonLine);
	if (!FJsonSerializer::Deserialize(Reader, JsonObj) || !JsonObj.IsValid())
	{
		UE_LOG(LogTemp, Warning, TEXT("ChessServer: JSON 파싱 실패: %s"), *JsonLine);
		return;
	}

	// type 필드로 메시지 종류 판별 (cmd 필드는 command 타입에만)
	FString Type;
	JsonObj->TryGetStringField(TEXT("type"), Type);

	// ── AI 수 응답 (WPF → UE5) ─────────────────────────────────
	if (Type == TEXT("ai_move_response"))
	{
		bool    bOk      = false;
		FString MoveUci;
		FString CommentKo;
		FString ErrorMsg;

		JsonObj->TryGetBoolField(TEXT("ok"),        bOk);
		JsonObj->TryGetStringField(TEXT("moveUci"), MoveUci);
		JsonObj->TryGetStringField(TEXT("commentKo"), CommentKo);
		JsonObj->TryGetStringField(TEXT("error"),   ErrorMsg);

		AsyncTask(ENamedThreads::GameThread, [this, bOk, MoveUci, CommentKo, ErrorMsg]()
		{
			ABoardActor* Board = FindBoard();
			if (!Board) return;
			Board->OnAiMoveResponseTcp(bOk, MoveUci, CommentKo, ErrorMsg);
		});
		return;
	}

	// ── 일반 커맨드 (WPF → UE5) ───────────────────────────────
	FString Cmd;
	if (!JsonObj->TryGetStringField(TEXT("cmd"), Cmd))
	{
		UE_LOG(LogTemp, Warning, TEXT("ChessServer: 'cmd' 필드 없음: %s"), *JsonLine);
		return;
	}

	// options 객체 (없을 수도 있음)
	TSharedPtr<FJsonObject> Opts = JsonObj->GetObjectField(TEXT("options"));

	// 게임 스레드로 전환
	AsyncTask(ENamedThreads::GameThread, [this, Cmd, Opts]()
	{
		ABoardActor* Board = FindBoard();
		if (!Board)
		{
			UE_LOG(LogTemp, Error, TEXT("ChessServer: ABoardActor를 찾을 수 없음"));
			return;
		}

		if (Cmd == TEXT("restart"))
		{
			UE_LOG(LogTemp, Log, TEXT("ChessServer: [restart] 실행"));
			Board->RestartGame();
		}
		else if (Cmd == TEXT("new_game"))
		{
			FString ModeStr  = TEXT("HOTSEAT");
			FString ColorStr = TEXT("WHITE");
			int     AiLevel  = 2;

			if (Opts.IsValid())
			{
				Opts->TryGetStringField(TEXT("mode"),        ModeStr);
				Opts->TryGetStringField(TEXT("playerColor"), ColorStr);
				double AiLevelD = 2.0;
				if (Opts->TryGetNumberField(TEXT("aiLevel"), AiLevelD))
					AiLevel = (int)AiLevelD;
			}

			TEnumAsByte<PieceColor>      Color = (ColorStr == TEXT("BLACK")) ? BLACK : WHITE;
			TEnumAsByte<EChessGameMode>  Mode  = (ModeStr  == TEXT("VS_AI")) ? VS_AI : HOTSEAT;

			UE_LOG(LogTemp, Log,
				TEXT("ChessServer: [new_game] mode=%s color=%s aiLevel=%d"),
				*ModeStr, *ColorStr, AiLevel);

			Board->NewGame(Color, Mode, AiLevel);
		}
		else
		{
			UE_LOG(LogTemp, Warning, TEXT("ChessServer: 알 수 없는 cmd: %s"), *Cmd);
		}
	});
}
