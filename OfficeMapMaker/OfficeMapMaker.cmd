@echo off
setlocal
set "PYTHONPATH=%~dp0;%PYTHONPATH%"
py -m officemapmaker %*
endlocal
