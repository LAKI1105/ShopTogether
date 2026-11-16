param(
    [ValidateSet('perf1340','settlement1339','reported1339','release1339','outing138','completion138','authority137','equipment136','book136','overflow136','cargo135','interact135','custom135','audit135','authority134recipe','authority134','all','core','extra','gun','death','join','phone','machines','overflow','features','panels','clock','customers','replica','overnight','lottery','recovery','throttled','ui1319','guard1319','deals1320','boundary1320','release1321','hint1322','dump1322','return1323','session1323','immediate1324','numeric1326','layout1326','mouse1327','trade1327','stress1327','issue1328')]
    [string[]]$Suite=@('all'),[ValidateRange(1,100000)][int]$UploadKbps=1000,
    [string]$GamePath=$env:PROBABLY_STOLEN_PATH,[string]$ProductionDll,[string]$ProductionPackage,[switch]$ContinueOnFailure,[switch]$ScanOnly
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'TestProfile.ps1')
$project=Split-Path $PSScriptRoot -Parent
if(-not $GamePath){$GamePath='D:\SteamLibrary\steamapps\common\Probably Stolen'}
$game=[IO.Path]::GetFullPath($GamePath)
$workshop=Join-Path (Split-Path (Split-Path $game -Parent) -Parent) 'workshop\content\4348910\3837477869'
if(Get-Process 'Probably Stolen' -ErrorAction SilentlyContinue){throw 'Close the game before running the isolated test.'}
if($Suite -contains 'all'){$Suite=@('core','gun','death','phone','extra','machines','features','panels','clock','customers','replica','overnight','lottery','recovery','ui1319','guard1319','deals1320','release1321')}
$workshopDll=Join-Path $workshop 'ShopTogether.dll'
$workshopHash=(Get-FileHash -Algorithm SHA256 -LiteralPath $workshopDll).Hash
# Unity's bundled Mono fails to resolve assembly CodeBase under non-ASCII paths.
# The disposable runtime belongs in the user's temporary directory, not the project.
$scratchRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$scratch=Join-Path $scratchRoot ('runtime-'+[Guid]::NewGuid().ToString('N'))
$testPrefPrefix='shop_together.probe.'+[Guid]::NewGuid().ToString('N')
$prefRegistry='Software\Questing Goose Studio\Probably Stolen'
function Live-IssuePreference(){
    $snapshot=@{};$key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($prefRegistry,$false)
    if($null -eq $key){return $snapshot}
    try{foreach($name in $key.GetValueNames()){if($name -match '^shop_together\.issue_report(_h\d+)?$'){$value=$key.GetValue($name);$snapshot[$name]=if($value -is [byte[]]){[Convert]::ToBase64String($value)}else{[string]$value}}}}finally{$key.Dispose()}
    return $snapshot
}
$liveIssueBefore=Live-IssuePreference
$isolated=Join-Path $scratch 'Game'
$probeFolder=Join-Path $isolated 'ProbeWorkshop'
$approvedSave=$null
$liveProfile=Join-Path $env:USERPROFILE 'AppData\LocalLow\Questing Goose Studio\Probably Stolen\SaveFile.es3'
$liveProfileHash=if(Test-Path -LiteralPath $liveProfile){(Get-FileHash -LiteralPath $liveProfile -Algorithm SHA256).Hash}else{$null}
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
    if(-not(Test-Path -LiteralPath $profile)){
        New-IsolatedTestProfile $profile
        if(-not $approvedSave){throw 'Missing isolated mod approval'}
        Copy-Item -LiteralPath $approvedSave -Destination (Join-Path $profile 'SaveFile.es3') -Force
    }
    $env:SHOPTOGETHER_TEST_PERSISTENT_PATH=$profile
    $env:SHOPTOGETHER_TEST_REPORT_KEY=$testPrefPrefix+'.'+$part+'.'+$role
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
function Send-TestMouseRequests([string]$role,[string]$text,$process,[hashtable]$sequences){
    foreach($m in [regex]::Matches($text,'\[SMC-MOUSE-(REQUEST|DOWN|MOVE|UP)\] (\d+) (\d+) (\d+)')){
        $action=$m.Groups[1].Value;$sequence=[int]$m.Groups[2].Value;$x=[int]$m.Groups[3].Value;$y=[int]$m.Groups[4].Value
        if($sequence -gt $sequences[$role]){if($action -eq 'REQUEST'){$privateDesktop.PostMouse($process.Id,$x,$y)}else{$privateDesktop.PostMouseAction($process.Id,$x,$y,$action)};$sequences[$role]=$sequence;Write-Output "PRIVATE_DESKTOP_MOUSE_SENT $role $sequence $action"}
    }
}
$started=@()
$privateDesktop=$null
$testHostReports=[Collections.Generic.List[string]]::new()
$failedSuites=[Collections.Generic.List[string]]::new()
try{
    & (Join-Path $PSScriptRoot 'PrepareIsolatedGame.ps1') -GamePath $game -Target $isolated -AllowApprovedTestTrust
    Add-Type -Path (Join-Path $PSScriptRoot 'PrivateDesktop.cs')
    $privateDesktop=[ShopTogetherTestDesktop]::new()
    $env:SteamAppId='4348910';$env:SteamGameId='4348910'
    Write-Output 'PRIVATE_DESKTOP_READY'
        $approvalFolder=Join-Path $scratch 'ApprovalWorkshop'
        New-Item -ItemType Directory -Path $approvalFolder | Out-Null
        if($ProductionPackage){
            $expected=@('ShopTogether.dll','manifest.xml','preview.jpg')
            $files=@(Get-ChildItem -LiteralPath $ProductionPackage -Force)
            if($files.Count -ne 3 -or @($files | Where-Object {$_.PSIsContainer -or $_.Name -notin $expected}).Count -gt 0){throw 'Runtime package must contain only DLL, manifest and preview'}
            foreach($name in $expected){Copy-Item -LiteralPath (Join-Path $ProductionPackage $name) -Destination $approvalFolder}
        }else{
            if($ProductionDll){Copy-Item -LiteralPath $ProductionDll -Destination (Join-Path $approvalFolder 'ShopTogether.dll')}else{& (Join-Path $project 'Build.ps1') -OutputPath (Join-Path $approvalFolder 'ShopTogether.dll') -GamePath $game}
            Copy-Item -LiteralPath (Join-Path $project 'manifest.xml') -Destination $approvalFolder
        }
        $profile=Join-Path $scratch 'profile-approval';New-IsolatedTestProfile $profile
        # The production candidate is scanned while disabled, so its real PlayerPrefs key cannot be read or written by the test.
        $profileSave=Join-Path $profile 'SaveFile.es3'
        if(Test-Path -LiteralPath $profileSave){
            $settings=Get-Content -LiteralPath $profileSave -Raw | ConvertFrom-Json -AsHashtable
            if($settings.ContainsKey('modOrder') -and $settings.modOrder.ContainsKey('value')){
                if($Suite -contains 'perf1340' -or $Suite -contains 'release1339' -or $Suite -contains 'reported1339' -or $Suite -contains 'settlement1339'){$settings.modOrder.value=@()}else{$settings.modOrder.value=@($settings.modOrder.value | Where-Object {$_ -ne 'shop_together'})}
                [IO.File]::WriteAllText($profileSave,($settings | ConvertTo-Json -Depth 100),[Text.UTF8Encoding]::new($false))
            }
        }
        $env:SHOPTOGETHER_TEST_ROOT=$scratch;$env:SHOPTOGETHER_TEST_PERSISTENT_PATH=$profile
        $env:SHOPTOGETHER_TEST_ONLY_APPROVE='scan';$env:SHOPTOGETHER_PROBE_FOLDER=$approvalFolder
        $env:SHOPTOGETHER_TEST_REPORT_KEY=$testPrefPrefix+'.approval'
        $approvalLog=Join-Path $scratch 'approval.log'
        $approvalArgs=@('-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-logFile',('"'+$approvalLog+'"'))
        $approvalProcess=$privateDesktop.Start((Join-Path $isolated 'Probably Stolen.exe'),($approvalArgs -join ' '),$isolated);$started=@($approvalProcess)
        $deadline=[DateTime]::UtcNow.AddSeconds(180)
        do{Start-Sleep -Milliseconds 1000;$text=Read-TestLog $approvalLog;if($text -match '\[SMC-TEST APPROVAL-FAILED\]'){throw 'The game refused or could not approve the candidate in the isolated profile'}}while($text -notmatch '\[SMC-TEST APPROVED\]' -and -not $approvalProcess.HasExited -and [DateTime]::UtcNow -lt $deadline)
        if($text -notmatch '\[SMC-TEST APPROVED\]'){throw 'Isolated candidate approval did not complete'}
        [void]$approvalProcess.WaitForExit(10000);Stop-TestProcesses $started;$started=@()
        Write-Output 'PRODUCTION_MOD_SCANNED_WITHOUT_LOADING'
    if($ScanOnly){Write-Output 'PRODUCTION_PACKAGE_SCAN_ONLY_COMPLETE';return}
    & (Join-Path $project 'Build.ps1') -Probe -OutputPath (Join-Path $probeFolder 'ShopTogether.dll') -GamePath $game
    Copy-Item -LiteralPath (Join-Path $project 'manifest.xml') -Destination $probeFolder
    $env:SHOPTOGETHER_PROBE_FOLDER=$probeFolder
    $env:SHOPTOGETHER_TEST_ONLY_APPROVE='1'
    $approvalLog=Join-Path $scratch 'probe-approval.log';$approvalArgs=@('-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-logFile',('"'+$approvalLog+'"'))
    $approvalProcess=$privateDesktop.Start((Join-Path $isolated 'Probably Stolen.exe'),($approvalArgs -join ' '),$isolated);$started=@($approvalProcess)
    $deadline=[DateTime]::UtcNow.AddSeconds(180)
    do{Start-Sleep -Milliseconds 1000;$text=Read-TestLog $approvalLog;if($text -match '\[SMC-TEST APPROVAL-FAILED\]'){throw 'The game refused or could not approve the isolated probe'}}while($text -notmatch '\[SMC-TEST APPROVED\]' -and -not $approvalProcess.HasExited -and [DateTime]::UtcNow -lt $deadline)
    if($text -notmatch '\[SMC-TEST APPROVED\]'){throw 'Isolated probe approval did not complete'}
    [void]$approvalProcess.WaitForExit(10000);Stop-TestProcesses $started;$started=@()
    $approvedSave=Join-Path $profile 'SaveFile.es3';if(-not(Test-Path -LiteralPath $approvedSave)){throw 'The isolated approved profile was not saved'}
    $env:SHOPTOGETHER_TEST_ONLY_APPROVE=$null
    Write-Output 'ISOLATED_MODS_APPROVED_BY_GAME_SCANNER'
    foreach($part in $Suite){
        $started=@();$hostText='';$clientText=''
        foreach($role in @('host','client')){$log=Join-Path $scratch ("smc-$role.log");if(Test-Path -LiteralPath $log){Remove-Item -LiteralPath $log -Force}}
        try{
            $hostExtra=if($part -eq 'throttled'){@('--smc-throttle',"$UploadKbps")}else{@()}
            $hostProcess=Start-TestGame 'host' $part $hostExtra;$started+=,$hostProcess
            $deadline=[DateTime]::UtcNow.AddSeconds($(if($part -eq 'stress1327'){360}else{150}))
            do{Start-Sleep -Milliseconds 1000;$hostText=Read-TestLog (Join-Path $scratch 'smc-host.log');if($hostText -match '\[SMC FAIL\]|Init failed: 多人|Trust check failed:'){throw 'Host bootstrap failed'}}while($hostText -notmatch '\[SMC CONTROL\] (\d+)' -and -not $hostProcess.HasExited -and [DateTime]::UtcNow -lt $deadline)
            $control=[regex]::Match($hostText,'\[SMC CONTROL\] (\d+)');$invite=[regex]::Match($hostText,'\[SMC-INTERNAL-INVITE\] (SMC1-[A-Za-z0-9_-]+)')
            if(-not $control.Success -or -not $invite.Success){throw 'Host did not produce a test invitation'}
            $clientProcess=Start-TestGame 'client' $part @('--smc-control',$control.Groups[1].Value,'--smc-invite',$invite.Groups[1].Value);$started+=,$clientProcess
            $deadline=[DateTime]::UtcNow.AddSeconds(600);$mouseSequences=@{host=0;client=0}
            do{
                Start-Sleep -Milliseconds 1000;$hostText=Read-TestLog (Join-Path $scratch 'smc-host.log');$clientText=Read-TestLog (Join-Path $scratch 'smc-client.log')
                if($part -in @('perf1340','reported1339','release1339','mouse1327','trade1327','layout1326','stress1327')){Send-TestMouseRequests 'client' $clientText $clientProcess $mouseSequences}
                if(($hostText+$clientText) -match '\[SMC FAIL\]|Init failed: 多人|Trust check failed:'){throw 'Dual runtime assertion failed'}
                if($hostText -match '\[SMC COMPLETE\]' -and $clientText -match '\[SMC COMPLETE\]'){break}
            }while([DateTime]::UtcNow -lt $deadline -and -not($hostProcess.HasExited -and $clientProcess.HasExited))
            if($hostText -notmatch '\[SMC COMPLETE\]' -or $clientText -notmatch '\[SMC COMPLETE\]'){throw 'Dual runtime timed out'}
            foreach($process in $started){[void]$process.WaitForExit(10000)}
            if($part -eq 'issue1328'){
                foreach($role in @('host','client')){
                    $prior=Read-TestLog (Join-Path $scratch "smc-$role.log");Write-Output "Issue report initial ${role}:"
                    $prior -split '\r?\n' | Where-Object {$_ -match '\[SMC (PASS|FAIL|COMPLETE)|(?i)friend .*reported:'}
                    if($role -eq 'host'){foreach($line in ($prior -split '\r?\n')){if($line -match '(?i)friend .*reported:'){$testHostReports.Add($line)}}}
                }
                Stop-TestProcesses $started;$started=@()
                foreach($role in @('host','client')){$log=Join-Path $scratch "smc-$role.log";if(Test-Path -LiteralPath $log){Remove-Item -LiteralPath $log -Force}}
                $hostProcess=Start-TestGame 'host' $part @('--smc-issue-restore','1');$started+=,$hostProcess
                $clientProcess=Start-TestGame 'client' $part @('--smc-issue-restore','1');$started+=,$clientProcess
                $deadline=[DateTime]::UtcNow.AddSeconds(300);$mouseSequences=@{host=0;client=0}
                do{
                    Start-Sleep -Milliseconds 1000;$hostText=Read-TestLog (Join-Path $scratch 'smc-host.log');$clientText=Read-TestLog (Join-Path $scratch 'smc-client.log')
                    Send-TestMouseRequests 'host' $hostText $hostProcess $mouseSequences;Send-TestMouseRequests 'client' $clientText $clientProcess $mouseSequences
                    if(($hostText+$clientText) -match '\[SMC FAIL\]|Init failed: 多人|Trust check failed:'){throw 'Issue report restart assertion failed'}
                    if($hostText -match '\[SMC COMPLETE\]' -and $clientText -match '\[SMC COMPLETE\]'){break}
                }while([DateTime]::UtcNow -lt $deadline -and -not($hostProcess.HasExited -and $clientProcess.HasExited))
                if($hostText -notmatch '\[SMC COMPLETE\]' -or $clientText -notmatch '\[SMC COMPLETE\]'){throw 'Issue report restart timed out'}
                foreach($process in $started){[void]$process.WaitForExit(10000)}
                Write-Output 'ISOLATED_REPORT_RESTART_VERIFIED'
            }
            if($part -eq 'dump1322'){
                $expected=[regex]::Match($hostText,'\[SMC-SAVE-EXPECTED\] waste=(\d+) food=(\d+)');$slot=Join-Path $scratch 'profile-host\save_-777.es3'
                if(-not $expected.Success -or -not(Test-Path -LiteralPath $slot)){throw 'Isolated game did not write the requested save'}
                $saved=[IO.File]::ReadAllText($slot);$waste=[regex]::Match($saved,'"nuclearWasteDumped"\s*:\s*(\d+)');$food=[regex]::Match($saved,'"serpicoFoodKcal"\s*:\s*(\d+)')
                if(-not $waste.Success -or -not $food.Success -or $waste.Groups[1].Value -ne $expected.Groups[1].Value -or $food.Groups[1].Value -ne $expected.Groups[2].Value){throw 'Outing results did not persist in the isolated save'}
                Write-Output 'ISOLATED_SAVE_VERIFIED waste and food totals'
            }
        }catch{
            if(-not $ContinueOnFailure -or $_.Exception.Message -match '(?i)desktop|active screen|isolation'){throw}
            $details=[regex]::Match(((Read-TestLog (Join-Path $scratch 'smc-host.log'))+"`n"+(Read-TestLog (Join-Path $scratch 'smc-client.log'))),'\[SMC FAIL\] ([^\r\n]+)')
            $reason=if($details.Success){$details.Groups[1].Value}else{$_.Exception.Message};$failedSuites.Add($part+': '+$reason);Write-Output ('ISOLATED_SUITE_FAILED '+$part+': '+$reason)
        }finally{
            foreach($role in @('host','client')){
                Write-Output "Native ${part} ${role}:";$text=Read-TestLog (Join-Path $scratch "smc-$role.log")
                if($role -eq 'host'){foreach($line in ($text -split '\r?\n')){if($line -match '(?i)friend .*reported:'){$testHostReports.Add($line)}}}
                if($part -eq 'hint1322' -and $env:SMC_HINT_EVIDENCE_DIR){$picture=[regex]::Match($text,'\[SMC-HINT-IMAGE\] ([A-Za-z0-9+/=]+)');if($picture.Success){[IO.File]::WriteAllBytes((Join-Path $env:SMC_HINT_EVIDENCE_DIR "$role.png"),[Convert]::FromBase64String($picture.Groups[1].Value));Write-Output "HINT_IMAGE_SAVED $role"}}
                $text -split '\r?\n' | Where-Object {$_ -match '\[SMC (PASS|FAIL|COMPLETE|ROUTER|PERF|NOTE|AUDIT)|Init failed: 多人|Trust check failed:|\[ModLoader\].*(shop_together)|(?i)friend .*reported:'}
                $text -split '\r?\n' | Select-String -Pattern '\[SMC FAIL\]|Patching exception' -Context 0,4 | ForEach-Object ToString
            }
            Stop-TestProcesses $started;$started=@()
        }
    }
    if($testHostReports.Count -eq 0){Write-Output 'HOST_DIAGNOSTICS: none'}else{Write-Output 'HOST_DIAGNOSTICS:';$testHostReports | Select-Object -Unique}
    if($failedSuites.Count -gt 0){throw ('Failed isolated suites: '+($failedSuites -join '; '))}
}catch{
    foreach($name in @('approval.log','probe-approval.log')){$approvalLog=Join-Path $scratch $name;if(Test-Path -LiteralPath $approvalLog){(Read-TestLog $approvalLog) -split '\r?\n' | Where-Object {$_ -match '\[SMC-TEST (APPROVED|APPROVAL-FAILED|THREAD-SCAN)\]'} | Select-Object -First 10}}
    Write-Output ('ISOLATED_TEST_ERROR: '+$_.Exception.Message)
    throw
}finally{
    Stop-TestProcesses $started
    if($null -ne $privateDesktop){$privateDesktop.Dispose()}
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($prefRegistry,$true)
    if($null -ne $key){try{foreach($name in $key.GetValueNames()){if($name.StartsWith($testPrefPrefix,[StringComparison]::Ordinal)){$key.DeleteValue($name,$false)}}}finally{$key.Dispose()}}
    $liveIssueAfter=Live-IssuePreference
    if(($liveIssueBefore | ConvertTo-Json -Compress) -ne ($liveIssueAfter | ConvertTo-Json -Compress)){throw 'The live issue-report preference changed during the isolated test'}
    'LIVE_ISSUE_REPORT_UNCHANGED'
    $resolved=[IO.Path]::GetFullPath($scratch)
    if(-not $resolved.StartsWith($scratchRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Refusing cleanup outside the test workspace'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
    if((Get-FileHash -Algorithm SHA256 -LiteralPath $workshopDll).Hash -ne $workshopHash){throw 'The subscribed Workshop DLL changed during the isolated test'}
    if($liveProfileHash -and (Get-FileHash -LiteralPath $liveProfile -Algorithm SHA256).Hash -ne $liveProfileHash){throw 'The live profile changed during testing; inspect it without overwriting'}
    if($liveProfileHash){'LIVE_PROFILE_UNCHANGED_BY_TEST'}
    'WORKSHOP_DLL_UNCHANGED_BY_TEST'
}
