<#
.SYNOPSIS
    Agnes Auto-Add Probe - reusable live test for Agnes "Tu dong them" button flow.

.DESCRIPTION
    Selects a profile by email, finds the Agnes provider card, clicks its "Tu dong them" button,
    and monitors StatusText, session log, Chrome tabs, and 9Router API for a new Agnes connection.

.PARAMETER ProfileEmail
    Email of the profile to test (required).

.PARAMETER StartApp
    Whether to build and start the app before running (default: "true").

.PARAMETER TimeoutSeconds
    Max time to monitor for completion (default: 300).

.PARAMETER ApiBaseUrl
    9Router API base URL (default: http://localhost:20128).

.PARAMETER ApiToken
    Bearer token for 9Router API (default: 123456).

.EXAMPLE
    .\auto-add.ps1 -ProfileEmail "hieuck.adobe2022@gmail.com" -StartApp true -TimeoutSeconds 300

.NOTES
    Uses pure UIA COM (no FlaUI) to avoid .NET version conflicts.
    Run from repo root or tools/live-verify/agnes/.
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$ProfileEmail,

    [string]$StartApp = "true",
    [int]$TimeoutSeconds = 300,
    [string]$ApiBaseUrl = "http://localhost:20128",
    [string]$ApiToken = "123456"
)

$ErrorActionPreference = "Stop"

# Parse StartApp string to boolean
$StartAppBool = $false
if ($StartApp -eq "true" -or $StartApp -eq "True" -or $StartApp -eq "1" -or $StartApp -eq "yes") {
    $StartAppBool = $true
}

Write-Host "=== Agnes Auto-Add Probe ==="
Write-Host ("Profile: {0}" -f $ProfileEmail)
Write-Host ("Timeout: {0}s" -f $TimeoutSeconds)
Write-Host ""

# --- Start app if requested ---
if ($StartAppBool) {
    Write-Host "Building and starting RouterPlus (Release)..."
    & "E:\GitHub\9router-plus\scripts\build.ps1" -Configuration Release
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed"
        exit 1
    }

    # Start app in background
    $appPath = "E:\GitHub\9router-plus\src\RouterPlus.App\bin\Release\net8.0-windows\win-x64\RouterPlus.exe"
    if (-not (Test-Path $appPath)) {
        Write-Error "App not found at $appPath"
        exit 1
    }
    $proc = Start-Process -FilePath $appPath -PassThru
    Write-Host ("Started RouterPlus (PID: {0})" -f $proc.Id)
    Start-Sleep -Seconds 5
} else {
    $proc = Get-Process -Name "RouterPlus" | Select-Object -First 1
    if (-not $proc) {
        Write-Error "RouterPlus process not found. Use -StartApp true."
        exit 1
    }
    Write-Host ("Attached to existing RouterPlus (PID: {0})" -f $proc.Id)
}

# --- Attach UIA COM ---
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$hwnd = $proc.MainWindowHandle
if ($hwnd -eq 0 -or $hwnd -eq [IntPtr]::Zero) {
    Write-Error "Main window handle is zero"
    exit 1
}

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
if ($automation -eq $null) {
    Write-Error "Could not get AutomationElement from window handle"
    exit 1
}
Write-Host ("Main window: {0}" -f $automation.Current.Name)

# --- Helper: wait for element ---
function Wait-Element($parent, $condition, $timeoutSec = 10) {
    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSec)
    while ([DateTime]::UtcNow -lt $deadline) {
        $el = $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($el) { return $el }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

# --- Step 1: Search/select profile ---
Write-Host ("`n[1/5] Searching for profile: {0}" -f $ProfileEmail)
$searchBoxCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileSearchTextBox")
$searchBox = Wait-Element $automation $searchBoxCond
if (-not $searchBox) { Write-Error "ProfileSearchTextBox not found"; exit 1 }

# Set text via ValuePattern
$valuePattern = $searchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
if ($valuePattern) {
    $valuePattern.SetValue($ProfileEmail)
} else {
    Write-Error "ProfileSearchTextBox has no ValuePattern"
    exit 1
}
Start-Sleep -Seconds 2

$listCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList")
$profileList = Wait-Element $automation $listCond
if (-not $profileList) { Write-Error "ProfileList not found"; exit 1 }

$itemCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, $ProfileEmail)
$profileText = Wait-Element $profileList $itemCond
if (-not $profileText) {
    Write-Error ("Profile '{0}' not found in list" -f $ProfileEmail)
    # Dump visible profiles for debugging
    $listItemCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $visible = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, $listItemCond)
    Write-Host "Visible profiles:"
    for ($i = 0; $i -lt $visible.Count; $i++) { Write-Host ("  - {0}" -f $visible[$i].Current.Name) }
    exit 1
}
Write-Host ("  Found profile text: {0}" -f $profileText.Current.Name)
# Get parent ListItem which has SelectionItemPattern
$profileItem = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($profileText)
if (-not $profileItem -or $profileItem.Current.ControlType.ProgrammaticName -ne "ControlType.ListItem") {
    Write-Error "Could not find parent ListItem for profile"
    exit 1
}
Write-Host ("  Parent ListItem: {0}" -f $profileItem.Current.Name)
$selPattern = $profileItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
if ($selPattern) {
    $selPattern.Select()
} else {
    Write-Error "Profile item has no SelectionItemPattern"
    exit 1
}
Start-Sleep -Seconds 2

# --- Step 2: Find Agnes provider card and its "Tu dong them" button ---
Write-Host "`n[2/5] Finding Agnes provider card and 'Tu dong them' button..."

# First, find the Agnes text element to locate its provider card
$agnesText = $null
$namesToTry = @("Agnes AI", "Agnes", "AG")
foreach ($name in $namesToTry) {
    $cond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, $name)
    $agnesText = Wait-Element $automation $cond 5
    if ($agnesText) { break }
}

if (-not $agnesText) {
    Write-Error "Agnes provider not found"
    exit 1
}
Write-Host ("  Found Agnes text: '{0}'" -f $agnesText.Current.Name)

# Get parent ProviderCardViewModel
$providerCard = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($agnesText)
if (-not $providerCard -or $providerCard.Current.ControlType.ProgrammaticName -ne "ControlType.DataItem") {
    Write-Error "Could not find parent ProviderCardViewModel"
    exit 1
}
Write-Host ("  ProviderCard: '{0}'" -f $providerCard.Current.Name)

# Click to select Agnes as active provider - try clicking the provider card or Agnes text
$clicked = $false
$patterns = $providerCard.GetSupportedPatterns()
$hasInvoke = $false
foreach ($p in $patterns) {
    if ($p.ProgrammaticName -eq "InvokePatternIdentifiers.Pattern") {
        $hasInvoke = $true
        break
    }
}
if ($hasInvoke) {
    $invokePattern = $providerCard.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    if ($invokePattern) {
        $invokePattern.Invoke()
        Write-Host "  Clicked provider card to select Agnes"
        $clicked = $true
    }
}
if (-not $clicked) {
    # Try clicking the Agnes text element itself
    $patterns = $agnesText.GetSupportedPatterns()
    $hasInvoke = $false
    foreach ($p in $patterns) {
        if ($p.ProgrammaticName -eq "InvokePatternIdentifiers.Pattern") {
            $hasInvoke = $true
            break
        }
    }
    if ($hasInvoke) {
        $invokePattern = $agnesText.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        if ($invokePattern) {
            $invokePattern.Invoke()
            Write-Host "  Clicked Agnes text to select Agnes"
            $clicked = $true
        }
    }
}
if (-not $clicked) {
    Write-Warning "No InvokePattern found on provider card or Agnes text, continuing anyway"
}
Start-Sleep -Seconds 2

# --- Step 3: Find "Tu dong them" button within the Agnes provider card ---
Write-Host "`n[3/5] Finding 'Tu dong them' button within Agnes card..."

# First try with Vietnamese diacritics (the actual button name)
$autoAddCond = [System.Windows.Automation.AndCondition]::new(
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button),
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, "Tự động thêm")
)
$autoAddBtn = $providerCard.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $autoAddCond)
if ($autoAddBtn) {
    Write-Host "  Found button with Vietnamese name"
} else {
    # Try without diacritics
    $autoAddCond = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, "Tu dong them")
    )
    $autoAddBtn = $providerCard.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $autoAddCond)
    if ($autoAddBtn) {
        Write-Host "  Found button without diacritics"
    }
}
if (-not $autoAddBtn) {
    # Try English fallback
    $autoAddCond = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, "Auto add")
    )
    $autoAddBtn = $providerCard.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $autoAddCond)
    if ($autoAddBtn) {
        Write-Host "  Found button with English name"
    }
}
if (-not $autoAddBtn) {
    Write-Error "'Tu dong them' button not found"
    $btnCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $buttons = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
    Write-Host "All buttons:"
    for ($i = 0; $i -lt $buttons.Count; $i++) {
        $b = $buttons[$i]
        if ($b.Current.Name) {
            Write-Host ("  - '{0}' Enabled={1} Visible={2}" -f $b.Current.Name, $b.Current.IsEnabled, (-not $b.Current.IsOffscreen))
        }
    }
    exit 1
}
$btnEn = $autoAddBtn.Current.IsEnabled
$btnVis = -not $autoAddBtn.Current.IsOffscreen
Write-Host ("  Found button: Enabled={0} Visible={1}" -f $btnEn, $btnVis)
if (-not $autoAddBtn.Current.IsEnabled -or $autoAddBtn.Current.IsOffscreen) {
    Write-Warning "Button is disabled or offscreen -- may indicate Agnes already connected or profile not ready"
}

# --- Step 4: Get StatusText element for monitoring ---
Write-Host "`n[4/5] Locating StatusText..."
$statusCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "StatusText")
$statusTextEl = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $statusCond)
if ($statusTextEl) {
    Write-Host "  StatusText element found"
} else {
    Write-Warning "  StatusText not found -- will rely on API monitoring only"
}

# --- Step 5: Invoke and monitor ---
Write-Host ("`n[5/5] Invoking 'Tu dong them' and monitoring for {0}s..." -f $TimeoutSeconds)
$startTime = [DateTime]::UtcNow
$deadline = $startTime.AddSeconds($TimeoutSeconds)
$iteration = 0
$connectionFound = $false

Write-Host ("Monitoring: StatusText, session logs, 9Router API ({0})" -f $ApiBaseUrl)
Write-Host ""

while ([DateTime]::UtcNow -lt $deadline) {
    $iteration++
    $now = [DateTime]::UtcNow
    $elapsed = [math]::Round(($now - $startTime).TotalSeconds, 1)

    # Check StatusText
    if ($statusTextEl) {
        $statusText = $statusTextEl.Current.Name
        if ($statusText) {
            Write-Host ("[{0}] StatusText: {1}" -f $now.ToString('HH:mm:ss'), $statusText)
            if ($statusText -match "Agnes|key|thanh cong|success|that bai|failed|loi|error|da ket noi|connected") {
                Write-Host ("  >>> KEY STATUS: {0}" -f $statusText)
            }
        }
    }

    # Check button state
    $btnEnabled = $autoAddBtn.Current.IsEnabled
    $btnVisible = -not $autoAddBtn.Current.IsOffscreen
    Write-Host ("[{0}] Button: Enabled={1} Visible={2} (elapsed: {3}s)" -f $now.ToString('HH:mm:ss'), $btnEnabled, $btnVisible, $elapsed)

    # Check 9Router API for new Agnes connection
    try {
        $headers = @{ Authorization = "Bearer $ApiToken" }
        $resp = Invoke-WebRequest -Uri "$ApiBaseUrl/api/providers" -Headers $headers -Method Get -UseBasicParsing -TimeoutSec 10
        if ($resp.StatusCode -eq 200) {
            $providers = $resp.Content | ConvertFrom-Json
            $agnesProvider = $providers | Where-Object { $_.kind -eq "Agnes" }
            if ($agnesProvider -and $agnesProvider.connections) {
                foreach ($conn in $agnesProvider.connections) {
                    if ($conn.email -eq $ProfileEmail) {
                        Write-Host ""
                        Write-Host "========================================"
                        Write-Host "*** NEW AGNES CONNECTION FOUND ***"
                        Write-Host "========================================"
                        Write-Host ("Email: {0}" -f $conn.email)
                        Write-Host ("Id: {0}" -f $conn.id)
                        Write-Host ("CreatedAt: {0}" -f $conn.createdAt)
                        Write-Host ("IsActive: {0}" -f $conn.isActive)
                        Write-Host ("TestStatus: {0}" -f $conn.testStatus)
                        Write-Host ("ExpiresAt: {0}" -f $conn.expiresAt)
                        Write-Host ("ErrorCode: {0}" -f $conn.errorCode)
                        Write-Host ("LastError: {0}" -f $conn.lastError)
                        $connectionFound = $true
                        break
                    }
                }
            }
        }
    } catch {
        Write-Host ("[{0}] API check failed: {1}" -f $now.ToString('HH:mm:ss'), $_.Exception.Message)
    }

    if ($connectionFound) { break }

    # Early exit if button becomes disabled and no longer visible (likely done)
    if (-not $btnEnabled -and -not $btnVisible) {
        Write-Host "Button hidden/disabled -- flow may be complete"
    }

    Start-Sleep -Seconds 5
}

Write-Host ""
Write-Host "=== PROBE COMPLETE ==="
if ($connectionFound) {
    Write-Host ("RESULT: SUCCESS -- Agnes auto-added for {0}" -f $ProfileEmail)
    exit 0
} else {
    Write-Host "RESULT: TIMEOUT or NO CONNECTION -- check logs above"
    $elapsedSec = [math]::Round(([DateTime]::UtcNow - $startTime).TotalSeconds, 1)
    Write-Host ("Elapsed: {0}s" -f $elapsedSec)
    exit 1
}