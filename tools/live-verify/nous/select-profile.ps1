<#
.SYNOPSIS
    Select a profile in RouterPlus and invoke the provider "Them" button for a given provider.

.DESCRIPTION
    UIAutomation helper. Optionally starts the app. Prints status lines.

.PARAMETER ProfileEmail
    Profile email to select (mandatory).

.PARAMETER Provider
    Provider display name to click (default: Nous).

.PARAMETER StartApp
    If set, kill any running RouterPlus, start the Release exe, wait for main window.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$ProfileEmail,

    [Parameter()]
    [string]$Provider = 'Nous',

    [Parameter()]
    [switch]$StartApp
)

$themName = 'Th' + [char]0xEA + 'm'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exePath = 'E:\GitHub\9router-plus\src\RouterPlus.App\bin\Release\net8.0-windows\RouterPlus.exe'

function Fail($msg) {
    Write-Error $msg
    exit 1
}

function Find-ByAutomationId($parent, $id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

if ($StartApp) {
    Get-Process -Name 'RouterPlus' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    if (-not (Test-Path $exePath)) { Fail "EXE_NOT_FOUND $exePath" }
    Start-Process -FilePath $exePath | Out-Null
    $deadline = (Get-Date).AddSeconds(30)
    $proc = $null
    while ((Get-Date) -lt $deadline) {
        $proc = Get-Process -Name 'RouterPlus' -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
        if ($proc) { break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $proc) { Fail "MAIN_WINDOW_NOT_FOUND within 30s" }
}

$process = Get-Process -Name 'RouterPlus' -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $process) { Fail "ROUTERPLUS_NOT_RUNNING" }

$mainWindow = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)

# Normalize window
try {
    $wp = $mainWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
    if ($wp) { $wp.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal) }
} catch {}
$mainWindow.SetFocus()
Start-Sleep -Milliseconds 300

Write-Host 'WINDOW_OK'

# Set search box
$searchBox = Find-ByAutomationId $mainWindow 'ProfileSearchTextBox'
if (-not $searchBox) { Fail "PROFILE_SEARCH_BOX_NOT_FOUND" }
try {
    $vp = $searchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $vp.SetValue($ProfileEmail)
} catch { Fail "FAILED_TO_SET_SEARCH: $($_.Exception.Message)" }
Start-Sleep -Milliseconds 500

# Select matching ListItem
$profileList = Find-ByAutomationId $mainWindow 'ProfileList'
if (-not $profileList) { Fail "PROFILE_LIST_NOT_FOUND" }

# Find the Text element whose Name equals the email inside ProfileList,
# then walk up via ControlViewWalker until a ListItem.
$textTypeCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
$textCandidates = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textTypeCond)

$listItem = $null
foreach ($tc in $textCandidates) {
    if ($tc.Current.Name -ne $ProfileEmail) { continue }
    $cur = $tc
    while ($cur -and $cur -ne [System.Windows.Automation.AutomationElement]::RootElement) {
        if ($cur.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) {
            $listItem = $cur
            break
        }
        $cur = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($cur)
    }
    if ($listItem) { break }
}

if (-not $listItem) { Fail "PROFILE_LIST_ITEM_NOT_FOUND $ProfileEmail" }

$selected = $false
try {
    $sip = $listItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($sip) { $sip.Select(); $selected = $true }
} catch { $selected = $false }

Start-Sleep -Milliseconds 300

$isSelected = $false
try {
    $sip = $listItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($sip) { $isSelected = [bool]$sip.Current.IsSelected }
} catch { $isSelected = $false }

if (-not $selected -or -not $isSelected) { Fail "PROFILE_NOT_SELECTED $ProfileEmail" }
Write-Host "PROFILE_SELECTED=$ProfileEmail"

# Find provider card containing provider text, then its Them button
# Iterate DataItem elements; require descendant Text == Provider and descendant Button 'Them'.
$dataTypeCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)
$dataItems = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, $dataTypeCond)

$button = $null
foreach ($di in $dataItems) {
    $textTypeCond2 = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $texts = $di.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textTypeCond2)
    $hasProviderText = $false
    foreach ($t in $texts) {
        if ($t.Current.Name -eq $Provider) { $hasProviderText = $true; break }
    }
    if (-not $hasProviderText) { continue }

    $btnCond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $themName))
    )
    $btns = $di.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
    if ($btns.Count -eq 0) { continue }

    $b = $btns[0]
    if (-not $b.Current.IsEnabled) { Fail "BUTTON_DISABLED for provider $Provider" }
    $button = $b
    break
}

if (-not $button) { Fail "CARD_OR_BUTTON_NOT_FOUND for provider $Provider" }
Write-Host 'CARD_FOUND'

$enabled = $button.Current.IsEnabled
Write-Host "BUTTON_ENABLED=$enabled"

try {
    $ip = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $ip.Invoke()
} catch { Fail "INVOKE_FAILED: $($_.Exception.Message)" }
Write-Host "INVOKED=$Provider"

Start-Sleep -Milliseconds 800
$statusEl = Find-ByAutomationId $mainWindow 'StatusText'
$status = ''
if ($statusEl) { $status = $statusEl.Current.Name }
Write-Host "STATUS=$status"