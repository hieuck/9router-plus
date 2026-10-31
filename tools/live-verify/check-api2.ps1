try { 
    $response = Invoke-RestMethod -Uri "http://localhost:20128/api/providers" -Headers @{ "X-API-Key" = "123456" } -Method Get
    Write-Host ($response | ConvertTo-Json -Depth 5)
} catch { 
    Write-Host ("API check failed: " + $_.Exception.Message)
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        Write-Host ($reader.ReadToEnd())
    }
}