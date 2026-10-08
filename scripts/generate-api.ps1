# Regenerates the OpenAPI contract snapshot (openapi/openapi.json) and the typed frontend schema
# (web/src/api/generated/schema.ts). Run after every endpoint or DTO change.
# The API runs on a private port with a temporary data directory, so it never touches real data or the old app.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$port = 5099
$data = Join-Path ([System.IO.Path]::GetTempPath()) "timingapp-openapi-$([guid]::NewGuid().ToString('N'))"
$url = "http://127.0.0.1:$port"

dotnet build (Join-Path $root 'src\TimingApp.Api') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:Kestrel__Endpoints__Http__Url = $url
$env:TimingApp__Storage__DataDirectory = $data
$process = Start-Process dotnet -ArgumentList 'run', '--no-build', '--no-launch-profile', '--project', (Join-Path $root 'src\TimingApp.Api') -PassThru -WindowStyle Hidden
try {
    $deadline = (Get-Date).AddSeconds(60)
    while ($true) {
        try {
            Invoke-WebRequest "$url/health" -UseBasicParsing -TimeoutSec 2 | Out-Null
            break
        }
        catch {
            if ((Get-Date) -gt $deadline) { throw 'API did not start.' }
            Start-Sleep -Milliseconds 500
        }
    }
    $json = (Invoke-WebRequest "$url/openapi/v1.json" -UseBasicParsing).Content
    $target = Join-Path $root 'openapi\openapi.json'
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    [System.IO.File]::WriteAllText($target, $json, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Wrote $target"
}
finally {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    Get-CimInstance Win32_Process -Filter "ParentProcessId = $($process.Id)" -ErrorAction SilentlyContinue |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Remove-Item Env:ASPNETCORE_ENVIRONMENT, Env:Kestrel__Endpoints__Http__Url, Env:TimingApp__Storage__DataDirectory -ErrorAction SilentlyContinue
    Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue
}

Push-Location (Join-Path $root 'web')
try {
    npx openapi-typescript ../openapi/openapi.json -o src/api/generated/schema.ts
    if ($LASTEXITCODE -ne 0) { throw 'openapi-typescript failed.' }
}
finally {
    Pop-Location
}
