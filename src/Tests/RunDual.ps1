param([ValidateSet('all','core','extra','outing','gun','death','join','phone','machines','overflow','features')][string]$Suite='all',[string]$GamePath=$env:PROBABLY_STOLEN_PATH)
$ErrorActionPreference='Stop'
if($Suite -eq 'all'){ foreach($part in @('core','gun','death','phone','outing','join','extra','overflow','machines','features')){ & $PSCommandPath -Suite $part -GamePath $GamePath }; return }
$project=Split-Path $PSScriptRoot -Parent
if(-not $GamePath){$GamePath='D:\SteamLibrary\steamapps\common\Probably Stolen'}
$game=[IO.Path]::GetFullPath($GamePath)
$workshop=Join-Path (Split-Path (Split-Path $game -Parent) -Parent) 'workshop\content\4348910\3837477869'
if(Get-Process 'Probably Stolen' -ErrorAction SilentlyContinue){throw 'Close the game before running the isolated test.'}
if(Test-Path -LiteralPath (Join-Path $game 'Mods\ShopTogether\manifest.xml')){throw 'A local ShopTogether copy would override Workshop. Move it outside Mods before testing.'}
$workshopDll=Join-Path $workshop 'ShopTogether.dll'
$workshopManifest=Join-Path $workshop 'manifest.xml'
$originalDll=[IO.File]::ReadAllBytes($workshopDll)
$originalManifest=[IO.File]::ReadAllBytes($workshopManifest)
$scratchRoot=[IO.Path]::GetFullPath((Join-Path (Split-Path $project -Parent) 'work'))
$scratch=Join-Path $scratchRoot ('runtime-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$probeDll=Join-Path $scratch 'ShopTogether.dll'
try { & (Join-Path $project 'Build.ps1') -Probe -OutputPath $probeDll -GamePath $game }
catch { $resolved=[IO.Path]::GetFullPath($scratch); if($resolved.StartsWith($scratchRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){Remove-Item -LiteralPath $resolved -Recurse -Force}; throw }
function Read-TestLog([string]$path){
    if(-not(Test-Path -LiteralPath $path)){return ''}
    $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete));$reader=[IO.StreamReader]::new($stream);try{return $reader.ReadToEnd()}finally{$reader.Dispose();$stream.Dispose()}
}
function Start-TestGame([string]$role,[string[]]$extra){
    $log=Join-Path $scratch ("smc-$role.log");if(Test-Path -LiteralPath $log){Remove-Item -LiteralPath $log -Force}
    $arguments=@('-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-logFile',('"'+$log+'"'),'--smc-role',$role)+$(if($Suite -ne 'core'){@('--smc-suite',$Suite)}else{@()})+$extra
    return Start-Process -FilePath (Join-Path $game 'Probably Stolen.exe') -WorkingDirectory $game -Environment @{SteamAppId='4348910';SteamGameId='4348910'} -ArgumentList $arguments -WindowStyle Hidden -PassThru
}
$started=@();$hostText='';$clientText=''
try{
[IO.File]::WriteAllBytes($workshopDll,[IO.File]::ReadAllBytes($probeDll))
[IO.File]::WriteAllBytes($workshopManifest,[IO.File]::ReadAllBytes((Join-Path $project 'manifest.xml')))
    $hostProcess=Start-TestGame 'host' @();$started+=,$hostProcess
    $deadline=[DateTime]::UtcNow.AddSeconds(120)
    do{Start-Sleep -Milliseconds 1000;$hostText=Read-TestLog (Join-Path $scratch 'smc-host.log');if($hostText -match '\[SMC FAIL\]|Init failed: 多人|Trust check failed:'){throw 'Host bootstrap failed'}}while($hostText -notmatch '\[SMC CONTROL\] (\d+)' -and -not $hostProcess.HasExited -and [DateTime]::UtcNow -lt $deadline)
    $controlMatch=[regex]::Match($hostText,'\[SMC CONTROL\] (\d+)');$inviteMatch=[regex]::Match($hostText,'\[SMC-INTERNAL-INVITE\] (SMC1-[A-Za-z0-9_-]+)')
    if(-not $controlMatch.Success -or -not $inviteMatch.Success){throw 'Host did not produce a test invitation'}
    $clientProcess=Start-TestGame 'client' @('--smc-control',$controlMatch.Groups[1].Value,'--smc-invite',$inviteMatch.Groups[1].Value);$started+=,$clientProcess
    $deadline=[DateTime]::UtcNow.AddSeconds(600)
    do{
        Start-Sleep -Milliseconds 1000;$hostText=Read-TestLog (Join-Path $scratch 'smc-host.log');$clientText=Read-TestLog (Join-Path $scratch 'smc-client.log')
        if(($hostText+$clientText) -match '\[SMC FAIL\]|Init failed: 多人|Trust check failed:'){throw 'Dual runtime assertion failed'}
        if($hostText -match '\[SMC COMPLETE\]' -and $clientText -match '\[SMC COMPLETE\]'){break}
    }while([DateTime]::UtcNow -lt $deadline -and -not($hostProcess.HasExited -and $clientProcess.HasExited))
    if($hostText -notmatch '\[SMC COMPLETE\]' -or $clientText -notmatch '\[SMC COMPLETE\]'){throw 'Dual runtime timed out'}
    foreach($testProcess in $started){[void]$testProcess.WaitForExit(10000)}
}finally{
    foreach($role in @('host','client')){Write-Output "Native ${role}:";$text=Read-TestLog (Join-Path $scratch "smc-$role.log");$text -split '\r?\n'|Where-Object {$_ -match '\[SMC (PASS|FAIL|COMPLETE|ROUTER)|Init failed: 多人|Trust check failed:'};$text -split '\r?\n'|Select-String -Pattern '\[SMC FAIL\]|Patching exception' -Context 0,4|ForEach-Object ToString}
    foreach($testProcess in $started){$children=Get-CimInstance Win32_Process|Where-Object {$_.ParentProcessId -eq $testProcess.Id}|Select-Object -ExpandProperty ProcessId;if(-not $testProcess.HasExited){Stop-Process -Id $testProcess.Id -ErrorAction SilentlyContinue; [void]$testProcess.WaitForExit(10000)};foreach($child in $children){Stop-Process -Id $child -ErrorAction SilentlyContinue}}
    [IO.File]::WriteAllBytes($workshopDll,$originalDll)
    [IO.File]::WriteAllBytes($workshopManifest,$originalManifest)
    $resolved=[IO.Path]::GetFullPath($scratch); if($resolved.StartsWith($scratchRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){Remove-Item -LiteralPath $resolved -Recurse -Force}
}