@echo off
rem Build -> publish -> installer. Run from the repository root or by double click.
setlocal
cd /d "%~dp0"

set "PROJ=.\ParsecIntegrationClient\ParsecIntegrationClient.csproj"
set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

echo ========================================
echo    Parsec Integration Client - build
echo ========================================
echo.

echo [1/4] restore
dotnet restore "%PROJ%"
if errorlevel 1 goto error
echo.

echo [2/4] build Release
dotnet build "%PROJ%" -c Release --no-restore -v:m
if errorlevel 1 goto error
echo.

echo [3/4] publish to .\publish
if exist "publish" rmdir /s /q "publish"
dotnet publish "%PROJ%" -c Release --no-restore -o "publish" -v:m
if errorlevel 1 goto error
if not exist "publish\ParsecIntegrationClient.exe" goto error
echo.

echo [4/4] installer
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
echo  publish\  - service files
if exist "Output\*.exe" echo  Output\   - installer
echo ========================================
pause
exit /b 0

:error
echo.
echo [ERROR] build failed
pause
exit /b 1
