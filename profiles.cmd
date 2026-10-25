@ECHO OFF
REM Shared profile definitions for ValSync Nile.
REM Usage: CALL profiles.cmd <number>   -> sets GDPI_ARGS and GDPI_NAME
REM Edit this file to tune a profile; every launcher and the service installer read it.

REM Yandex DNS on a non-standard port: Egyptian ISPs cannot hijack it like port 53.
set "DNS=--dns-addr 77.88.8.8 --dns-port 1253 --dnsv6-addr 2a02:6b8::feed:0ff --dnsv6-port 1253"

set "GDPI_ARGS="
set "GDPI_NAME="
REM Labels instead of IF (...) blocks so folder paths containing ")" don't break parsing.
for %%n in (1 2 3 4 5 6) do if "%~1"=="%%n" goto p%%n
goto :eof

:p1
set "GDPI_NAME=Recommended - fake packets + reverse frag + QUIC block + DNS"
set "GDPI_ARGS=-9 %DNS%"
goto :eof

:p2
set "GDPI_NAME=Fixed TTL 5 fake packets + DNS (Turkey default style)"
set "GDPI_ARGS=-5 --set-ttl 5 %DNS%"
goto :eof

:p3
set "GDPI_NAME=SNI split + extra fake packets + DNS (aggressive)"
set "GDPI_ARGS=-9 --frag-by-sni --fake-gen 5 --fake-resend 2 %DNS%"
goto :eof

:p4
set "GDPI_NAME=Low TTL fake packets, no DNS redirect"
set "GDPI_ARGS=-5 --set-ttl 3 -q"
goto :eof

:p5
set "GDPI_NAME=Legacy compatible mode + DNS (for old routers / VMs)"
set "GDPI_ARGS=-1 -q %DNS%"
goto :eof

:p6
set "GDPI_NAME=Blacklist only - bypass only sites in egypt-blacklist.txt"
set "GDPI_ARGS=-9 --frag-by-sni %DNS% --blacklist "%~dp0egypt-blacklist.txt" --allow-no-sni"
goto :eof
