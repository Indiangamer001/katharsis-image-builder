#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64',
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & $Dotnet publish .\src\Tiny11Builder.Cli\Tiny11Builder.Cli.csproj -c Release -r $Runtime --self-contained true -o .\engine
    if ($LASTEXITCODE -ne 0) { throw 'Build failed. Install the .NET 8 SDK and retry.' }
    Write-Host 'Built engine. Run Start-SelectiveBuild.ps1 from an elevated PowerShell.'
} finally { Pop-Location }
