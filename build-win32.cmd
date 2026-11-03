@echo off
rem Win32 queue viewer: build + single-exe package.
rem Managed dependencies are embedded into the EXE.
rem Only native fbclient.dll stays outside.
setlocal
cd /d "%~dp0"

set "PROJ=.\ParsecQueueViewer.Win32\ParsecQueueViewer.Win32.csproj"
set "OUT=.\ParsecQueueViewer.Win32\bin\Release\net48"
set "PORTABLE=.\Output\ParsecQueueViewerWin32_v1.1.0_single"
set "ZIP=.\Output\ParsecQueueViewerWin32_v1.1.0_single.zip"

echo ========================================
echo    Parsec Queue Viewer Win32 - single exe
echo ========================================
echo.

echo [1/4] restore
dotnet restore "%PROJ%"
if errorlevel 1 goto error
echo.

echo [2/4] build Release
dotnet build "%PROJ%" -c Release --no-restore -v:m
if errorlevel 1 goto error
if not exist "%OUT%\ParsecQueueViewerWin32.exe" goto error
if not exist "%OUT%\fbclient.dll" goto nofbclient
echo.

echo [3/4] package: exe + fbclient.dll
if exist "%PORTABLE%" rmdir /s /q "%PORTABLE%"
mkdir "%PORTABLE%"
copy /y "%OUT%\ParsecQueueViewerWin32.exe" "%PORTABLE%\" >nul
copy /y "%OUT%\fbclient.dll" "%PORTABLE%\" >nul
copy /y ".\ParsecQueueViewer.Win32\README.md" "%PORTABLE%\README.md" >nul
copy /y ".\ParsecQueueViewer.Win32\preview.png" "%PORTABLE%\preview.png" >nul
if errorlevel 1 goto error
echo.

echo [4/4] ZIP
if exist "%ZIP%" del /f /q "%ZIP%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%PORTABLE%\*' -DestinationPath '%ZIP%' -CompressionLevel Optimal"
if errorlevel 1 goto error
if not exist "%ZIP%" goto error

echo.
echo ========================================
echo  DONE
echo  %PORTABLE%\ParsecQueueViewerWin32.exe  - single managed exe
echo  %PORTABLE%\fbclient.dll                - only external DLL
echo  %ZIP%
echo ========================================
pause
exit /b 0

:nofbclient
echo.
echo [ERROR] fbclient.dll not found in build output.
echo         Install Firebird client or pass /p:FbClientSource=...
goto error

:error
echo.
echo [ERROR] build failed
pause
exit /b 1
