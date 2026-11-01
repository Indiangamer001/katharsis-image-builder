#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$SourcePath,
    [string]$ProfilePath,
    [string]$WorkspacePath,
    [string]$OutputPath,
    [string]$OscdimgPath,
    [int]$ImageIndex = 0,
    [switch]$PlanOnly,
    [switch]$NonInteractive,
    [switch]$Build,
    [switch]$AcceptExperimentalRemoval
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$engine = Join-Path $PSScriptRoot 'engine\Tiny11Selector.exe'
if (-not (Test-Path -LiteralPath $engine -PathType Leaf)) {
    throw 'The bundled engine is missing. Extract the complete release, or run Build-Selector.ps1 from a source checkout.'
}
if (-not [Environment]::Is64BitProcess) { throw 'Run 64-bit Windows PowerShell or PowerShell.' }
if ($Build -and $PlanOnly) { throw 'Choose either -Build or -PlanOnly.' }

function Invoke-Engine {
    param([string[]]$Arguments)
    # In Windows PowerShell, native stderr is converted into ErrorRecords under
    # redirection. Temporarily use Continue; the native exit code is authoritative.
    $prior = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $lines = & $engine @Arguments
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $prior }
    if ($code -ne 0) { throw "Image engine failed (exit $code). See the error and workspace logs above." }
    return (($lines -join [Environment]::NewLine) | ConvertFrom-Json)
}

function Read-Choice {
    param([string]$Title)
    Write-Host "`n$Title : [K] Keep source state, [D] Disable, [R] Remove payloads (experimental)"
    while ($true) {
        switch ((Read-Host 'Choice [K]').Trim().ToUpperInvariant()) {
            '' { return 'Keep' }; 'K' { return 'Keep' }; 'D' { return 'Disable' }; 'R' { return 'Remove' }
            default { Write-Host 'Enter K, D, or R.' }
        }
    }
}

function Select-Entries {
    param([object[]]$Items, [string]$Title)
    if ($Items.Count -eq 0) { return }
    Write-Host "`n$Title" -ForegroundColor Cyan
    for ($i = 0; $i -lt $Items.Count; $i++) {
        Write-Host ('{0,4}. {1} [{2}]' -f ($i + 1), $Items[$i].Identity, $Items[$i].Detail)
    }
    Write-Host 'Enter comma-separated row numbers. Blank keeps every listed item. No items are selected by default.'
    while ($true) {
        $answer = (Read-Host 'Rows').Trim()
        if (-not $answer) { return }
        $indices = @(); $valid = $true
        foreach ($part in $answer.Split(',')) {
            $number = 0
            if (-not [int]::TryParse($part.Trim(), [ref]$number) -or $number -lt 1 -or $number -gt $Items.Count) {
                $valid = $false; break
            }
            $indices += $number - 1
        }
        if ($valid) { foreach ($index in ($indices | Select-Object -Unique)) { $Items[$index] }; return }
        Write-Host 'Use row numbers from the displayed list, separated by commas.'
    }
}

function Save-Json {
    param([object]$Value, [string]$Path)
    $Value | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Confirm-Removal {
    if ($profile.Defender -ne 'Remove' -and $profile.WindowsUpdate -ne 'Remove') { return }
    Write-Warning 'REMOVE deletes identified offline services, deployed files and component-store payloads. This is not a supported package uninstall. Boot, repairs, updates or apps can fail. Metadata may remain; upgrades can restore files. Test in a VM.'
    if (-not $AcceptExperimentalRemoval) {
        if ($NonInteractive) { throw 'Experimental removal requires -AcceptExperimentalRemoval.' }
        if ((Read-Host 'Type REMOVE to accept physical removal, or anything else to stop') -cne 'REMOVE') { throw 'Stopped without building.' }
    }
    $profile.AcceptExperimentalRemoval = $true
}

$tools = Invoke-Engine -Arguments @('tools')
if (-not $tools.Administrator) { throw 'Open PowerShell as Administrator, then rerun this script. It only services offline working copies.' }
$loaded = -not [string]::IsNullOrWhiteSpace($ProfilePath)
if ($loaded) {
    $ProfilePath = (Resolve-Path -LiteralPath $ProfilePath).Path
    # Let the engine reject unknown modes and malformed JSON before inspecting media.
    $profile = Invoke-Engine -Arguments @('read', $ProfilePath)
    Confirm-Removal
} else {
    if ($NonInteractive) { throw '-NonInteractive requires an existing -ProfilePath.' }
    $profile = [pscustomobject]@{
        Version = 1; Defender = 'Keep'; WindowsUpdate = 'Keep'; AcceptExperimentalRemoval = $false
        Recipe = [pscustomobject]@{
            SchemaVersion = 1; SourcePath = ''; ImageIndex = 1; WorkspacePath = ''; OutputPath = ''
            DismPath = ''; OscdimgPath = ''; Mode = 'Custom'; Flavor = 'Regular'
            ExpertAcknowledged = $false; ExpertActions = @(); SelectedOptions = @(); UpdatePackages = @()
            Compression = 'Recovery'; EnableNetFx3 = $false; CompactInstallation = $true
            PreferLocalAccount = $false; KeepWorkingFiles = $false; TurboMode = $false
        }
    }
}
if ($SourcePath) { $profile.Recipe.SourcePath = $SourcePath }
if (-not $profile.Recipe.SourcePath) {
    if ($NonInteractive) { throw 'Set SourcePath in the profile or supply -SourcePath.' }
    $profile.Recipe.SourcePath = (Read-Host 'Full path to official Windows 11 ISO (or extracted media folder)').Trim('"')
}
$profile.Recipe.SourcePath = (Resolve-Path -LiteralPath $profile.Recipe.SourcePath).Path
if ($WorkspacePath) { $profile.Recipe.WorkspacePath = $WorkspacePath }
if (-not $profile.Recipe.WorkspacePath) { $profile.Recipe.WorkspacePath = Join-Path $PSScriptRoot 'workspace' }
if ($OutputPath) { $profile.Recipe.OutputPath = $OutputPath }
if (-not $profile.Recipe.OutputPath) {
    $profile.Recipe.OutputPath = Join-Path $profile.Recipe.WorkspacePath ('Windows11-Pro-Custom-{0}.iso' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$profile.Recipe.DismPath = $tools.DismPath
if ($OscdimgPath) { $profile.Recipe.OscdimgPath = (Resolve-Path -LiteralPath $OscdimgPath).Path }
if (-not $profile.Recipe.OscdimgPath) { $profile.Recipe.OscdimgPath = $tools.OscdimgPath }

$images = @(Invoke-Engine -Arguments @('inspect', $profile.Recipe.SourcePath))
if ($ImageIndex -gt 0) { $profile.Recipe.ImageIndex = $ImageIndex }
elseif (-not $loaded) {
    $pro = @($images | Where-Object { $_.Edition -eq 'Professional' })
    if ($pro.Count -gt 0) { $profile.Recipe.ImageIndex = $pro[0].Index }
    else {
        $images | Format-Table Index, Name, Architecture
        $profile.Recipe.ImageIndex = [int](Read-Host 'Windows 11 Pro not found. Enter the edition index to use')
    }
}
$chosen = @($images | Where-Object { $_.Index -eq $profile.Recipe.ImageIndex })
if ($chosen.Count -ne 1) { throw 'The selected edition index is not in this image.' }
Write-Host "`nSelected edition: $($chosen[0].Name) / $($chosen[0].Architecture)" -ForegroundColor Green

# Preview/profile files belong to a fresh local session. The engine separately
# validates the workspace before creating mounts or staging any Windows media.
$session = Join-Path $PSScriptRoot ('sessions\' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $session -Force
$savedProfile = Join-Path $session 'profile.json'
$inventoryFile = Join-Path $session 'inventory.json'
$planFile = Join-Path $session 'plan.json'
Save-Json $profile $savedProfile
Write-Host 'Reading the selected image inventory. This can take several minutes.'
$inventory = @(Invoke-Engine -Arguments @('inventory', $savedProfile))
Save-Json $inventory $inventoryFile

if (-not $loaded) {
    Write-Host 'KEEP means preserve what is present in the source. It does not add missing apps.'
    Write-Host 'Frameworks and shared app dependencies are listed too. Removing them can break retained applications.'
    $actions = @()
    $apps = @($inventory | Where-Object { $_.Kind -eq 'ProvisionedApp' -and $_.Identity -notmatch 'Defender|SecHealth|SecurityHealth' })
    foreach ($item in @(Select-Entries $apps 'Microsoft and other provisioned apps to REMOVE')) {
        $actions += [pscustomobject]@{ Kind = $item.Kind; Identity = $item.Identity; Mode = 'Remove' }
    }
    $features = @($inventory | Where-Object { $_.Kind -eq 'Feature' -and $_.Identity -notmatch 'Defender|WindowsUpdate|UpdateOrchestrator|WaaSMedic' })
    $disabled = @(Select-Entries $features 'Optional features to DISABLE (retain payload)')
    foreach ($item in $disabled) { $actions += [pscustomobject]@{ Kind = $item.Kind; Identity = $item.Identity; Mode = 'Disable' } }
    $remaining = @($features | Where-Object { $_.Identity -notin @($disabled | ForEach-Object { $_.Identity }) })
    foreach ($item in @(Select-Entries $remaining 'Optional features to REMOVE (request payload removal)')) {
        $actions += [pscustomobject]@{ Kind = $item.Kind; Identity = $item.Identity; Mode = 'Remove' }
    }
    $capabilities = @($inventory | Where-Object { $_.Kind -eq 'Capability' -and $_.Detail -eq 'Installed' -and $_.Identity -notmatch 'Defender|WindowsUpdate|UpdateOrchestrator|WaaSMedic' })
    foreach ($item in @(Select-Entries $capabilities 'Installed capabilities (languages, accessories) to REMOVE')) {
        $actions += [pscustomobject]@{ Kind = $item.Kind; Identity = $item.Identity; Mode = 'Remove' }
    }
    $profile.Recipe.ExpertActions = @($actions)
    $profile.Recipe.ExpertAcknowledged = $actions.Count -gt 0
    $catalog = @(Invoke-Engine -Arguments @('catalog'))
    $extras = @($catalog | Where-Object { $_.Kind -eq 'Policy' -or $_.Id -in @('edge-files', 'onedrive-files') } |
        ForEach-Object { [pscustomobject]@{ Identity = $_.Id; Detail = $_.Title + ': ' + $_.Description } })
    $profile.Recipe.SelectedOptions = @(Select-Entries $extras 'Additional removals and settings to APPLY' | ForEach-Object { $_.Identity })
    $profile.Defender = Read-Choice 'Microsoft Defender Antivirus'
    $profile.WindowsUpdate = Read-Choice 'Windows Update'
    Write-Host 'Defender choices apply to Antivirus, not the firewall or enterprise Defender for Endpoint agent.'
}

if (-not $loaded) { Confirm-Removal }
Save-Json $profile $savedProfile
$plan = Invoke-Engine -Arguments @('plan', $savedProfile, $inventoryFile)
Save-Json $plan $planFile
Write-Host "`nDefender: $($profile.Defender) | Windows Update: $($profile.WindowsUpdate)"
Write-Host "Apps/features/services/payload actions: $(@($plan.ExpertActions).Count)"
$plan.ExpertActions | Format-Table Kind, Mode, Identity -AutoSize -Wrap
Write-Host "Saved reusable profile: $savedProfile"
Write-Host "Exact build plan: $planFile"
Write-Host "Output ISO: $($profile.Recipe.OutputPath)"
if ($PlanOnly -or ($NonInteractive -and -not $Build)) { return }
if (-not $Build -and (Read-Host 'Type BUILD to create this ISO; anything else keeps the profile only') -cne 'BUILD') { return }
$result = Invoke-Engine -Arguments @('build', $savedProfile)
Save-Json $result (Join-Path $session 'result.json')
Write-Host "`nISO created: $($result.IsoPath)" -ForegroundColor Green
Write-Host "SHA256: $($result.Sha256)"
Write-Host "Build report: $($result.ReportPath)"
foreach ($warning in $result.Warnings) { Write-Warning $warning }
