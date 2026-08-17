@echo off
chcp 65001 >nul
title PoE Auto Buyer - Seamless Workflow (Trade Web -> In-Game Auto Buy)
color 0A

cd /d "%~dp0"

set "PLUGIN_DIR=%~dp0"
set "EXILE_DIR=%~dp0..\..\..\"
set "PYTHON_TOOL_DIR=%~dp0..\..\..\..\autobuypoe\"

cls
echo ===============================================================================
echo            QUY TRINH TU DONG SAN DO POE (TRADE WEB -^> IN-GAME)
echo ===============================================================================
echo.
echo  Quy trinh se thuc hien:
echo    [1] Khoi dong Trade Web autobuypoe (tim kiem do tren pathofexile.com)
echo    [2] Khi click "Travel to Hideout", tu dong kich hoat ExileApi de vao game mua!
echo.
echo ===============================================================================
echo.
echo [*] Dang khoi dong Web Trade Tool (autobuypoe)...
if exist "%PYTHON_TOOL_DIR%" (
    cd /d "%PYTHON_TOOL_DIR%"
    py open_profile.py
) else (
    echo [Loi] Khong tim thay thu muc autobuypoe tai: %PYTHON_TOOL_DIR%
    pause
)

pause
