// Copyright Epic Games, Inc. All Rights Reserved.

using UnrealBuildTool;
using System.Collections.Generic;

public class Chess3Dv3EditorTarget : TargetRules
{
	public Chess3Dv3EditorTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Editor;
		DefaultBuildSettings = BuildSettingsVersion.V6;            // Upgrade 경고 제거
		IncludeOrderVersion = EngineIncludeOrderVersion.Unreal5_7; // Include 순서 경고 제거
		CppStandard = CppStandardVersion.Cpp20;                    // C++20 경고 제거
		bOverrideBuildEnvironment = true;                           // 빌드 환경 충돌 해결

		ExtraModuleNames.Add("Chess3Dv3");
	}
}
