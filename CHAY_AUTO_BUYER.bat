@echo off
chcp 65001 >nul
title PoE Auto Buyer - Workflow Launcher
cd /d "%~dp0"

set "PLUGIN_DIR=%~dp0"
set "EXILE_DIR=%~dp0..\..\..\"
set "PYTHON_TOOL_DIR=%~dp0..\..\..\..\autobuypoe\"

cls
echo ===============================================================================
echo            PATH OF EXILE - QUY TRINH TU DONG MUA DO TOAN DIEN
echo                (Web Trade autobuypoe -^> ExileApi In-Game)
echo ===============================================================================
echo.
echo  [1] CHAY THEO QUY TRINH CHUAN:  [KHUYEN NGHI - MAC DINH]
echo      - Buoc 1: Mo Web Trade bang autobuypoe
echo      - Buoc 2: Khoi dong ExileApi Shop Auto Buyer de mua do trong game
echo.
echo  [2] Chi chay Tool In-Game (ExileApi Loader)
echo  [3] Chi chay Web Trade Tool (autobuypoe Python)
echo  [0] Thoat
echo.
echo ===============================================================================
set /p choice="Nhap lua chon cua ban (1, 2, 3 hoac 0) [Mac dinh: 1]: "
if "%choice%"=="" set choice=1

if "%choice%"=="1" goto run_workflow
if "%choice%"=="2" goto run_exile_only
if "%choice%"=="3" goto run_python_only
if "%choice%"=="0" exit /b
goto run_workflow

:run_workflow
cls
echo ===============================================================================
echo    BUOC 1/2: DANG KHOI DONG TRANG TRADE POE (AUTOBUYPOE)
echo ===============================================================================
echo.
if exist "%PYTHON_TOOL_DIR%" (
    cd /d "%PYTHON_TOOL_DIR%"
    if exist "launch_chrome.bat" (
        start "" "launch_chrome.bat"
    )
    start python open_profile.py
    echo [*] Da mo trinh duyet Chrome Profile Trade.
) else (
    echo [Luu y] Khong tim thay thu muc autobuypoe tai: %PYTHON_TOOL_DIR%
)

echo.
echo [!] Kiem tra trinh duyet Trade da mo xong.
echo Bam phim bat ky de tiep tuc sang BUOC 2 (Mo Tool In-Game ExileApi)...
pause >nul

cls
echo ===============================================================================
echo    BUOC 2/2: DANG KHOI DONG EXILEAPI - SHOP AUTO BUYER IN-GAME
echo ===============================================================================
echo.
cd /d "%EXILE_DIR%"
if exist "Loader.exe" (
    start "" "Loader.exe"
    echo [OK] Da mo ExileApi Loader.exe thanh cong!
) else (
    echo [Loi] Khong tim thay Loader.exe tai: %EXILE_DIR%
)

echo.
echo ===============================================================================
echo  HUONG DAN MUA DO TRONG GAME:
echo    1. Vao game, gap NPC va mo cua so Shop (Faustus, Merchant...)
echo    2. Cac vien Timeless Jewel duoc Highlight xanh kem so Seed
echo    3. Bam phim [F6] tren ban phim de TU DONG MUA!
echo ===============================================================================
echo.
pause
exit /b

:run_exile_only
cls
cd /d "%EXILE_DIR%"
if exist "Loader.exe" (
    start "" "Loader.exe"
    echo [OK] Da mo ExileApi Loader.exe!
) else (
    echo [Loi] Khong tim thay Loader.exe tai: %EXILE_DIR%
)
timeout /t 3 >nul
exit /b

:run_python_only
cls
cd /d "%PYTHON_TOOL_DIR%"
if exist "launch_chrome.bat" (
    start "" "launch_chrome.bat"
)
python open_profile.py
pause
exit /b
