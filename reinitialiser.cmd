@echo off
setlocal
rem ---------------------------------------------------------------------------
rem  Remet l'application a l'etat neuf : aucune connexion, aucun serveur,
rem  aucun utilisateur, aucun fichier .rdp genere.
rem  Fermez l'application avant de lancer ce script.
rem ---------------------------------------------------------------------------

set "DATA=%LOCALAPPDATA%\TermServMultiScreen"

tasklist /fi "imagename eq TermServMultiScreen.exe" | find /i "TermServMultiScreen.exe" >nul
if not errorlevel 1 (
  echo [ATTENTION] L'application est encore ouverte : fermez-la d'abord,
  echo             sinon elle reecrira ses connexions en se fermant.
  exit /b 1
)

echo Cela supprime les connexions enregistrees et les fichiers .rdp generes.
echo Dossier concerne : %DATA%
choice /c ON /n /m "Continuer ? [O]ui / [N]on : "
if errorlevel 2 exit /b 0

if exist "%DATA%\config.json" (
  copy /y "%DATA%\config.json" "%DATA%\config.json.sauvegarde" >nul
  del /q "%DATA%\config.json"
  echo   connexions supprimees ^(sauvegarde : config.json.sauvegarde^)
)
if exist "%DATA%\sessions\*.rdp" (
  del /q "%DATA%\sessions\*.rdp"
  echo   fichiers .rdp generes supprimes
)

echo.
echo OK : au prochain lancement, l'application sera vide.
endlocal
