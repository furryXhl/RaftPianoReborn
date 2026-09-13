param()

$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot 'Build-Native.cmd')
if ($LASTEXITCODE -ne 0) {
    throw "Native build failed with exit code $LASTEXITCODE."
}

& (Join-Path $PSScriptRoot 'Build-Package.ps1')
