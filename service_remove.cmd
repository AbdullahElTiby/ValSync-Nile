@ECHO OFF
net session >nul 2>&1
IF ERRORLEVEL 1 (
    echo This script must be run as administrator.
    echo Right click - Run as administrator.
    pause
    goto :eof
)
sc stop "ValSync"
sc delete "ValSync"
sc stop "ValSync-Voice"
sc delete "ValSync-Voice"
sc stop "WinDivert"
sc delete "WinDivert"
sc stop "WinDivert14"
sc delete "WinDivert14"
echo VALSYNC Nile services removed.
pause
