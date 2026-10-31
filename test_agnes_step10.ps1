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
}

# Press Enter to trigger search
Add-Type -MemberDefinition @"
    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);
"@ -Name "Win32" -Namespace "Native"

Start-Sleep -Milliseconds 500
# Press Enter (VK_RETURN = 0x0D)
[Native.Win32]::keybd_event(0x0D, 0, 0, 0)
[Native.Win32]::keybd_event(0x0D, 0, 2, 0)  # KEYEVENTF_KEYUP = 2
Write-Host "Pressed Enter"
Start-Sleep -Seconds 3

# Dump the ProfileList children
$profileList = $mainWindow.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList")
)

if ($profileList) {
    Write-Host "Found ProfileList"
    $children = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    Write-Host "ProfileList children count: $($children.Count)"
    foreach ($child in $children) {
        Write-Host ("  Child: Name='{0}', ControlType={1}, AutomationId='{2}'" -f $child.Current.Name, $child.Current.ControlType, $child.Current.AutomationId)
        $grandChildren = $child.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($gc in $grandChildren) {
            Write-Host ("    Grandchild: Name='{0}', ControlType={1}'" -f $gc.Current.Name, $gc.Current.ControlType)
        }
    }
} else {
    Write-Host "ProfileList not found"
}

# Check all elements for profile items
$allElements = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

Write-Host "=== All ListItems ==="
foreach ($elem in $allElements) {
    if ($elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) {
        Write-Host ("  ListItem: Name='{0}', AutomationId='{1}'" -f $elem.Current.Name, $elem.Current.AutomationId)
    }
}

Write-Host "=== Elements with 'hieuck' or 'adobe' ==="
foreach ($elem in $allElements) {
    $name = $elem.Current.Name
    if ($name -and ($name -like "*hieuck*" -or $name -like "*adobe*")) {
        Write-Host ("  Name='{0}', ControlType={1}'" -f $name, $elem.Current.ControlType)
    }
}

Write-Host "Done"