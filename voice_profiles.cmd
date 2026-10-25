@ECHO OFF
REM Voice chat profiles (zapret winws, UDP only). GoodbyeDPI cannot touch UDP, so voice runs
REM as a second process next to it. TCP is left entirely to GoodbyeDPI, so the two never overlap.
REM Usage: CALL voice_profiles.cmd <number>   -> sets VOICE_ARGS and VOICE_NAME

set "WF=%~dp0voice\windivert.filter"

set "VOICE_ARGS="
set "VOICE_NAME="
for %%n in (1 2) do if "%~1"=="%%n" goto v%%n
goto :eof

:v1
REM Only packets recognized as Discord voice or STUN (WhatsApp/Telegram calls, many game voice SDKs).
REM Light: the driver only diverts matching packets, other game traffic is not touched.
set "VOICE_NAME=Light - Discord voice + STUN calls"
set "VOICE_ARGS=--wf-raw-part=@"%WF%\windivert_part.discord_media.txt" --wf-raw-part=@"%WF%\windivert_part.stun.txt" --filter-l7=discord,stun --dpi-desync=fake --dpi-desync-repeats=6"
goto :eof

:v2
REM Adds Vivox (Valorant, League, many other games) and other UDP voice on 12000-65535: fakes are sent only
REM for the first packets of each flow (cutoff n3), then the flow is left alone. Valorant game traffic (7000-8000) is excluded.
set "VOICE_NAME=Full - Light + Valorant/Vivox + all UDP voice 12000-65535 + WireGuard"
set "VOICE_ARGS=--wf-udp=12000-65535 --wf-raw-part=@"%WF%\windivert_part.discord_media.txt" --wf-raw-part=@"%WF%\windivert_part.stun.txt" --wf-raw-part=@"%WF%\windivert_part.wireguard.txt" --filter-l7=discord,stun,wireguard --dpi-desync=fake --dpi-desync-repeats=6 --new --filter-udp=12000-65535 --dpi-desync=fake --dpi-desync-any-protocol=1 --dpi-desync-cutoff=n3 --dpi-desync-repeats=6"
goto :eof
