# AppDex

Windows app launcher by Spangblop, personal project that's just meant to be a quick way to launch apps from different app stores and all over your computer. It tracks launch counts and playtime, imports installed Steam and Epic Games titles, and supports custom artwork, launch arguments, and appearance presets.

## Run the beta

Download the Windows ZIP from [Releases](../../releases), extract it, and open `AppDex.exe`.

## Build from source

Use Visual Studio Build Tools with MSBuild and the .NET Framework 4.8 targeting pack. From a Developer PowerShell prompt, run:

```powershell
msbuild .\AppDex.csproj /p:Configuration=Release
```

The executable is written to `bin\Release\AppDex.exe`.
