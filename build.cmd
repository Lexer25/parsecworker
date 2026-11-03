@echo off
rem Build -> publish -> installer (.NET 8, self-contained single-file).
rem Run from the repository root or by double click.
setlocal
cd /d "%~dp0"

set "PROJ=.\ParsecIntegrationClient\ParsecIntegrationClient.csproj"
set "VIEWER_PROJ=.\ParsecQueueViewer\ParsecQueueViewer.csproj"
set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

echo ========================================
echo    Parsec Integration Client - build
echo ========================================
echo.

echo [1/5] restore
dotnet restore "%PROJ%"
if errorlevel 1 goto error
dotnet restore "%VIEWER_PROJ%"
if errorlevel 1 goto error
echo.

echo [2/5] build Release (service + queue viewer)
dotnet build "%PROJ%" -c Release --no-restore -v:m
if errorlevel 1 goto error
dotnet build "%VIEWER_PROJ%" -c Release --no-restore -v:m
if errorlevel 1 goto error
echo.

echo [3/5] publish service to .\publish (win-x64, self-contained, single-file)
if exist "publish" rmdir /s /q "publish"
dotnet publish "%PROJ%" -c Release --no-restore -o "publish" -v:m
if errorlevel 1 goto error
if not exist "publish\ParsecIntegrationClient.exe" goto error
if exist "db" xcopy /e /i /y "db" "publish\db" >nul
if errorlevel 1 goto error
echo.

echo [4/5] publish queue viewer to .\publish\QueueViewer
dotnet publish "%VIEWER_PROJ%" -c Release --no-restore -o "publish\QueueViewer" -v:m
if errorlevel 1 goto error
if not exist "publish\QueueViewer\ParsecQueueViewerWpf.exe" goto error
echo.

echo [5/5] installer
if not exist "%ISCC%" goto noiscc
if not exist "ParsecSetup.iss" goto noiss
"%ISCC%" "ParsecSetup.iss"
if errorlevel 1 goto error
goto done

:noiscc
echo [SKIP] Inno Setup not found: %ISCC%
echo        publish\ is ready: copy it to the target machine and run install-service.ps1
goto done

:noiss
echo [SKIP] ParsecSetup.iss not found
goto done

:done
echo.
echo ========================================
echo  DONE
echo  publish\  - service files (ParsecIntegrationClient.exe + appsettings.json)
echo  publish\QueueViewer\  - queue monitor (ParsecQueueViewerWpf.exe)
if exist "Output\*.exe" echo  Output\   - installer
echo ========================================
pause
exit /b 0

:error
echo.
echo [ERROR] build failed
pause
exit /b 1
