@echo off
setlocal
rem ---------------------------------------------------------------------------
rem  Compile "Bureau a distance multi-ecrans" (WinUI 3 / .NET 10).
rem  Produit UN SEUL fichier autonome : publication\TermServMultiScreen.exe
rem
rem  Tout est embarque dedans : .NET 10, Windows App SDK, WinUI, l'icone et les
rem  XAML compiles. Aucun runtime a installer sur le poste cible.
rem ---------------------------------------------------------------------------

set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"

set "ROOT=%~dp0"
set "OUT=%ROOT%publication"

if exist "%OUT%" rd /s /q "%OUT%"

echo Compilation du fichier unique autonome (une a deux minutes)...
"%DOTNET%" publish "%ROOT%app\TermServMultiScreen.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:WindowsAppSDKSelfContained=true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:IncludeAllContentForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:PublishReadyToRun=false ^
  -p:DebugType=none ^
  -o "%OUT%"

if errorlevel 1 (
  echo.
  echo [ERREUR] La compilation a echoue.
  exit /b 1
)

echo.
dir /b "%OUT%"
echo.

echo Signature de l'executable...
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%signer-release.ps1"
if errorlevel 1 (
  echo [AVERTISSEMENT] La signature a echoue. Avez-vous le fichier private_key.xml ?
)

echo.
echo OK : "%OUT%\TermServMultiScreen.exe"
echo Ce seul fichier suffit : copiez-le ou vous voulez, sur n'importe quel poste Windows 10/11 64 bits.
endlocal
