param([ValidateSet(4,6)][int]$Players=4,[switch]$PerformanceOnly,[Parameter(Mandatory)][string]$SavePath,[string]$GamePath='D:\SteamLibrary\steamapps\common\Probably Stolen')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'TestProfile.ps1')
if(Get-Process 'Probably Stolen' -ErrorAction SilentlyContinue){throw 'Close game before isolated group test'}
$project=Split-Path $PSScriptRoot -Parent
$game=[IO.Path]::GetFullPath($GamePath)
$workshop=Join-Path (Split-Path (Split-Path $game -Parent) -Parent) 'workshop\content\4348910\3837477869'
$dll=Join-Path $workshop 'ShopTogether.dll';$manifest=Join-Path $workshop 'manifest.xml'
$workshopHash=(Get-FileHash -Algorithm SHA256 -LiteralPath $dll).Hash
$manifestHash=(Get-FileHash -Algorithm SHA256 -LiteralPath $manifest).Hash
$saveHash=(Get-FileHash -LiteralPath $SavePath).Hash
$scratchRoot=[IO.Path]::GetFullPath((Join-Path (Split-Path $project -Parent) 'work'))
$scratch=Join-Path $scratchRoot ('group-'+[Guid]::NewGuid().ToString('N'))
$isolated=Join-Path $scratch 'Game';$probeFolder=Join-Path $isolated 'ProbeWorkshop'
New-Item -ItemType Directory -Path $scratch | Out-Null
$started=@();$privateDesktop=$null
function ReadLog([int]$i){$p=Join-Path $scratch "player-$i.log";if(-not(Test-Path -LiteralPath $p)){return ''};$stream=[IO.File]::Open($p,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete));$reader=[IO.StreamReader]::new($stream);try{return $reader.ReadToEnd()}finally{$reader.Dispose();$stream.Dispose()}}
function Launch([int]$i,[string[]]$extra){$log=Join-Path $scratch "player-$i.log";$profile=Join-Path $scratch "profile-$i";New-IsolatedTestProfile $profile;$env:SHOPTOGETHER_TEST_PERSISTENT_PATH=$profile;$a=@('-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-logFile',('"'+$log+'"'),'--smc-role',$(if($i -eq 0){'host'}else{'client'}),'--smc-group',$Players,'--smc-index',$i,'--smc-performance',$(if($PerformanceOnly){'1'}else{'0'}))+$extra;return $privateDesktop.Start((Join-Path $isolated 'Probably Stolen.exe'),($a -join ' '),$isolated)}
try{
 $copy=Join-Path $scratch 'day70.es3';Copy-Item -LiteralPath $SavePath -Destination $copy
 & (Join-Path $PSScriptRoot 'PrepareIsolatedGame.ps1') -GamePath $game -Target $isolated
 & (Join-Path $project 'Build.ps1') -Probe -OutputPath (Join-Path $probeFolder 'ShopTogether.dll') -GamePath $game
 Copy-Item -LiteralPath (Join-Path $project 'manifest.xml') -Destination $probeFolder
 Add-Type -Path (Join-Path $PSScriptRoot 'PrivateDesktop.cs')
 $privateDesktop=[ShopTogetherTestDesktop]::new()
 $env:SteamAppId='4348910';$env:SteamGameId='4348910';$env:SHOPTOGETHER_PROBE_FOLDER=$probeFolder
 Write-Output 'PRIVATE_DESKTOP_READY'
 $started+=Launch 0 @('--smc-save',('"'+$copy+'"'))
 $deadline=[DateTime]::UtcNow.AddSeconds(240)
 do{Start-Sleep -Milliseconds 1000;$text=ReadLog 0;if($text -match '\[SMC FAIL\]|Trust check failed:'){throw 'Group host bootstrap failed'}}while($text -notmatch '\[SMC CONTROL\] (\d+)' -and [DateTime]::UtcNow -lt $deadline)
 $port=[regex]::Match($text,'\[SMC CONTROL\] (\d+)');$invite=[regex]::Match($text,'\[SMC-INTERNAL-INVITE\] (SMC1-[A-Za-z0-9_-]+)');if(-not $port.Success -or -not $invite.Success){throw 'Group host invitation missing'}
 for($i=1;$i -lt $Players;$i++){$started+=Launch $i @('--smc-control',$port.Groups[1].Value,'--smc-invite',$invite.Groups[1].Value);$joinDeadline=[DateTime]::UtcNow.AddSeconds(180);do{Start-Sleep -Milliseconds 1000;$joined=ReadLog $i;if($joined -match '\[SMC FAIL\]'){throw "Guest $i bootstrap failed"}}while($joined -notmatch 'group client received day70 world' -and [DateTime]::UtcNow -lt $joinDeadline);if($joined -notmatch 'group client received day70 world'){throw "Guest $i did not join"}}
 $deadline=[DateTime]::UtcNow.AddSeconds(600)
 do{Start-Sleep -Milliseconds 1000;$complete=0;for($i=0;$i -lt $Players;$i++){$text=ReadLog $i;if($text -match '\[SMC FAIL\]|Trust check failed:'){throw "Group player $i failed"};if($text -match '\[SMC COMPLETE\]'){$complete++}}}while($complete -lt $Players -and [DateTime]::UtcNow -lt $deadline)
 if($complete -ne $Players){throw 'Group test timed out'}
}finally{
 for($i=0;$i -lt $Players;$i++){(ReadLog $i) -split '\r?\n' | Where-Object {$_ -match '\[SMC (PASS|FAIL|COMPLETE|PERF)\]'} | ForEach-Object {"Player ${i}: $_"}}
 foreach($p in $started){$children=Get-CimInstance Win32_Process|Where-Object ParentProcessId -eq $p.Id|Select-Object -ExpandProperty ProcessId;if(-not $p.HasExited){Stop-Process -Id $p.Id -ErrorAction SilentlyContinue;[void]$p.WaitForExit(10000)};foreach($child in $children){Stop-Process -Id $child -ErrorAction SilentlyContinue}}
 if($null -ne $privateDesktop){$privateDesktop.Dispose()}
 $resolved=[IO.Path]::GetFullPath($scratch)
 if(-not $resolved.StartsWith($scratchRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Refusing cleanup outside the test workspace'}
 if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
 if((Get-FileHash -LiteralPath $SavePath).Hash -ne $saveHash){throw 'Original save changed during isolated testing'}
 if((Get-FileHash -LiteralPath $dll).Hash -ne $workshopHash -or (Get-FileHash -LiteralPath $manifest).Hash -ne $manifestHash){throw 'Workshop files changed during isolated testing'}
 Write-Output 'ORIGINAL_SAVE_AND_WORKSHOP_UNCHANGED'
}
