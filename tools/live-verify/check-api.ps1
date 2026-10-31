try {
    $response = Invoke-RestMethod -Uri "http://localhost:20128/api/providers" -Headers @{ "Authorization" = "Bearer 123456" } -Method Get
    $agnesConns = $response | Where-Object { $_.provider -eq "Agnes" -and $_.name -eq "hieuck.it@gmail.com" }
    if ($agnesConns) {
        Write-Host "Agnes connection found for hieuck.it@gmail.com:"
        $agnesConns | ForEach-Object {
            Write-Host ("  Id: " + $_.id)
            Write-Host ("  Name: " + $_.name)
            Write-Host ("  CreatedAt: " + $_.createdAt)
            Write-Host ("  IsActive: " + $_.isActive)
            Write-Host ("  TestStatus: " + $_.testStatus)
        }
    } else {
        Write-Host "No Agnes connection found for hieuck.it@gmail.com"
    }
} catch {
    Write-Host ("API check failed: " + $_.Exception.Message)
}