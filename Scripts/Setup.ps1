<#
.SYNOPSIS
    Sets up the ATM solution for local development.

.DESCRIPTION
    1. Verifies the .NET 10 SDK is installed.
    2. Ensures the ASP.NET Core HTTPS development certificate is trusted.
    3. Restores and builds the solution.
    4. Optionally resets the SQLite database, runs the tests, and starts the API and client.

.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Debug.

.PARAMETER SkipCertificate
    Skip the HTTPS development certificate check/trust step.

.PARAMETER ResetDatabase
    Delete the local SQLite database (atm.db) so it is recreated and reseeded on the next API start.

.PARAMETER RunTests
    Run all tests after building.

.PARAMETER Coverage
    Collect code coverage when running tests (implies -RunTests).

.PARAMETER Run
    Start the API and the Blazor client in separate windows after setup.

.EXAMPLE
    .\Scripts\Setup.ps1

.EXAMPLE
    .\Scripts\Setup.ps1 -RunTests -Run

.EXAMPLE
    .\Scripts\Setup.ps1 -ResetDatabase -Run
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipCertificate,
    [switch]$ResetDatabase,
    [switch]$RunTests,
    [switch]$Coverage,
    [switch]$Run
)

$ErrorActionPreference = 'Stop'

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$Solution   = Join-Path $RepoRoot 'ATM.slnx'
$ApiProject = Join-Path $RepoRoot 'src\ATM.Api'
$ClientProj = Join-Path $RepoRoot 'src\ATM.Client'
$RunSettings = Join-Path $RepoRoot 'coverage.runsettings'

function Write-Step([string]$Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Ok([string]$Message) {
    Write-Host "    $Message" -ForegroundColor Green
}

function Invoke-Dotnet {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'dotnet $($Arguments -join ' ')' failed with exit code $LASTEXITCODE."
    }
}

# 1. .NET SDK -----------------------------------------------------------------
Write-Step 'Checking .NET SDK'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK not found. Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0'
}

$sdks = & dotnet --list-sdks
$net10 = $sdks | Where-Object { $_ -match '^10\.' }
if (-not $net10) {
    throw ".NET 10 SDK not found. Installed SDKs:`n$($sdks -join "`n")`nInstall it from https://dotnet.microsoft.com/download/dotnet/10.0"
}
Write-Ok "Found: $(($net10 | Select-Object -Last 1).Split(' ')[0])"

# 2. HTTPS dev certificate ----------------------------------------------------
if ($SkipCertificate) {
    Write-Step 'Skipping HTTPS development certificate'
}
else {
    Write-Step 'Checking HTTPS development certificate'
    & dotnet dev-certs https --check --trust *> $null
    if ($LASTEXITCODE -eq 0) {
        Write-Ok 'Certificate is already trusted.'
    }
    else {
        Write-Host '    Trusting certificate (accept the confirmation dialog if prompted)...'
        Invoke-Dotnet dev-certs https --trust
        Write-Ok 'Certificate trusted.'
    }
}

# 3. Restore & build ----------------------------------------------------------
Write-Step 'Restoring NuGet packages'
Invoke-Dotnet restore $Solution

Write-Step "Building solution ($Configuration)"
Invoke-Dotnet build $Solution --configuration $Configuration --no-restore
Write-Ok 'Build succeeded.'

# 4. Reset database (optional) ------------------------------------------------
if ($ResetDatabase) {
    Write-Step 'Resetting SQLite database'
    $dbFiles = foreach ($dir in @($ApiProject, $RepoRoot)) {
        Get-ChildItem -Path $dir -Filter 'atm.db*' -File -ErrorAction SilentlyContinue
    }
    if ($dbFiles) {
        $dbFiles | ForEach-Object {
            Remove-Item $_.FullName -Force
            Write-Ok "Deleted $($_.FullName)"
        }
    }
    else {
        Write-Ok 'No database found; nothing to delete.'
    }
}

# 5. Tests (optional) ---------------------------------------------------------
if ($RunTests -or $Coverage) {
    Write-Step 'Running tests'
    $testArgs = @('test', $Solution, '--configuration', $Configuration, '--no-build')
    if ($Coverage) {
        $testArgs += '--collect:XPlat Code Coverage'
        if (Test-Path $RunSettings) {
            $testArgs += @('--settings', $RunSettings)
        }
        else {
            Write-Warning "coverage.runsettings not found; collecting coverage with default settings."
        }
    }
    Invoke-Dotnet @testArgs
    Write-Ok 'All tests passed.'
}

# 6. Run (optional) -----------------------------------------------------------
if ($Run) {
    Write-Step 'Starting API and client'

    $shell = if (Get-Command pwsh -ErrorAction SilentlyContinue) { 'pwsh' } else { 'powershell' }

    Start-Process $shell -WorkingDirectory $ApiProject -ArgumentList @(
        '-NoExit', '-Command',
        "`$Host.UI.RawUI.WindowTitle = 'ATM API'; dotnet run --configuration $Configuration --launch-profile https"
    )

    Start-Process $shell -WorkingDirectory $ClientProj -ArgumentList @(
        '-NoExit', '-Command',
        "`$Host.UI.RawUI.WindowTitle = 'ATM Client'; dotnet run --configuration $Configuration --launch-profile https"
    )

    Write-Ok 'API:     https://localhost:7102  (Swagger: https://localhost:7102/swagger)'
    Write-Ok 'Client:  https://localhost:7101'
    Write-Host '    Close the two new windows (or press Ctrl+C in them) to stop.'
}

Write-Host ''
Write-Host 'Setup complete.' -ForegroundColor Green
if (-not $Run) {
    Write-Host 'Start the apps with:  .\Scripts\Setup.ps1 -Run   (or see README.md)'
}
