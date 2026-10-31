Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$process = Get-Process -Name "RouterPlus" | Select-Object -First 1
if (-not $process) {
    Write-Host "RouterPlus not found"
    exit 1
}

$mainWindow = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
if (-not $mainWindow) {
    Write-Host "Main window not found"
    exit 1
}

Write-Host "Found main window: $($mainWindow.Current.Name)"

# Find ProfileSearchTextBox
$searchTextBox = $mainWindow.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileSearchTextBox")
)

if (-not $searchTextBox) {
    Write-Host "ProfileSearchTextBox not found"
    exit 1
}

Write-Host "Found ProfileSearchTextBox"

# Set focus and type email
$pattern = $searchTextBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
if ($pattern) {
    $pattern.SetValue("hieuck.adobe2022@gmail.com")
    Write-Host "Typed email in search box"
} else {
    Write-Host "ValuePattern not available on search box"
}

Start-Sleep -Seconds 2

# Find the profile list item and click via mouse
$allElements = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$profileItem = $null
foreach ($elem in $allElements) {
    if ($elem.Current.Name -like "*hieuck.adobe2022@gmail.com*") {
        $profileItem = $elem
        Write-Host "Found profile item: $($elem.Current.Name) (ControlType: $($elem.Current.ControlType))"
        break
    }
}

if ($profileItem) {
    $clickablePoint = $profileItem.GetClickablePoint()
    [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new([int]$clickablePoint.X, [int]$clickablePoint.Y)
    Start-Sleep -Milliseconds 500
    Add-Type -MemberDefinition @"
        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);
"@ -Name "Win32" -Namespace "Native"
    [Native.Win32]::mouse_event(0x0002, 0, 0, 0, 0)  # MOUSEEVENTF_LEFTDOWN
    [Native.Win32]::mouse_event(0x0004, 0, 0, 0, 0)  # MOUSEEVENTF_LEFTUP
    Write-Host "Clicked profile item via mouse_event"
} else {
    Write-Host "Could not find profile item"
}

Start-Sleep -Seconds 2

# Find and click the Agnes provider button via mouse
$allElements = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$agnesProviderBtn = $null
foreach ($elem in $allElements) {
    if ($elem.Current.Name -eq "Agnes (8)" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
        $agnesProviderBtn = $elem
        Write-Host "Found Agnes provider button: $($elem.Current.Name)"
        break
    }
}

if ($agnesProviderBtn) {
    $clickablePoint = $agnesProviderBtn.GetClickablePoint()
    [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new([int]$clickablePoint.X, [int]$clickablePoint.Y)
    Start-Sleep -Milliseconds 500
    [Native.Win32]::mouse_event(0x0002, 0, 0, 0, 0)
    [Native.Win32]::mouse_event(0x0004, 0, 0, 0, 0)
    Write-Host "Clicked Agnes provider button via mouse_event"
    Start-Sleep -Seconds 3
} else {
    Write-Host "Agnes provider button not found"
}

# Re-find all elements
$allElements = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Look for "Tự động thêm" buttons and find the one in Agnes container
$agnesAutoAddBtn = $null
foreach ($elem in $allElements) {
    $name = $elem.Current.Name
    if (($name -eq "Tự động thêm" -or $name -like "*Tự động thêm*") -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
        Write-Host "Found 'Tự động thêm' button: Enabled=$($elem.Current.IsEnabled)"
        
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($elem)
        $foundAgnes = $false
        for ($i = 0; $i -lt 15 -and $parent; $i++) {
            if ($parent.Current.Name -like "*Agnes*") {
                $foundAgnes = $true
                Write-Host "  Found in Agnes container: $($parent.Current.Name)"
                break
            }
            $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
        }
        
        if ($foundAgnes) {
            $agnesAutoAddBtn = $elem
            Write-Host "Selected Agnes 'Tự động thêm' button"
            break
        }
    }
}

if ($agnesAutoAddBtn) {
    Write-Host "Invoking Agnes 'Tự động thêm' button..."
    $invokePattern = $agnesAutoAddBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    if ($invokePattern -and $agnesAutoAddBtn.Current.IsEnabled) {
        $invokePattern.Invoke()
        Write-Host "Button invoked successfully!"
    } else {
        Write-Host "Button not invokable or not enabled"
    }
} else {
    Write-Host "Could not find Agnes 'Tự động thêm' button"
}

Write-Host "Done - now monitor for 300 seconds..."
Write-Host "You should see Chrome opening and the Agnes flow starting"