#Requires -Version 5.1
<#
    Перегенерация SOAP-клиента Парсека из WSDL.

    В отличие от прежнего Web Reference (System.Web.Services, только .NET Framework),
    клиент генерируется dotnet-svcutil в WCF-виде (System.ServiceModel) и работает в .NET 8.
    Синхронные методы (--syncOnly) сохранены, потому что весь код ParsecService вызывает
    операции синхронно.

    Что делает скрипт:
      1. при первом запуске ставит dotnet-svcutil 8.0.0 в локальный каталог .tools
         (он в .gitignore, в репозиторий не попадает);
      2. генерирует ParsecIntegrationClient\IntegrationWebService\IntegrationService.cs;
      3. заменяет старый файл только после успешной генерации.

    Запуск (из любого каталога):
        .\tools\regenerate-parsec-client.ps1

    При первом запуске нужен доступ к nuget.org.
    После перегенерации: dotnet build и проверка обмена с Парсеком на стенде.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $root 'ParsecIntegrationClient'
$outDir = Join-Path $projectDir 'IntegrationWebService'
$outFile = Join-Path $outDir 'IntegrationService.cs'
$wsdl = Join-Path $outDir 'IntegrationService.wsdl'

if (-not (Test-Path $wsdl)) {
    throw "Не найден WSDL: $wsdl"
}

$toolDir = Join-Path $root '.tools'
$tool = Join-Path $toolDir 'dotnet-svcutil.exe'
$cliHome = Join-Path $toolDir 'dotnet-home'
$tempPath = Join-Path $toolDir 'tmp'
New-Item -ItemType Directory -Force $cliHome, $tempPath | Out-Null

# svcutil и dotnet CLI пишут служебные файлы в профиль/TEMP; держим их внутри .tools,
# чтобы скрипт работал и под ограниченной учётной записью, и в песочнице.
$env:DOTNET_CLI_HOME = $cliHome
$env:TMP = $tempPath
$env:TEMP = $tempPath
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SVCUTIL_TELEMETRY_OPTOUT = '1'

# Первый запуск dotnet CLI в чистом DOTNET_CLI_HOME создаёт служебные файлы и может
# завершиться предупреждением о рабочих нагрузках. Для svcutil это выглядит как ошибка,
# поэтому прогреваем CLI заранее и не считаем код возврата критичным.
try {
    & dotnet --info *> $null
}
catch {
    # Ошибки прогрева не важны: следующий запуск dotnet уже работает со своим состоянием.
}

if (-not (Test-Path $tool)) {
    Write-Host '=== dotnet tool install dotnet-svcutil ==='
    & dotnet tool install dotnet-svcutil --tool-path $toolDir --version 8.0.0
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet tool install завершился с кодом $LASTEXITCODE"
    }
}

# svcutil отказывается перезаписывать файл, поэтому генерация идёт во временный каталог,
# а готовый файл копируется на место только после успеха.
$tempOut = Join-Path $toolDir 'svcutil-out'
if (Test-Path $tempOut) {
    Remove-Item $tempOut -Recurse -Force
}
New-Item -ItemType Directory -Force $tempOut | Out-Null

try {
    $generated = Join-Path $tempOut 'IntegrationService.cs'
    $succeeded = $false

    # Первый запуск svcutil в чистом DOTNET_CLI_HOME может упасть на инициализации
    # dotnet CLI (предупреждение о рабочих нагрузках). Второй запуск уже работает;
    # настоящая ошибка генерации проявится и на повторе.
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        Write-Host "=== dotnet-svcutil (попытка $attempt) ==="

        if (Test-Path $generated) {
            Remove-Item $generated -Force
        }

        & $tool $wsdl `
            --outputDir $tempOut `
            --outputFile 'IntegrationService.cs' `
            --namespace '*,ParsecIntegrationClient.IntegrationWebService' `
            --serializer XmlSerializer `
            --syncOnly `
            --noLogo

        if ($LASTEXITCODE -eq 0 -and (Test-Path $generated)) {
            $succeeded = $true
            break
        }

        if ($attempt -lt 2) {
            Write-Host 'Повтор после инициализации dotnet CLI...'
            Start-Sleep -Seconds 1
        }
    }

    if (-not $succeeded) {
        throw "dotnet-svcutil не создал $generated (код $LASTEXITCODE)"
    }

    Copy-Item $generated $outFile -Force
}
finally {
    if (Test-Path $tempOut) {
        Remove-Item $tempOut -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Готово: $outFile"
Write-Host 'Проверьте git diff и соберите проект: dotnet build .\ParsecIntegrationClient\ParsecIntegrationClient.csproj'
