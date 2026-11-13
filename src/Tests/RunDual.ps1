param(
    [ValidateSet('all','core','extra','outing','gun','death','join','phone','machines','overflow','features','panels','clock','customers','replica','overnight','lottery','recovery','throttled','ui1319','guard1319','deals1320','boundary1320','release1321','hint1322','dump1322','return1323','session1323','immediate1324','numeric1326','probably_compat')]
    [string[]]$Suite=@('all'),[ValidateRange(1,100000)][int]$UploadKbps=1000,
    [string]$GamePath=$env:PROBABLY_STOLEN_PATH
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'TestProfile.ps1')
$project=Split-Path $PSScriptRoot -Parent
if(-not $GamePath){$GamePath='D:\SteamLibrary\steamapps\common\Probably Stolen'}
$game=[IO.Path]::GetFullPath($GamePath)
$workshop=Join-Path (Split-Path (Split-Path $game -Parent) -Parent) 'workshop\content\4348910\3837477869'
if(Get-Process 'Probably Stolen' -ErrorAction SilentlyContinue){throw 'Close the game before running the isolated test.'}
if($Suite -contains 'all'){$Suite=@('immediate1324','return1323','overflow','session1323','hint1322','dump1322','core','gun','death','phone','outing','join','extra','machines','features','panels','clock','customers','replica','overnight','lottery','recovery','throttled','ui1319','guard1319','deals1320','boundary1320','release1321')}
$workshopDll=Join-Path $workshop 'ShopTogether.dll'
$workshopHash=(Get-FileHash -Algorithm SHA256 -LiteralPath $workshopDll).Hash
$scratchRoot=[IO.Path]::GetFullPath((Join-Path (Split-Path $project -Parent) 'work'))
$scratch=Join-Path $scratchRoot ('runtime-'+[Guid]::NewGuid().ToString('N'))
$isolated=Join-Path $scratch 'Game'
$probeFolder=Join-Path $isolated 'ProbeWorkshop'
New-Item -ItemType Directory -Path $scratch -Force | Out-Null

function Read-TestLog([string]$path){
    if(-not(Test-Path -LiteralPath $path)){return ''}
    $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    $reader=[IO.StreamReader]::new($stream)
    try{return $reader.ReadToEnd()}finally{$reader.Dispose();$stream.Dispose()}
}
function Start-TestGame([string]$role,[string]$part,[string[]]$extra){
    $log=Join-Path $scratch ("smc-$role.log")
    $profile=Join-Path $scratch ("profile-$role")
    if(-not(Test-Path -LiteralPath $profile)){New-IsolatedTestProfile $profile}
    $env:SHOPTOGETHER_TEST_PERSISTENT_PATH=$profile
    $arguments=@('-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-logFile',('"'+$log+'"'),'--smc-role',$role)+$(if($part -ne 'core'){@('--smc-suite',$part)}else{@()})+$extra
    # The child starts with the private desktop explicitly assigned; there is no visible-desktop fallback.
    return $privateDesktop.Start((Join-Path $isolated 'Probably Stolen.exe'),($arguments -join ' '),$isolated)
}
function Stop-TestProcesses($processes){
    foreach($process in $processes){
        $children=@(Get-CimInstance Win32_Process | Where-Object {$_.ParentProcessId -eq $process.Id} | Select-Object -ExpandProperty ProcessId)
        if(-not $process.HasExited){Stop-Process -Id $process.Id -ErrorAction SilentlyContinue;[void]$process.WaitForExit(10000)}
        foreach($child in $children){Stop-Process -Id $child -ErrorAction SilentlyContinue}
    }
}
$started=@()
$privateDesktop=$null
try{
    & (Join-Path $PSScriptRoot 'PrepareIsolatedGame.ps1') -GamePath $game -Target $isolated
    & (Join-Path $project 'Build.ps1') -Probe -OutputPath (Join-Path $probeFolder 'ShopTogether.dll') -GamePath $game
    Copy-Item -LiteralPath (Join-Path $project 'manifest.xml') -Destination $probeFolder
    Add-Type -Path (Join-Path $PSScriptRoot 'PrivateDesktop.cs')
    $privateDesktop=[ShopTogetherTestDesktop]::new()
    $env:SteamAppId='4348910';$env:SteamGameId='4348910';$env:SHOPTOGETHER_PROBE_FOLDER=$probeFolder
    Write-Output 'PRIVATE_DESKTOP_READY'
    foreach($part in $Suite){
        $started=@();$hostText='';$clientText=''
        foreach($role in @('host','client')){$log=Join-Path $scratch ("smc-$role.log");if(Test-Path -LiteralPath $log){Remove-Item -LiteralPath $log -Force}}
        try{
            $hostExtra=if($part -eq 'throttled'){@('--smc-throttle',"$UploadKbps")}else{@()}
            $hostProcess=Start-TestGame 'host' $part $hostExtra;$started+=,$hostProcess
            $deadline=[DateTime]::UtcNow.AddSeconds(150)
            do{Start-Sleep -Milliseconds 1000;$hostText=Read-TestLog (Join-Path $scratch 'smc-host.log');if($hostText -match '\[SMC FAIL\]|Init failed: 多人|Trust check failed:'){throw 'Host bootstrap failed'}}while($hostText -notmatch '\[SMC CONTROL\] (\d+)' -and -not $hostProcess.HasExited -and [DateTime]::UtcNow -lt $deadline)
            $control=[regex]::Match($hostText,'\[SMC CONTROL\] (\d+)');$invite=[regex]::Match($hostText,'\[SMC-INTERNAL-INVITE\] (SMC1-[A-Za-z0-9_-]+)')
            if(-not $control.Success -or -not $invite.Success){throw 'Host did not produce a test invitation'}
            $clientProcess=Start-TestGame 'client' $part @('--smc-control',$control.Groups[1].Value,'--smc-invite',$invite.Groups[1].Value);$started+=,$clientProcess
            $deadline=[DateTime]::UtcNow.AddSeconds(600)
            do{
                Start-Sleep -Milliseconds 1000;$hostText=Read-TestLog (Join-Path $scratch 'smc-host.log');$clientText=Read-TestLog (Join-Path $scratch 'smc-client.log')
                if(($hostText+$clientText) -match '\[SMC FAIL\]|Init failed: 多人|Trust check failed:'){throw 'Dual runtime assertion failed'}
                if($hostText -match '\[SMC COMPLETE\]' -and $clientText -match '\[SMC COMPLETE\]'){break}
            }while([DateTime]::UtcNow -lt $deadline -and -not($hostProcess.HasExited -and $clientProcess.HasExited))
            if($hostText -notmatch '\[SMC COMPLETE\]' -or $clientText -notmatch '\[SMC COMPLETE\]'){throw 'Dual runtime timed out'}
            foreach($process in $started){[void]$process.WaitForExit(10000)}
            if($part -eq 'dump1322'){
                $expected=[regex]::Match($hostText,'\[SMC-SAVE-EXPECTED\] waste=(\d+) food=(\d+)');$slot=Join-Path $scratch 'profile-host\save_-777.es3'
                if(-not $expected.Success -or -not(Test-Path -LiteralPath $slot)){throw 'Isolated game did not write the requested save'}
                $saved=[IO.File]::ReadAllText($slot);$waste=[regex]::Match($saved,'"nuclearWasteDumped"\s*:\s*(\d+)');$food=[regex]::Match($saved,'"serpicoFoodKcal"\s*:\s*(\d+)')
                if(-not $waste.Success -or -not $food.Success -or $waste.Groups[1].Value -ne $expected.Groups[1].Value -or $food.Groups[1].Value -ne $expected.Groups[2].Value){throw 'Outing results did not persist in the isolated save'}
                Write-Output 'ISOLATED_SAVE_VERIFIED waste and food totals'
            }
        }finally{
            foreach($role in @('host','client')){
                Write-Output "Native ${part} ${role}:";$text=Read-TestLog (Join-Path $scratch "smc-$role.log")
                if($part -eq 'hint1322' -and $env:SMC_HINT_EVIDENCE_DIR){$picture=[regex]::Match($text,'\[SMC-HINT-IMAGE\] ([A-Za-z0-9+/=]+)');if($picture.Success){[IO.File]::WriteAllBytes((Join-Path $env:SMC_HINT_EVIDENCE_DIR "$role.png"),[Convert]::FromBase64String($picture.Groups[1].Value));Write-Output "HINT_IMAGE_SAVED $role"}}
                $text -split '\r?\n' | Where-Object {$_ -match '\[SMC (PASS|FAIL|COMPLETE|ROUTER|PERF)|Init failed: 多人|Trust check failed:|\[ModLoader\].*shop_together|(?i)friend .*reported:.*(fountain click|click pause|drag end|layout|load)'}
                $text -split '\r?\n' | Select-String -Pattern '\[SMC FAIL\]|Patching exception' -Context 0,4 | ForEach-Object ToString
            }
            Stop-TestProcesses $started;$started=@()
        }
    }
}catch{
    Write-Output ('ISOLATED_TEST_ERROR: '+$_.Exception.Message)
    throw
}finally{
    Stop-TestProcesses $started
    if($null -ne $privateDesktop){$privateDesktop.Dispose()}
    $resolved=[IO.Path]::GetFullPath($scratch)
    if(-not $resolved.StartsWith($scratchRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Refusing cleanup outside the test workspace'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
    if((Get-FileHash -Algorithm SHA256 -LiteralPath $workshopDll).Hash -ne $workshopHash){throw 'The subscribed Workshop DLL changed during the isolated test'}
    'WORKSHOP_DLL_UNCHANGED_BY_TEST'
}
