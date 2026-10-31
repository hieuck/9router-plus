Add-Type -AssemblyName UIAutomationClient
$proc = Get-Process -Name "RouterPlus" | Select-Object -First 1
if ($proc) {
    $element = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    $allElements = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    
    # Find all ProfileRowViewModel items
    for ($i = 0; $i -lt $allElements.Count; $i++) {
        $el = $allElements[$i]
        if ($el.Current.Name -eq "RouterPlus.App.ViewModels.ProfileRowViewModel" -and $el.Current.ControlType.ProgrammaticName -eq "ControlType.ListItem") {
            # Get children of this ListItem
            $children = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
            $email = ""
            $agnesStatus = ""
            for ($j = 0; $j -lt $children.Count; $j++) {
                $ch = $children[$j]
                if ($ch.Current.ControlType.ProgrammaticName -eq "ControlType.Text" -and $ch.Current.Name -like "*@*") {
                    $email = $ch.Current.Name
                }
                if ($ch.Current.Name -like "*Agnes*" -or $ch.Current.Name -like "*agnes*") {
                    $agnesStatus = $ch.Current.Name
                }
            }
            if ($email) {
                Write-Output ("Profile: {0} | Agnes: {1}" -f $email, $agnesStatus)
            }
        }
    }
} else {
    Write-Output "Process not found"
}