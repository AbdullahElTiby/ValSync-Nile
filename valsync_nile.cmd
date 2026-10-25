@ECHO OFF
SETLOCAL
PUSHD "%~dp0"
set _arch=x86
IF "%PROCESSOR_ARCHITECTURE%"=="AMD64" (set _arch=x86_64)
IF DEFINED PROCESSOR_ARCHITEW6432 (set _arch=x86_64)

net session >nul 2>&1
IF ERRORLEVEL 1 (
    echo This script must be run as administrator.
    echo Right click - Run as administrator.
    pause
    goto end
)

:menu
cls
echo ==========================================================
echo   ValSync Nile
echo   Works on WE (TE Data), Vodafone, Orange, Etisalat/e^&
echo ==========================================================
echo.
echo   Websites (window stays open while active):
echo     1  Recommended - fake packets + DNS
echo     2  Fixed TTL 5 fake packets + DNS
echo     3  Aggressive  - SNI split + extra fake packets + DNS
echo     4  Low TTL, no DNS redirect
echo     5  Legacy compatible mode + DNS
echo     6  Blacklist only (egypt-blacklist.txt)
echo.
echo   Voice chat in games / calls (64-bit Windows, runs alongside 1-6):
echo     V1  Light - Discord voice + STUN calls
echo     V2  Full  - adds Valorant/Vivox and all UDP voice
echo.
echo     S  Install as Windows service (starts with Windows)
echo     R  Remove services
echo     Q  Quit
echo.
set "choice="
set /p choice=Choose:
IF /I "%choice%"=="Q" goto end
IF /I "%choice%"=="S" goto service
IF /I "%choice%"=="R" goto remove
IF /I "%choice%"=="V1" goto voice
IF /I "%choice%"=="V2" goto voice
CALL profiles.cmd %choice%
IF NOT DEFINED GDPI_ARGS goto menu

echo.
echo Starting profile %choice%: %GDPI_NAME%
start "ValSync Nile" "%_arch%\goodbyedpi.exe" %GDPI_ARGS%
echo Started. Close the ValSync Nile window to stop it. If sites still fail, try another profile.
pause
goto menu

:voice
IF NOT "%_arch%"=="x86_64" (
    echo Voice bypass needs 64-bit Windows.
    pause
    goto menu
)
CALL voice_profiles.cmd %choice:~1%
echo.
echo Starting voice profile %choice%: %VOICE_NAME%
start "ValSync Nile Voice" /min "voice\winws.exe" %VOICE_ARGS%
echo Started (minimized). Close the "ValSync Nile Voice" window to stop it.
echo Restart the game / Discord so voice reconnects through the bypass.
pause
goto menu

:service
CALL service_install.cmd
goto end

:remove
CALL service_remove.cmd

:end
POPD
ENDLOCAL
