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
set "CSPROJ=%ROOT%app\TermServMultiScreen.csproj"
set "OUT=%ROOT%publication"

rem Lire la version actuelle
for /f "usebackq tokens=*" %%v in (`powershell -NoProfile -Command "$c=Get-Content '%CSPROJ%'; $v=($c -match '<Version>')[0] -replace '.*<Version>(.*?)</Version>.*','$1'; Write-Output $v"`) do set "CURRENT_VERSION=%%v"

echo ===========================================================
echo Version actuelle de l'application : %CURRENT_VERSION%
echo ===========================================================
echo.
set /p NEW_VERSION="Entrez la nouvelle version (laissez vide pour garder %CURRENT_VERSION%) : "

if not "%NEW_VERSION%"=="" (
  echo Mise a jour du fichier csproj avec la version %NEW_VERSION%...
  powershell -NoProfile -Command "(Get-Content '%CSPROJ%') -replace '<Version>%CURRENT_VERSION%</Version>', '<Version>%NEW_VERSION%</Version>' | Set-Content -Encoding UTF8 '%CSPROJ%'"
)

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
