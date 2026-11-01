#requires -Version 5.1
param([string]$Engine = (Join-Path $PSScriptRoot '..\engine\Tiny11Selector.exe'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $root ('test-results\selector-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $scratch -Force
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'Start-SelectiveBuild.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
# Run the actual wrapper function, including its native argv and JSON handling,
# without running the interactive script or any Windows servicing operation.
$function = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-Engine' }, $true)
. ([scriptblock]::Create($function.Extent.Text))
$profileFile = Join-Path $scratch 'profile with spaces.json'
$inventoryFile = Join-Path $scratch 'inventory with spaces.json'
@{ Version = 1; Defender = 'Keep'; WindowsUpdate = 'Keep'; Recipe = @{ Mode = 'Custom' } } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $profileFile -Encoding UTF8
'[]' | Set-Content -LiteralPath $inventoryFile -Encoding UTF8
$plan = Invoke-Engine -Arguments @('plan', $profileFile, $inventoryFile)
if ($plan.Mode -ne 'Custom' -or @($plan.ExpertActions).Count -ne 0) { throw 'Unexpected Keep plan.' }
$catalog = @(Invoke-Engine -Arguments @('catalog'))
if ($catalog.Count -lt 10 -or 'onedrive-files' -notin $catalog.Id) { throw 'Catalog is incomplete.' }
$json = Get-Content -LiteralPath $profileFile -Raw
$json.Replace('"Defender"', '"Defendr"') | Set-Content -LiteralPath $profileFile -Encoding UTF8
$rejected = $false
try { $null = Invoke-Engine -Arguments @('validate', $profileFile) } catch { $rejected = $true }
if (-not $rejected) { throw 'Malformed profile was accepted.' }
foreach ($name in @('Select-Entries', 'Read-Choice')) {
    $function = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
    . ([scriptblock]::Create($function.Extent.Text))
}
$script:answers = New-Object 'System.Collections.Generic.Queue[string]'
function Read-Host { param([string]$Prompt) return $script:answers.Dequeue() }
$items = @([pscustomobject]@{Identity='Calculator';Detail='Installed'}, [pscustomobject]@{Identity='Paint';Detail='Installed'})
$script:answers.Enqueue('')
if (@(Select-Entries $items 'Blank selection test').Count -ne 0) { throw 'Blank unexpectedly selected an item.' }
$script:answers.Enqueue('999'); $script:answers.Enqueue('2,2')
$selected = @(Select-Entries $items 'Range/duplicate test')
if ($selected.Count -ne 1 -or $selected[0].Identity -ne 'Paint') { throw 'Selection did not validate/deduplicate.' }
$script:answers.Enqueue(''); if ((Read-Choice 'Default mode test') -ne 'Keep') { throw 'Unsafe default.' }
$script:answers.Enqueue('R'); if ((Read-Choice 'Remove mode test') -ne 'Remove') { throw 'Wrong removal mode.' }
Write-Host 'PASS: PowerShell 5.1 parse, native argument forwarding with spaces, JSON results, catalog, and invalid-profile rejection.'
Write-Host 'PASS: Blank keeps all, invalid rows retry, duplicate rows deduplicate, Keep default and explicit Remove.'
$global:LASTEXITCODE = 0
exit 0
