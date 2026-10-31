Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Mouse {
    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);
    public const uint MOUSEEVENTF_LEFTDOWN = 0x02;
    public const uint MOUSEEVENTF_LEFTUP = 0x04;
    public static void Click() {
        mouse_event(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
    }
}
"@

$proc = Get-Process -Name "RouterPlus" | Select-Object -First 1
if (-not $proc) {
    Write-Output "RouterPlus process not found!"
    exit 1
}
Write-Output "Found RouterPlus process: PID=$($proc.Id)"

# Use the correct window handle for "9Router Profile Tool"
$automation = [System.Windows.Automation.AutomationElement]::FromHandle(47258290)
if ($automation -eq $null) {
    Write-Output "Main window element not found!"
    exit 1
}
Write-Output "Main window title: $($automation.Current.Name)"

# Step 1: Find ProfileSearchTextBox and set text
$searchBox = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileSearchTextBox"))
if ($searchBox -eq $null) {
    Write-Output "ProfileSearchTextBox not found!"
    exit 1
}

$textPattern = $searchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
if ($textPattern -eq $null) {
    Write-Output "ProfileSearchTextBox does not support ValuePattern!"
    exit 1
}

Write-Output "Setting profile search text to 'hieuck.adobe2022@gmail.com'"
$textPattern.SetValue("hieuck.adobe2022@gmail.com")

# Wait for filtering to complete
Start-Sleep -Seconds 5

# Step 2: Find the profile list and select the profile
$profileList = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList"))
if ($profileList -eq $null) {
    Write-Output "ProfileList not found!"
    exit 1
}

$children = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, 
    [System.Windows.Automation.Condition]::TrueCondition)

Write-Output "Initial children count: $($children.Count)"

if ($children.Count -eq 0) {
    Write-Output "No ListItems found initially, waiting more..."
    Start-Sleep -Seconds 5
    $children = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, 
        [System.Windows.Automation.Condition]::TrueCondition)
    Write-Output "Children after 5s more: $($children.Count)"
}

if ($children.Count -eq 0) {
    Write-Output "Still no ListItems! Checking descendants..."
    $descendants = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, 
        [System.Windows.Automation.Condition]::TrueCondition)
    Write-Output "Descendants count: $($descendants.Count)"
    for ($i = 0; $i -lt $descendants.Count; $i++) {
        $d = $descendants[$i]
        if (-not [string]::IsNullOrEmpty($d.Current.Name)) {
            Write-Output "  [$i] Name: '$($d.Current.Name)', ControlType: $($d.Current.ControlType.ProgrammaticName)"
        }
    }
    exit 1
}

# Find the ListItem containing hieuck.adobe2022@gmail.com
$listItem = $null
for ($i = 0; $i -lt $children.Count; $i++) {
    $item = $children[$i]
    $textElements = $item.FindAll([System.Windows.Automation.TreeScope]::Descendants, 
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))
    foreach ($textEl in $textElements) {
        if ($textEl.Current.Name -eq "hieuck.adobe2022@gmail.com") {
            $listItem = $item
            Write-Output "Found ListItem at index $i containing 'hieuck.adobe2022@gmail.com'"
            break
        }
    }
    if ($listItem) { break }
}

if ($listItem -eq $null) {
    Write-Output "Could not find ListItem with target profile!"
    exit 1
}

# Select the profile
$selectionItemPattern = $listItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
if ($selectionItemPattern) {
    $selectionItemPattern.Select()
    Write-Output "Profile selected via SelectionItemPattern"
} else {
    Write-Output "ListItem does not support SelectionItemPattern!"
    exit 1
}

Start-Sleep -Seconds 3

# Step 3: Find the Agnes card and its "Tự động thêm" button
# First find the Agnes card (DataItem containing "Agnes AI")
$agnesCard = $null
$allElements = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, 
    [System.Windows.Automation.Condition]::TrueCondition)
for ($i = 0; $i -lt $allElements.Count; $i++) {
    $el = $allElements[$i]
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::DataItem -and $el.Current.Name -eq "Agnes AI") {
        $agnesCard = $el
        Write-Output "Found Agnes card at index $i"
        break
    }
}

if ($agnesCard -eq $null) {
    Write-Output "Agnes card not found!"
    exit 1
}

# Find "Tự động thêm" button within Agnes card
$condition = [System.Windows.Automation.AndCondition]::new(
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button),
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, "Tự động thêm")
)
$autoAddButton = $agnesCard.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)

if ($autoAddButton -eq $null) {
    Write-Output "ERROR: 'Tự động thêm' button not found within Agnes card"
    # Fallback: search at main window level
    $autoAddButton = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($autoAddButton -eq $null) {
        Write-Output "ERROR: 'Tự động thêm' button not found at main window level either"
        exit 1
    } else {
        Write-Output "Found 'Tự động thêm' button at main window level (fallback)"
    }
} else {
    Write-Output "Found 'Tự động thêm' button within Agnes card"
}

Write-Output "Button IsEnabled: $($autoAddButton.Current.IsEnabled)"
Write-Output "Button IsOffscreen: $($autoAddButton.Current.IsOffscreen)"

# Step 4: Invoke the button once
Write-Output "=== INVOKING 'Tự động thêm' AT $([DateTime]::UtcNow.ToString('o')) ==="

$invokePattern = $autoAddButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
if ($invokePattern) {
    $invokePattern.Invoke()
    Write-Output "Button invoked via InvokePattern"
} else {
    Write-Output "Button does not support InvokePattern, trying mouse click..."
    $clickablePoint = $autoAddButton.GetClickablePoint()
    if ($clickablePoint) {
        [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new([int]$clickablePoint.X, [int]$clickablePoint.Y)
        [Mouse]::Click()
        Write-Output "Button clicked via mouse event"
    } else {
        Write-Output "Could not get clickable point for 'Tự động thêm' button"
        exit 1
    }
}

# Step 5: Monitor for up to 120 seconds
$timeout = [TimeSpan]::FromSeconds(120)
$deadline = [DateTime]::UtcNow + $timeout
$checkInterval = 5

Write-Output "Monitoring for Agnes key flow completion (up to 120s)..."

$iteration = 0
$finalStatusText = ""
$chromeOpened = $false
$agnesConnectionFound = $false
$errorMessage = ""

while ([DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Seconds $checkInterval
    $iteration++

    # Check StatusText
    $statusTextElement = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "StatusText"))
    
    if ($statusTextElement -ne $null) {
        $statusText = $statusTextElement.Current.Name
        if (-not [string]::IsNullOrEmpty($statusText)) {
            $finalStatusText = $statusText
            Write-Output "[$([DateTime]::UtcNow.ToString('HH:mm:ss'))] StatusText: $statusText"
        }
    }

    # Check if button is still enabled
    $autoAddButton = $agnesCard.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($autoAddButton -eq $null) {
        $autoAddButton = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    }
    if ($autoAddButton -ne $null) {
        $isEnabled = $autoAddButton.Current.IsEnabled
        $isVisible = -not $autoAddButton.Current.IsOffscreen
        Write-Output "[$([DateTime]::UtcNow.ToString('HH:mm:ss'))] Button: Enabled=$isEnabled, Visible=$isVisible"
    } else {
        Write-Output "[$([DateTime]::UtcNow.ToString('HH:mm:ss'))] Button: Not found (may be hidden during operation)"
    }

    # Check for new Agnes connection via 9Router API
    try {
        $headers = @{Authorization="Bearer 123456"}
        $response = Invoke-WebRequest -Uri "http://localhost:5000/api/providers" -Headers $headers -Method Get -UseBasicParsing -TimeoutSec 5
        if ($response.StatusCode -eq 200) {
            $providers = $response.Content | ConvertFrom-Json
            $agnesProvider = $providers | Where-Object { $_.kind -eq "Agnes" }
            if ($agnesProvider) {
                $connections = $agnesProvider.connections
                if ($connections) {
                    foreach ($conn in $connections) {
                        if ($conn.email -eq "hieuck.adobe2022@gmail.com") {
                            Write-Output "*** NEW AGNES CONNECTION FOUND FOR hieuck.adobe2022@gmail.com ***"
                            Write-Output "Connection details: $($conn | ConvertTo-Json -Depth 5)"
                            $agnesConnectionFound = $true
                        }
                    }
                }
            }
        }
    } catch {
        $errorMessage = $_.Exception.Message
        Write-Output "API check failed: $errorMessage"
    }

    # Also check Chrome CDP ports
    try {
        $cdpResponse = Invoke-WebRequest -Uri "http://127.0.0.1:9222/json" -Method Get -UseBasicParsing -TimeoutSec 3
        if ($cdpResponse.StatusCode -eq 200) {
            $tabs = $cdpResponse.Content | ConvertFrom-Json
            $agnesTabs = $tabs | Where-Object { $_.url -like "*agnes-ai.com*" -or $_.url -like "*accounts.google.com*" }
            if ($agnesTabs) {
                Write-Output "*** CHROME TABS FOUND ***"
                foreach ($tab in $agnesTabs) {
                    Write-Output "  Tab: url=$($tab.url), title=$($tab.title)"
                }
                $chromeOpened = $true
            }
        }
    } catch {
        # Ignore CDP errors
    }
}

# Final checks after timeout
Write-Output "=== MONITORING TIMEOUT (120s) ==="

# Check Chrome processes with remote-debugging-port
try {
    $chromeProcs = Get-CimInstance Win32_Process -Filter "Name LIKE 'chrome%'"
    foreach ($p in $chromeProcs) {
        $cmd = $p.CommandLine
        if ($cmd -match "remote-debugging-port") {
            Write-Output "Chrome process with remote-debugging-port found: PID=$($p.ProcessId), CmdLine=$cmd"
            $chromeOpened = $true
        }
    }
} catch {
    Write-Output "Could not query Chrome processes: $($_.Exception.Message)"
}

Write-Output "=== FINAL REPORT ==="
Write-Output "Final StatusText: $finalStatusText"
Write-Output "Chrome opened (CDP/port): $chromeOpened"
Write-Output "New Agnes connection found: $agnesConnectionFound"
if ($errorMessage) {
    Write-Output "Error message: $errorMessage"
}