# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Chess3Dv3** is an Unreal Engine 5.1 C++ project — a local hot-seat chess game. It does not include online multiplayer or Stockfish integration. Gameplay is turn-based with click-to-move controls.

## Building

Open the project via `Chess3Dv3.uproject` in Unreal Engine 5.1. Build targets follow standard UE5 conventions:

- **IDE**: Visual Studio Community 2026
- **Editor build**: `Chess3Dv3Editor Development Win64`
- **Game build**: `Chess3Dv3 Development Win64`
- **Regenerate project files**: Right-click `Chess3Dv3.uproject` → *Generate Visual Studio project files*.

There are no custom build scripts — all compilation goes through UBT.

## Module Structure

Single runtime module: `Source/Chess3Dv3/` with `Chess3Dv3.Build.cs`.

Key dependencies: `Core`, `CoreUObject`, `Engine`, `InputCore`.

Source is organized under `Source/Chess3Dv3/`:

```
Chess3Dv3/
  Chess3Dv3.h / Chess3Dv3.cpp          — Module entry point
  Chess3Dv3GameModeBase.h / .cpp        — Default GameMode base
  Chess3Dv3.Build.cs                    — Module build rules
  Chess3Dv3.Target.cs / Editor.Target.cs
  Public/                               — Header files
    BoardActor.h
    CaseActor.h
    PieceActor.h
    BishopPieceActor.h
    KingPieceActor.h
    KnightPieceActor.h
    PawnPieceActor.h
    QueenPieceActor.h
    RookPieceActor.h
  Private/                              — Implementation files
    BoardActor.cpp
    CaseActor.cpp
    PieceActor.cpp
    BishopPieceActor.cpp
    KingPieceActor.cpp
    KnightPieceActor.cpp
    PawnPieceActor.cpp
    QueenPieceActor.cpp
    RookPieceActor.cpp
```

## Architecture

### Core Actors

- **`ABoardActor`** — Central board actor. Owns an array of `ACaseActor*` (the 64 squares). Tracks the active player color (`m_ActivePlayerColor`) and the currently selected piece (`m_selectedPiece`). Provides `GetCase(x, y)` and `EndTurn()`.
- **`ACaseActor`** — Individual board square. Holds a reference to the piece on it (`m_Piece`), its grid coordinates (`m_X`, `m_Y`), and Blueprint-assigned material/piece-class references used for spawning. Handles click events to trigger move logic.
- **`APieceActor`** — Base piece actor. Tracks grid position (`m_X`, `m_Y`), color (`m_Color`), and movement state (`m_hasMoved`). Provides `Init()`, `Move()`, and virtual `GetAccessibleCases()`. Click events highlight valid moves. Subclassed per piece type.

### Piece Subclasses

Each piece overrides `GetAccessibleCases()` to return valid target squares:

- `APawnPieceActor`
- `ARookPieceActor`
- `AKnightPieceActor`
- `ABishopPieceActor`
- `AQueenPieceActor`
- `AKingPieceActor`

### Enums

- **`PieceColor`** — `BLACK` / `WHITE`. Defined in `BoardActor.h`, used across all actors.

### Content (`Content/`)

- `Blueprints/` — Blueprint subclasses of each C++ actor (e.g., `BP_PawnPieceActor`, `Bp_BoardActor`), plus `SoloGameMode` and `SoloPlayerController`.
- `FBX/` — Static mesh assets for chess pieces.
- `Materials/` — Materials for pieces, tiles, highlight, and marble board.

## Key Conventions

- **Move generation belongs in `GetAccessibleCases()`** on each piece subclass — not in `Tick` and not in `BoardActor`.
- **Turn management is in `ABoardActor::EndTurn()`**. Flip `m_ActivePlayerColor` there.
- **No networking.** This is a single-machine, two-player game. Do not add replication.
- **No Stockfish / UCI.** There is no AI engine integration in this project.
- **Blueprint-driven setup.** Piece meshes, materials, and BP classes are assigned in Blueprint, not hardcoded in C++.
- Class names use no shared prefix. Files follow `<ClassName>.h` / `<ClassName>.cpp` under `Public/` and `Private/`.
