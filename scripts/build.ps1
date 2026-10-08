# Local CI gate: frontend type check, lint, tests and production build (into src/TimingApp.Api/wwwroot),
# then backend build and tests, then end-to-end tests. Fails on the first broken step.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Step([string]$name, [scriptblock]$action) {
    Write-Host "==> $name" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -ne 0) { throw "$name failed (exit code $LASTEXITCODE)." }
}

Push-Location $root
try {
    Push-Location (Join-Path $root 'web')
    try {
        if (-not (Test-Path node_modules)) {
            Step 'npm ci' { npm ci }
        }
        Step 'vue-tsc' { npx vue-tsc --noEmit }
        Step 'eslint' { npx eslint . }
        Step 'vitest' { npx vitest run }
        Step 'vite build' { npx vite build }
    }
    finally {
        Pop-Location
    }

    # After the frontend build: the API's static web assets manifest must include the new wwwroot files.
    Step 'dotnet build' { dotnet build TimingApp.slnx --nologo }
    Step 'dotnet test' { dotnet test --solution TimingApp.slnx --no-build }

    Push-Location (Join-Path $root 'web')
    try {
        # Real backend with simulated cameras on a private port (see web/playwright.config.ts).
        Step 'playwright' { npx playwright test }
    }
    finally {
        Pop-Location
    }
    Write-Host 'Build OK' -ForegroundColor Green
}
finally {
    Pop-Location
}
