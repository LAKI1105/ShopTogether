param([Parameter(Mandatory=$true)][string]$GamePath,[Parameter(Mandatory=$true)][string]$Target)
$ErrorActionPreference='Stop'
$game=[IO.Path]::GetFullPath($GamePath)
$targetPath=[IO.Path]::GetFullPath($Target)
if(Test-Path -LiteralPath $targetPath){throw 'The isolated game target already exists'}
New-Item -ItemType Directory -Path $targetPath | Out-Null
foreach($name in @('Probably Stolen.exe','UnityPlayer.dll','UnityCrashHandler64.exe')){
    Copy-Item -LiteralPath (Join-Path $game $name) -Destination $targetPath
}
foreach($name in @('Probably Stolen_Data','MonoBleedingEdge')){
    Copy-Item -LiteralPath (Join-Path $game $name) -Destination $targetPath -Recurse
}
New-Item -ItemType Directory -Path (Join-Path $targetPath 'Mods'),(Join-Path $targetPath 'ProbeWorkshop') | Out-Null
$managed=Join-Path $targetPath 'Probably Stolen_Data\Managed'
$bridge=Join-Path $managed 'ShopTogetherTestRedirect.dll'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:library "/out:$bridge" (Join-Path $PSScriptRoot 'IsolatedWorkshopRedirect.cs')
if($LASTEXITCODE -ne 0){throw 'Could not build the isolated Workshop path redirect'}

# Only this disposable game copy is changed. Its normal mod scanning and capability checks remain enabled.
# The subscribed Workshop folder, the installed game, and the user's saved trust records are never written here.
Add-Type -Path (Join-Path $game 'Probably Stolen_Data\Managed\Unity.Burst.Cecil.dll')
$resolver=[Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($managed)
$reader=[Mono.Cecil.ReaderParameters]::new();$reader.InMemory=$true;$reader.AssemblyResolver=$resolver
$original=Join-Path $game 'Probably Stolen_Data\Managed\Assembly-CSharp.dll'
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($original,$reader)
$redirectAssembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($bridge,$reader)
try{
    $module=$assembly.MainModule
    $loader=$module.GetType('ModLoader')
    $method=@($loader.Methods | Where-Object {$_.Name -eq 'WorkshopModFolders'})
    if($method.Count -ne 1){throw 'Could not find the Workshop discovery method'}
    $calls=@($method[0].Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'GetInstalledItems' -and $_.Operand.DeclaringType.Name -eq 'ModWorkshop'})
    if($calls.Count -ne 1){throw 'Unexpected Workshop discovery implementation'}
    $bridgeType=$redirectAssembly.MainModule.GetType('IsolatedWorkshopRedirect')
    $bridgeMethod=@($bridgeType.Methods | Where-Object {$_.Name -eq 'Apply'})
    if($bridgeMethod.Count -ne 1){throw 'Missing test path redirect'}
    $il=$method[0].Body.GetILProcessor()
    $call=$il.Create([Mono.Cecil.Cil.OpCodes]::Call,[Mono.Cecil.MethodReference]$module.ImportReference($bridgeMethod[0]))
    $cast=$il.Create([Mono.Cecil.Cil.OpCodes]::Castclass,[Mono.Cecil.TypeReference]$method[0].ReturnType)
    $il.InsertAfter($calls[0],$call);$il.InsertAfter($call,$cast)
    # Loading the probe may refresh the trust fingerprint in memory. Do not persist test-only fingerprints.
    $trust=$module.GetType('ModTrust')
    $save=@($trust.Methods | Where-Object {$_.Name -eq 'Save' -and $_.Parameters.Count -eq 0})
    if($save.Count -ne 1){throw 'Could not isolate the mod trust record'}
    $save[0].Body.Instructions.Clear();$save[0].Body.ExceptionHandlers.Clear();$save[0].Body.Variables.Clear()
    $saveIl=$save[0].Body.GetILProcessor();$saveIl.Append($saveIl.Create([Mono.Cecil.Cil.OpCodes]::Ret))
    $assembly.Write((Join-Path $managed 'Assembly-CSharp.dll'))
}finally{$redirectAssembly.Dispose();$assembly.Dispose()}
'ISOLATED_GAME_READY'
