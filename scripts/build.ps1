<#
.SYNOPSIS
  Testa e publica o ClaudemonWin. Requer o .NET 8 SDK (winget install Microsoft.DotNet.SDK.8).
.PARAMETER SelfContained
  Gera um .exe que não precisa do .NET Desktop Runtime instalado (~70 MB em vez de ~1 MB).
#>
param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

dotnet test tests/ClaudemonWin.Core.Tests -c Release
if ($LASTEXITCODE -ne 0) { throw 'Testes falharam.' }

$sc = if ($SelfContained) { 'true' } else { 'false' }
dotnet publish src/ClaudemonWin -c Release -r win-x64 --self-contained $sc `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
if ($LASTEXITCODE -ne 0) { throw 'Publish falhou.' }

Write-Host "`nPronto: $(Resolve-Path dist\ClaudemonWin.exe)" -ForegroundColor Green
