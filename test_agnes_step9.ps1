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
    $pattern.SetValue("hieuck.it@gmail.com")
    Write-Host "Typed email in search box"
} else {
    Write-Host "ValuePattern not available on search box"
}

Start-Sleep -Seconds 2

# Find the profile list item and click via mouse
$allElements = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$profileItem = $null
foreach ($elem in $allElements) {
    if ($elem.Current.Name -like "*hieuck.it@gmail.com*") {
        $profileItem = $elem
        Write-Host "Found profile item: $($elem.Current.Name) (ControlType: $($elem.Current.ControlType))"
        break
    }
}

if ($profileItem) {
    $clickablePoint = $profileItem.GetClickablePoint()
    if ($clickablePoint) {
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
        Write-Host "Profile item has no clickable point"
    }
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
    if ($clickablePoint) {
        [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new([int]$clickablePoint.X, [int]$clickablePoint.Y)
        Start-Sleep -Milliseconds 500
        [Native.Win32]::mouse_event(0x0002, 0, 0, 0, 0)
        [Native.Win32]::mouse_event(0x0004, 0, 0, 0, 0)
        Write-Host "Clicked Agnes provider button via mouse_event"
        Start-Sleep -Seconds 3
    } else {
        Write-Host "Agnes provider button has no clickable point"
    }
} else {
    Write-Host "Agnes provider button not found"
}

# Re-find all elements
$allElements = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Find all DataItems / ItemsControlItems
Write-Host "=== All DataItems and ItemsControlItems ==="
foreach ($elem in $allElements) {
    if ($elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::DataItem -or 
        $elem.Current.ClassName -eq "ItemsControlItem" -or
        $elem.Current.ClassName -eq "RouterPlus.App.ViewModels.ProviderCardViewModel") {
        $children = $elem.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        $hasAgnes = $false
        foreach ($child in $children) {
            if ($child.Current.Name -eq "Agnes AI") {
                $hasAgnes = $true
                break
            }
        }
        if ($hasAgnes) {
            Write-Host ("Found Agnes container: Name='{0}' Class='{1}' Type='{2}'" -f $elem.Current.Name, $elem.Current.ClassName, $elem.Current.ControlType)
            Write-Host "=== Children of this container ==="
            for ($i = 0; $i -lt $children.Count; $i++) {
                $child = $children[$i]
                $name = $child.Current.Name
                $autoId = $child.Current.AutomationId
                $controlType = $child.Current.ControlType
                $isEnabled = $child.Current.IsEnabled
                $className = $child.Current.ClassName
                
                if ($name -or $autoId -or $className) {
                    Write-Host ("  [{0}] Name='{1}' AutoId='{2}' Type='{3}' Enabled={4} Class='{5}'" -f $i, $name, $autoId, $controlType, $isEnabled, $className)
                }
            }
            break
        }
    }
}

Write-Host "Done"