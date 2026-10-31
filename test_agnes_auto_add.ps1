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

# Find the profile in the list - need to find the list item containing the email
$profileList = $mainWindow.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList")
)

if (-not $profileList) {
    Write-Host "ProfileList not found, trying alternative search"
    # Try finding by name/class
    $allItems = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($item in $allItems) {
        if ($item.Current.Name -like "*hieuck.adobe2022@gmail.com*" -or $item.Current.Name -like "*adobe2022*") {
            Write-Host "Found profile item by name: $($item.Current.Name)"
            $profileList = $item
            break
        }
    }
}

if (-not $profileList) {
    Write-Host "Could not find profile list or item"
    exit 1
}

# Find the list item for the profile
$listItem = $profileList.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, "hieuck.adobe2022@gmail.com")
)

if (-not $listItem) {
    Write-Host "Looking for profile item with partial match..."
    $allItems = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($item in $allItems) {
        if ($item.Current.Name -like "*adobe2022*") {
            $listItem = $item
            Write-Host "Found profile: $($item.Current.Name)"
            break
        }
    }
}

if (-not $listItem) {
    Write-Host "Profile item not found"
    exit 1
}

# Click the profile item
$invokePattern = $listItem.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
if ($invokePattern) {
    $invokePattern.Invoke()
    Write-Host "Clicked profile item"
} else {
    $selectionPattern = $listItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($selectionPattern) {
        $selectionPattern.Select()
        Write-Host "Selected profile item via SelectionItemPattern"
    } else {
        Write-Host "No invoke/selection pattern on profile item"
    }
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
Write-Host "Searching for Agnes card..."

# Find the providers list/items
$allElements = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($elem in $allElements) {
    if ($elem.Current.Name -like "*Agnes*" -or $elem.Current.Name -like "*Agnes AI*") {
        Write-Host "Found Agnes element: $($elem.Current.Name) (AutomationId: $($elem.Current.AutomationId), Class: $($elem.Current.ClassName))"
    }
}

# Find the "Tự động thêm" button specifically for Agnes
$autoAddButton = $mainWindow.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, "Tự động thêm")
)

if (-not $autoAddButton) {
    Write-Host "Looking for 'Auto add' (English)..."
    $autoAddButton = $mainWindow.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, "Auto add")
    )
}

if ($autoAddButton) {
    Write-Host "Found 'Tự động thêm' button"
    Write-Host "  IsEnabled: $($autoAddButton.Current.IsEnabled)"
    Write-Host "  AutomationId: $($autoAddButton.Current.AutomationId)"
    Write-Host "  ClassName: $($autoAddButton.Current.ClassName)"
    
    $invokePattern = $autoAddButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    if ($invokePattern -and $autoAddButton.Current.IsEnabled) {
        Write-Host "Invoking 'Tự động thêm' button..."
        $invokePattern.Invoke()
        Write-Host "Button invoked successfully"
    } else {
        Write-Host "Button not enabled or no invoke pattern"
    }
} else {
    Write-Host "Could not find 'Tự động thêm' button"
    # Dump all buttons
    Write-Host "All buttons found:"
    foreach ($elem in $allElements) {
        if ($elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
            Write-Host "  Button: Name='$($elem.Current.Name)', AutomationId='$($elem.Current.AutomationId)', Enabled=$($elem.Current.IsEnabled)"
        }
    }
}

Write-Host "Done"