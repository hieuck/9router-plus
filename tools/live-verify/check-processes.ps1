$procs = Get-Process -Name 'RouterPlus'
Write-Host "RouterPlus processes: $($procs.Count)"
foreach ($p in $procs) {
    Write-Host "  PID=$($p.Id), MainWindowHandle=$($p.MainWindowHandle), MainWindowTitle='$($p.MainWindowTitle)'"
}