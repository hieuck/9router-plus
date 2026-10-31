Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Search for hieuck.it@gmail.com in main window
Write-Host "=== Searching for hieuck.it@gmail.com in main window ==="
foreach ($elem in $all) {
    $name = $elem.Current.Name
    if ($name -like "*hieuck.it@gmail.com*") {
        Write-Host "Found: Name='$name' Type='$($elem.Current.ControlType)' Class='$($elem.Current.ClassName)'"
        # Show parent chain
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($elem)
        for ($level = 0; $level -lt 10 -and $parent; $level++) {
            Write-Host ("  Parent {0}: Name='{1}' Class='{2}' Type='{3}'" -f $level, $parent.Current.Name, $parent.Current.ClassName, $parent.Current.ControlType)
            $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
        }
    }
}

Write-Host "Done"