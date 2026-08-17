@echo off
chcp 65001 >nul
title PoE Auto Buyer - Seamless Workflow Launcher (Trade -> In-Game)
color 0A

:: Kiem tra quyen Administrator
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [!] Vui long chay file nay voi quyen Administrator (Run as Administrator).
    echo Dang tu dong yeu cau quyen Admin...
    powershell -Command "Start-Process '%~0' -Verb RunAs"
    exit /b
)

set PLUGIN_DIR=%~dp0
pushd "%PLUGIN_DIR%..\..\..\"
set EXILE_DIR=%CD%\
popd

pushd "%PLUGIN_DIR%..\..\..\..\autobuypoe\" 2>nul
if %errorlevel% equ 0 (
    set PYTHON_TOOL_DIR=%CD%\
    popd
) else (
    set PYTHON_TOOL_DIR=D:\codecuatien\autobuypoe\
)

cls
echo ===============================================================================
echo            PATH OF EXILE - QUY TRINH TU DONG MUA DO TOAN DIEN
echo                (Web Trade autobuypoe -> ExileApi In-Game)
echo ===============================================================================
echo.
echo  [1] CHAY THEO QUY TRINH CHUAN:  [KHUYEN NGHI - MAC DINH]
echo      - Buoc 1: Dang nhap trang Trade bang autobuypoe
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

echo Lua chon khong hop le!
timeout /t 2 >nul
goto end

:run_workflow
cls
echo ===============================================================================
echo    BUOC 1/2: DANG KHOI DONG TRANG TRADE POE (AUTOBUYPOE)
echo ===============================================================================
echo.
echo [*] Dang mo trinh duyet Chrome Profile cho trang Trade...
cd /d "%PYTHON_TOOL_DIR%"
if exist "launch_chrome.bat" (
    start "" "launch_chrome.bat"
)
start python open_profile.py

echo.
echo [!] Vui long kiem tra cua so trinh duyet Trade vua mo (dang nhap neu can).
echo.
echo Nhan phim bat ky de tiep tuc sang BUOC 2 (Khoi dong Tool In-Game ExileApi)...
pause >nul

cls
echo ===============================================================================
echo    BUOC 2/2: DANG KHOI DONG EXILEAPI - SHOP AUTO BUYER IN-GAME
echo ===============================================================================
echo.
echo [*] Dang kiem tra game Path of Exile...
tasklist /fi "imagename eq PathOfExile.exe" 2>NUL | find /i /n "PathOfExile.exe">NUL
if "%ERRORLEVEL%"=="0" (
    echo  - Da tim thay tien trinh Path of Exile!
) else (
    tasklist /fi "imagename eq PathOfExileSteam.exe" 2>NUL | find /i /n "PathOfExileSteam.exe">NUL
    if "%ERRORLEVEL%"=="0" (
        echo  - Da tim thay tien trinh Path of Exile (Steam)!
    ) else (
        echo  [Luu y] Ban co the mo game Path of Exile truoc hoac ngay sau do.
    )
)

echo.
echo [*] Dang khoi dong ExileApi Loader...
cd /d "%EXILE_DIR%"
start "" "%EXILE_DIR%Loader.exe"

echo.
echo ===============================================================================
echo  [HOAN TAT] DA KHOI DONG THANH CONG TOAN BO QUY TRINH!
echo.
echo  HUONG DAN MUA DO TRONG GAME:
echo    1. Vao game, gap NPC va mo cua so Shop (Faustus, Merchant, Helena...)
echo    2. Quan sat cac vien ngọc Timeless Jewel duoc Highlight kem so Seed
echo    3. Bam phim [F6] tren ban phim de TU DONG MUA ngay vao hom do!
echo ===============================================================================
echo.
pause
exit /b

:run_exile_only
cls
echo Dang khoi dong ExileApi Loader...
cd /d "%EXILE_DIR%"
start "" "%EXILE_DIR%Loader.exe"
echo [OK] ExileApi da duoc khoi chay!
timeout /t 3 >nul
exit /b

:run_python_only
cls
echo Dang khoi dong Web Trade autobuypoe...
cd /d "%PYTHON_TOOL_DIR%"
if exist "launch_chrome.bat" (
    start "" "launch_chrome.bat"
)
python open_profile.py
pause
exit /b

:end
