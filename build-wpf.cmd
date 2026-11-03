@echo off
rem WPF queue viewer: self-contained single-file exe.
rem .NET 8, WPF and the Firebird provider are packed inside; nothing else is needed.
rem Result: Output\ParsecQueueViewerWpf_v1.1.2_single\ParsecQueueViewerWpf.exe
setlocal
cd /d "%~dp0"

set "PROJ=.\ParsecQueueViewer\ParsecQueueViewer.csproj"
set "STAGE=.\ParsecQueueViewer\bin\Release\net8.0-windows\win-x64\publish"
set "PORTABLE=.\Output\ParsecQueueViewerWpf_v1.1.2_single"

echo ========================================
echo    Parsec Queue Viewer WPF - single exe
echo ========================================
echo.

echo [1/3] restore
dotnet restore "%PROJ%"
if errorlevel 1 goto error
echo.

echo [2/3] publish (win-x64, self-contained, single-file)
dotnet publish "%PROJ%" -c Release --no-restore -v:m
if errorlevel 1 goto error
if not exist "%STAGE%\ParsecQueueViewerWpf.exe" goto error
echo.

echo [3/3] package: one file
if exist "%PORTABLE%" rmdir /s /q "%PORTABLE%"
mkdir "%PORTABLE%"
copy /y "%STAGE%\ParsecQueueViewerWpf.exe" "%PORTABLE%\" >nul
if errorlevel 1 goto error
echo.

echo ========================================
echo  DONE
echo  %PORTABLE%\ParsecQueueViewerWpf.exe
echo  One file: copy it to another Windows x64 computer and run.
echo  .NET 8 Desktop Runtime and fbclient.dll are not required.
echo ========================================
pause
exit /b 0

:error
echo.
echo [ERROR] build failed
pause
exit /b 1
