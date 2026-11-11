param([string]$GamePath=$env:PROBABLY_STOLEN_PATH)
# The safe paired runner includes native bootstrap, isolated saves and authority checks.
& (Join-Path $PSScriptRoot 'RunDual.ps1') -Suite core -GamePath $GamePath