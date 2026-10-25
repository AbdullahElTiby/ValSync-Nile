@ECHO OFF
REM One-click start with the recommended profile (1).
SETLOCAL
PUSHD "%~dp0"
set _arch=x86
IF "%PROCESSOR_ARCHITECTURE%"=="AMD64" (set _arch=x86_64)
IF DEFINED PROCESSOR_ARCHITEW6432 (set _arch=x86_64)

CALL profiles.cmd 1
start "VALSYNC Nile" "%_arch%\goodbyedpi.exe" %GDPI_ARGS%

POPD
ENDLOCAL
