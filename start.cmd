@echo off
set "PDM_BACKEND_DIR=%~dp0"
set "PDM_BUILD_DESKTOP_IN_DOCKER=1"
call "%~dp0scripts\launch-native.cmd" %*
