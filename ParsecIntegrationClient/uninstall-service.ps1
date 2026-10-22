#Requires -RunAsAdministrator
<#
    Удаление службы Windows.

    Использование:
        .\uninstall-service.ps1
        .\uninstall-service.ps1 -ServiceName ParsecIntegrationService
#>
param(
    [string]$ServiceName = "ParsecIntegrationService"
)

$ErrorActionPreference = "Stop"

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Host "Служба $ServiceName не установлена."
    return
}

if ($service.Status -ne "Stopped") {
    Stop-Service -Name $ServiceName -Force
    (Get-Service -Name $ServiceName).WaitForStatus("Stopped", "00:01:00")
}

& sc.exe delete $ServiceName | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "sc.exe delete завершился с кодом $LASTEXITCODE"
}

Write-Host "Служба $ServiceName удалена. Каталог данных и журналы не удалялись."
