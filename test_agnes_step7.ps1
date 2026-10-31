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

# Find the Agnes ProviderCardViewModel (DataItem) - look at the full tree
Write-Host "=== Looking for Agnes ProviderCardViewModel ==="
$agnesCard = $null
foreach ($elem in $allElements) {
    if ($elem.Current.ClassName -eq "RouterPlus.App.ViewModels.ProviderCardViewModel" -or $elem.Current.Name -eq "Agnes AI") {
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($elem)
        for ($idx = 0; $idx -lt 5 -and $parent; $idx++) {
            if ($parent.Current.ClassName -eq "RouterPlus.App.ViewModels.ProviderCardViewModel") {
                $agnesCard = $parent
                Write-Host ("Found Agnes card at parent level {0}: {1} Class={2}" -f $idx, $parent.Current.Name, $parent.Current.ClassName)
                break
            }
            $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
        }
        if ($agnesCard) { break }
    }
}

if ($agnesCard) {
    Write-Host "=== Children of Agnes card ==="
    $children = $agnesCard.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
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
} else {
    Write-Host "Agnes card not found via ClassName, searching by text 'Agnes AI'..."
    foreach ($elem in $allElements) {
        if ($elem.Current.Name -eq "Agnes AI") {
            Write-Host "Found 'Agnes AI' text element"
            $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($elem)
            for ($idx = 0; $idx -lt 10 -and $parent; $idx++) {
                Write-Host ("  Parent {0}: Name='{1}' Class='{2}' Type='{3}'" -f $idx, $parent.Current.Name, $parent.Current.ClassName, $parent.Current.ControlType)
                $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
            }
            break
        }
    }
}

Write-Host "Done"