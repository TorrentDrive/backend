#
# Deprecated. Kept so existing instructions keep working.
#
# run.ps1 used to ask you to fill in a .env file before starting. That step is
# gone: the container generates its own secrets on first boot and everything
# else is configured in the browser. install.ps1 does the whole job.
#
# This shim will be removed in a future release. Use install.ps1 directly:
#
#   irm https://raw.githubusercontent.com/TorrentDrive/backend/main/install.ps1 | iex
#
$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "  ! run.ps1 is deprecated - running install.ps1 instead." -ForegroundColor Yellow
Write-Host "    No .env file is needed any more. See https://tc.gitnasr.com/docs" -ForegroundColor DarkGray
Write-Host ""

$installer = Join-Path $PSScriptRoot "install.ps1"

if (Test-Path $installer) {
    & $installer @args
} else {
    Write-Host "  install.ps1 is not next to this script; fetching it."
    Invoke-RestMethod https://raw.githubusercontent.com/TorrentDrive/backend/main/install.ps1 | Invoke-Expression
}
