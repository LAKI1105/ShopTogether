param([string]$OutputPath = (Join-Path $PSScriptRoot 'ShopTogether.dll'),[switch]$Probe,[switch]$VisitorProbe,[string]$GamePath=$env:PROBABLY_STOLEN_PATH)
$ErrorActionPreference = 'Stop'
if(-not $GamePath){$GamePath='D:\SteamLibrary\steamapps\common\Probably Stolen'}
$managed = Join-Path $GamePath 'Probably Stolen_Data\Managed'
if(-not(Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))){throw 'Set PROBABLY_STOLEN_PATH or pass -GamePath pointing to your installed game.'}
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$names = @('mscorlib','System','System.Core','System.Xml','netstandard','Assembly-CSharp','Assembly-CSharp-firstpass','0Harmony','Mono.Security','Newtonsoft.Json','UnityEngine.CoreModule','UnityEngine.IMGUIModule','UnityEngine.InputLegacyModule','UnityEngine.UI','UnityEngine.TextRenderingModule','Unity.TextMeshPro','com.rlabrecque.steamworks.net','UnityEngine.UIModule','UnityEngine.JSONSerializeModule','UnityEngine.ImageConversionModule','UnityEngine.UnityWebRequestModule')
if($VisitorProbe){if(-not $Probe){throw 'Visitor test helper is only available in a probe build'};$names+='ShopTogetherIsolatedApproval'}
$refs = [Microsoft.CodeAnalysis.MetadataReference[]]@($names | ForEach-Object { [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path $managed "${_}.dll")) })
$parse=[Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default
$sourceFiles=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs')
if($Probe){$symbols=@('LAN_PROBE');if($VisitorProbe){$symbols+='PM_REAL_PROBE'};$parse=$parse.WithPreprocessorSymbols([string[]]$symbols);$sourceFiles+=Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Tests') -Filter '*RuntimeProbe.cs'}
$syntax = [Microsoft.CodeAnalysis.SyntaxTree[]]@($sourceFiles | ForEach-Object { [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($_.FullName), $parse, $_.FullName) })
$identityText=(@($sourceFiles|Sort-Object FullName|ForEach-Object {[IO.File]::ReadAllText($_.FullName).Replace("`r`n","`n")}) -join "`n") + [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'manifest.xml')).Replace("`r`n","`n")
$identity=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($identityText))).Substring(0,24)
$syntax = [Microsoft.CodeAnalysis.SyntaxTree[]]@($syntax + [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(('namespace ShopTogether { internal static class BuildIdentity { internal const string Code="'+$identity+'"; } }'),$parse))
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary).WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release)
$compile = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('ShopTogether', $syntax, $refs, $options)
$buffer = [IO.MemoryStream]::new()
try {
    $result = $compile.Emit($buffer)
    $result.Diagnostics | Where-Object Severity -eq 'Error' | ForEach-Object ToString
    if (-not $result.Success) { throw 'Compilation failed; existing output was preserved.' }
    [IO.File]::WriteAllBytes([IO.Path]::GetFullPath($OutputPath), $buffer.ToArray())
    "Compiled: $OutputPath"
} finally { $buffer.Dispose() }
