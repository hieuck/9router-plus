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

# Find the profile list item for hieuck.adobe2022@gmail.com
$allElements = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$profileItem = $null
foreach ($elem in $allElements) {
    if ($elem.Current.Name -like "*hieuck.adobe2022@gmail.com*" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) {
        $profileItem = $elem
        Write-Host "Found profile list item: $($elem.Current.Name)"
        break
    }
}

if (-not $profileItem) {
    # Try finding by any element containing the email
    foreach ($elem in $allElements) {
        if ($elem.Current.Name -like "*adobe2022*") {
            $profileItem = $elem
            Write-Host "Found element with adobe2022: $($elem.Current.Name) (ControlType: $($elem.Current.ControlType))"
            break
        }
    }
}

if ($profileItem) {
    # Try SelectionItemPattern
    $selectionPattern = $profileItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($selectionPattern) {
        $selectionPattern.Select()
        Write-Host "Selected profile item via SelectionItemPattern"
    } else {
        # Try InvokePattern
        $invokePattern = $profileItem.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        if ($invokePattern) {
            $invokePattern.Invoke()
            Write-Host "Clicked profile item via InvokePattern"
        } else {
            # Try clicking via mouse
            $clickablePoint = $profileItem.GetClickablePoint()
            [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new([int]$clickablePoint.X, [int]$clickablePoint.Y)
            [System.Windows.Forms.Application]::DoEvents()
            [System.Windows.Forms.Mouse]::Click("Left")
            Write-Host "Clicked profile item via mouse"
        }
    }
} else {
    Write-Host "Could not find profile item"
}

Start-Sleep -Seconds 2

# Verify selection
$selectedProfileName = $mainWindow.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "SelectedProfileName")
)

if ($selectedProfileName) {
    Write-Host "Selected profile name: $($selectedProfileName.Current.Name)"
} else {
    Write-Host "Could not verify selected profile name"
}

# Now find Agnes card and its "Tự động thêm" button
Write-Host "Searching for 'Tự động thêm' button for Agnes..."

$autoAddButtons = @()
foreach ($elem in $allElements) {
    if ($elem.Current.Name -eq "Tự động thêm" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
        $autoAddButtons += $elem
    }
}

Write-Host "Found $($autoAddButtons.Count) 'Tự động thêm' buttons"

# We need the one associated with Agnes - let's find the Agnes provider card first
$agnesCard = $null
foreach ($elem in $allElements) {
    if ($elem.Current.Name -eq "Agnes (8)" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
        # This is the Agnes provider tab/button
        Write-Host "Found Agnes provider tab"
        # The 'Tự động thêm' should be in the same container - let's find it via tree traversal
    }
}

# Better approach: find all buttons and check their parent containers
foreach ($btn in $autoAddButtons) {
    $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($btn)
    $foundAgnes = $false
    for ($i = 0; $i -lt 10 -and $parent; $i++) {
        if ($parent.Current.Name -like "*Agnes*") {
            $foundAgnes = $true
            break
        }
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
    }
    if ($foundAgnes) {
        Write-Host "Found Agnes 'Tự động thêm' button!"
        Write-Host "  IsEnabled: $($btn.Current.IsEnabled)"
        Write-Host "  AutomationId: $($btn.Current.AutomationId)"
        
        $invokePattern = $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        if ($invokePattern -and $btn.Current.IsEnabled) {
            Write-Host "Invoking 'Tự động thêm' button..."
            $invokePattern.Invoke()
            Write-Host "Button invoked successfully"
        } else {
            Write-Host "Button not enabled or no invoke pattern"
        }
        break
    }
}

Write-Host "Done"