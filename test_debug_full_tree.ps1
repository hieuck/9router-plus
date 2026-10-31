Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
Write-Host "Total elements: " $all.Count

# Dump everything with Agnes
for ($i = 0; $i -lt $all.Count; $i++) {
    $el = $all[$i]
    $name = $el.Current.Name
    $autoId = $el.Current.AutomationId
    $controlType = $el.Current.ControlType.ProgrammaticName
    $isEnabled = $el.Current.IsEnabled
    $className = $el.Current.ClassName
    
    if ($name -like "*Agnes*") {
        Write-Host "[$i] Name='$name' AutoId='$autoId' Type='$controlType' Enabled=$isEnabled Class='$className'"
        # Show parent chain
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($el)
        for ($level = 0; $level -lt 10 -and $parent; $level++) {
            Write-Host ("  Parent {0}: Name='{1}' Class='{2}' Type='{3}'" -f $level, $parent.Current.Name, $parent.Current.ClassName, $parent.Current.ControlType)
            $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
        }
    }
}

Write-Host "Done"