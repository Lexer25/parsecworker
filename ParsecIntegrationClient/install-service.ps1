#Requires -RunAsAdministrator
<#
    Установка/обновление службы Windows. Заменяет ProjectInstaller + installutil прежней версии.

    Имя службы по умолчанию совпадает с прежним (ParsecIntegrationService), поэтому обновление
    на стенде выполняется «поверх»: скрипт остановит службу, обновит путь к exe и запустит её.

    Использование:
        .\install-service.ps1
        .\install-service.ps1 -ServiceName MyService -DisplayName "My Service"
        .\install-service.ps1 -Account "NT AUTHORITY\NetworkService"
        .\install-service.ps1 -Account "DOMAIN\svc_parsec" -Password "пароль"
#>
param(
    [string]$ServiceName = "ParsecIntegrationService",
    [string]$DisplayName = "ParsecIntegrationService",
    [string]$Description = "Модуль интеграции СКУД Артонит и СКУД Парсек",
    [string]$Account = "NT AUTHORITY\LocalService",
    [string]$Password = ""
)

$ErrorActionPreference = "Stop"

$exe = Join-Path $PSScriptRoot "ParsecIntegrationClient.exe"
if (-not (Test-Path $exe)) {
    throw "Не найден $exe. Сначала выполните dotnet publish и запускайте скрипт из папки поставки."
}

# Кавычки нужны, если путь содержит пробелы.
$binaryPath = '"' + $exe + '"'
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if ($service) {
    Write-Host "Служба $ServiceName уже установлена — обновляю параметры (путь к exe, тип запуска)."

    if ($service.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force
        (Get-Service -Name $ServiceName).WaitForStatus("Stopped", "00:01:00")
    }

    & sc.exe config $ServiceName binPath= $binaryPath start= auto DisplayName= $DisplayName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe config завершился с кодом $LASTEXITCODE"
    }
}
else {
    New-Service -Name $ServiceName -BinaryPathName $binaryPath -DisplayName $DisplayName -StartupType Automatic | Out-Null
}

& sc.exe description $ServiceName $Description | Out-Null

# Перезапуск при падении: три попытки с интервалом 60 секунд, счётчик сбрасывается раз в сутки.
& sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

# Учётная запись: service-аккаунтам (LocalService/NetworkService/LocalSystem) пароль не нужен.
if ($Account) {
    if ($Password) {
        & sc.exe config $ServiceName obj= $Account password= $Password | Out-Null
    }
    else {
        & sc.exe config $ServiceName obj= $Account | Out-Null
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Не удалось назначить учётную запись $Account (код $LASTEXITCODE)"
    }
}

Start-Service -Name $ServiceName
Get-Service -Name $ServiceName | Format-List Name, DisplayName, Status, StartType
Write-Host "Готово. Файл службы: $exe"
Write-Host "Журнал: <Paths:MainPath>\log (по умолчанию C:\IntegrationClient\log)"
