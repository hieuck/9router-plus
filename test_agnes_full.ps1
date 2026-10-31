Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);
}
"@

$profileToolHwnd = [IntPtr]207558060
$mainWindow = [System.Windows.Automation.AutomationElement]::FromHandle($profileToolHwnd)
if (-not $mainWindow) {
    Write-Host "Main window not found"
    exit 1
}

Write-Host "=== Found Profile Tool Window ==="

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

# Clear and type email then press Enter
$pattern = $searchTextBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
if ($pattern) {
    $pattern.SetValue("")
    Start-Sleep -Milliseconds 500
    $pattern.SetValue("hieuck.adobe2022@gmail.com")
    Write-Host "Typed email in search box"
} else {
    Write-Host "ValuePattern not available on search box"
    exit 1
}

# Press Enter to trigger search
Start-Sleep -Milliseconds 500
[Win32]::keybd_event(0x0D, 0, 0, 0)
[Win32]::keybd_event(0x0D, 0, 2, 0)
Write-Host "Pressed Enter"
Start-Sleep -Seconds 3

# Find ProfileList and check children
$profileList = $mainWindow.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList")
)

if (-not $profileList) {
    Write-Host "ProfileList not found"
    exit 1
}

Write-Host "Found ProfileList"
$children = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
Write-Host "ProfileList children count: $($children.Count)"

$profileItem = $null
foreach ($child in $children) {
    Write-Host ("  Child: Name='{0}', ControlType={1}, AutomationId='{2}'" -f $child.Current.Name, $child.Current.ControlType, $child.Current.AutomationId)
    $grandChildren = $child.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($gc in $grandChildren) {
        $name = $gc.Current.Name
        if ($name -and $name -like "*hieuck.adobe2022*") {
            Write-Host "  *** Found target profile in grandchild: $name ***"
            $profileItem = $child
        }
    }
}

if (-not $profileItem) {
    # Try to find by name directly in all descendants
    $allDescendants = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($elem in $allDescendants) {
        $name = $elem.Current.Name
        if ($name -and $name -like "*hieuck.adobe2022*") {
            Write-Host "  *** Found target profile element: $name, ControlType=$($elem.Current.ControlType) ***"
            # Try to get parent ListItem
            $parent = $elem
            for ($i=0; $i -lt 5; $i++) {
                $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
                if (-not $parent) { break }
                if ($parent.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) {
                    $profileItem = $parent
                    Write-Host "  *** Found parent ListItem ***"
                    break
                }
            }
            break
        }
    }
}

if (-not $profileItem) {
    Write-Host "ERROR: Could not find profile ListItem for hieuck.adobe2022@gmail.com"
    exit 1
}

Write-Host "Found profile ListItem, attempting to select..."

# Try SelectionItemPattern
$selectionPattern = $profileItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
if ($selectionPattern) {
    $selectionPattern.Select()
    Write-Host "Selected profile via SelectionItemPattern"
} else {
    Write-Host "SelectionItemPattern not available, trying click..."
    $clickablePattern = $profileItem.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    if ($clickablePattern) {
        $clickablePattern.Invoke()
        Write-Host "Invoked profile item"
    } else {
        Write-Host "No SelectionItemPattern or InvokePattern on profile item"
    }
}

Start-Sleep -Seconds 3

# Verify selection via StatusText
$statusText = $mainWindow.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "StatusText")
)
if ($statusText) {
    $statusPattern = $statusText.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if ($statusPattern) {
        Write-Host "StatusText: $($statusPattern.Current.Value)"
    } else {
        Write-Host "StatusText Name: $($statusText.Current.Name)"
    }
}

# Now find the "Tự động thêm" button for Agnes
Write-Host ""
Write-Host "=== Looking for Agnes 'Tự động thêm' button ==="

$allElements = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Find Agnes card container first
$agnesCard = $null
foreach ($elem in $allElements) {
    $name = $elem.Current.Name
    if ($name -and $name -eq "Agnes (88)") {
        # Check if this is the provider card (not the button)
        if ($elem.Current.ControlType -ne [System.Windows.Automation.ControlType]::Button) {
            $agnesCard = $elem
            Write-Host "Found Agnes card: Name='$name', ControlType=$($elem.Current.ControlType)"
            break
        }
    }
}

if (-not $agnesCard) {
    Write-Host "Agnes card not found by name 'Agnes (88)', trying 'Agnes AI'..."
    foreach ($elem in $allElements) {
        $name = $elem.Current.Name
        if ($name -and $name -eq "Agnes AI") {
            if ($elem.Current.ControlType -ne [System.Windows.Automation.ControlType]::Button) {
                $agnesCard = $elem
                Write-Host "Found Agnes card via 'Agnes AI': Name='$name', ControlType=$($elem.Current.ControlType)"
                break
            }
        }
    }
}

if (-not $agnesCard) {
    Write-Host "ERROR: Agnes card not found"
    exit 1
}

# Now find "Tự động thêm" button within Agnes card
$autoAddButton = $null
$agnesDescendants = $agnesCard.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($elem in $agnesDescendants) {
    $name = $elem.Current.Name
    if ($name -and $name -eq "Tự động thêm") {
        if ($elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
            $autoAddButton = $elem
            Write-Host "Found 'Tự động thêm' button: IsEnabled=$($elem.Current.IsEnabled), AutomationId='$($elem.Current.AutomationId)'"
            break
        }
    }
}

if (-not $autoAddButton) {
    Write-Host "ERROR: 'Tự động thêm' button not found within Agnes card"
    # Dump all buttons in Agnes card
    Write-Host "Buttons in Agnes card:"
    foreach ($elem in $agnesDescendants) {
        if ($elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
            Write-Host ("  Name='{0}', IsEnabled={1}, AutomationId='{2}'" -f $elem.Current.Name, $elem.Current.IsEnabled, $elem.Current.AutomationId)
        }
    }
    exit 1
}

# Check IsEnabled
if (-not $autoAddButton.Current.IsEnabled) {
    Write-Host "ERROR: 'Tự động thêm' button is DISABLED"
    exit 1
}

Write-Host "Button is ENABLED, invoking..."

$invokePattern = $autoAddButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
if ($invokePattern) {
    $invokePattern.Invoke()
    Write-Host "Invoked 'Tự động thêm' button"
} else {
    Write-Host "ERROR: InvokePattern not available on button"
    exit 1
}

Write-Host ""
Write-Host "=== Monitoring for 300 seconds (5 min) ==="
Write-Host "Checking StatusText every 5s..."

for ($i = 1; $i -le 60; $i++) {
    Start-Sleep -Seconds 5
    
    $statusText = $mainWindow.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "StatusText")
    )
    if ($statusText) {
        $statusPattern = $statusText.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        if ($statusPattern) {
            $status = $statusPattern.Current.Value
            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Status: $status"
            if ($status -like "*thành công*" -or $status -like "*success*" -or $status -like "*đã kết nối*" -or $status -like "*connected*") {
                Write-Host "SUCCESS: Connection appears to be established!"
                break
            }
            if ($status -like "*lỗi*" -or $status -like "*error*" -or $status -like "*thất bại*" -or $status -like "*failed*") {
                Write-Host "FAILURE: Error detected in status"
                break
            }
        }
    }
}

Write-Host "Done monitoring"