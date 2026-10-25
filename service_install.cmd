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

echo Install ValSync Nile as a Windows service (auto-starts with Windows).
echo Test profiles first with valsync_nile.cmd and pick the one that works on your ISP.
echo.
echo   1  Recommended - fake packets + DNS
echo   2  Fixed TTL 5 fake packets + DNS
echo   3  Aggressive  - SNI split + extra fake packets + DNS
echo   4  Low TTL, no DNS redirect
echo   5  Legacy compatible mode + DNS
echo   6  Blacklist only (egypt-blacklist.txt)
echo.
set "choice=1"
set /p choice=Website profile [1]:
CALL profiles.cmd %choice%
IF NOT DEFINED GDPI_ARGS (
    echo Invalid profile.
    pause
    goto end
)

REM Service binPath needs every inner quote escaped.
set "SVC_ARGS=%GDPI_ARGS:"=\"%"

sc stop "ValSync" >nul 2>&1
sc delete "ValSync" >nul 2>&1
sc create "ValSync" binPath= "\"%CD%\%_arch%\goodbyedpi.exe\" %SVC_ARGS%" start= "auto"
sc description "ValSync" "Bypasses DPI censorship in Egypt (ValSync Nile, profile %choice%)."
sc start "ValSync"
echo.
echo Installed website profile %choice%: %GDPI_NAME%

IF NOT "%_arch%"=="x86_64" goto done
echo.
echo Also install voice chat bypass for games / calls?
echo   0  No
echo   1  Light - Discord voice + STUN calls
echo   2  Full  - adds Valorant/Vivox and all UDP voice
echo.
set "vchoice=0"
set /p vchoice=Voice profile [0]:
sc stop "ValSync-Voice" >nul 2>&1
sc delete "ValSync-Voice" >nul 2>&1
CALL voice_profiles.cmd %vchoice%
IF NOT DEFINED VOICE_ARGS goto done

set "SVC_VARGS=%VOICE_ARGS:"=\"%"
sc create "ValSync-Voice" binPath= "\"%CD%\voice\winws.exe\" %SVC_VARGS%" start= "auto"
sc description "ValSync-Voice" "Bypasses voice chat DPI in Egypt (ValSync Nile, voice profile %vchoice%)."
sc start "ValSync-Voice"
echo.
echo Installed voice profile %vchoice%: %VOICE_NAME%

:done
echo.
echo Do not move or delete this folder while the services are installed.
pause

:end
POPD
ENDLOCAL
