param([string]$OutputPath=(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'ShopTogether.dll'),[string]$GamePath=$env:PROBABLY_STOLEN_PATH)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
if(-not $GamePath){$GamePath='D:\SteamLibrary\steamapps\common\Probably Stolen'}
$managed=Join-Path $GamePath 'Probably Stolen_Data\Managed'
& (Join-Path $project 'Build.ps1') -OutputPath $OutputPath -GamePath $GamePath
$testRoot=Join-Path (Split-Path ([IO.Path]::GetFullPath($OutputPath)) -Parent) ('connection-check-'+[Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $testRoot)
try {
    $testExe=Join-Path $testRoot 'SecurityProbe.exe'
    $sources=@('AuthorityModel.cs','DirectConnection.cs','InviteCode.cs','RouterRules.cs','ProtocolGuard.cs','SecureChannel.cs','LargeTransfer.cs','MessageStream.cs') | ForEach-Object {Join-Path $project $_}
    $sources+=@(Join-Path $PSScriptRoot 'TransportSecurityTests.cs'; Join-Path $PSScriptRoot 'RoomSetupTests.cs')
    & 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /nowarn:0649 "/out:$testExe" "/r:$managed\Mono.Security.dll" $sources
    if($LASTEXITCODE -ne 0){throw 'Test harness compilation failed'}
    Copy-Item -LiteralPath "$managed\Mono.Security.dll" -Destination $testRoot
    # Run on .NET Framework (the same family as the game's Mono runtime); .NET 8 does not support Mono.Security's certificate code.
    & $testExe
    if($LASTEXITCODE -ne 0){throw 'Connection test suite failed'}
} finally {
    $parent=[IO.Path]::GetFullPath((Split-Path $OutputPath -Parent)).TrimEnd('\')+'\'
    $resolved=[IO.Path]::GetFullPath($testRoot)
    if(-not $resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase)){throw 'Refusing cleanup outside build directory'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
