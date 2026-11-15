param(
    [Parameter(Mandatory=$true)][string]$PreviewPath,
    [string]$DllPath=(Join-Path (Split-Path $PSScriptRoot -Parent) 'ShopTogether.dll'),
    [string]$OutputDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) 'out\Workshop')
)
$ErrorActionPreference='Stop'
$destination=[IO.Path]::GetFullPath($OutputDirectory)
$allowed=@('ShopTogether.dll','manifest.xml','preview.jpg')
if(Test-Path -LiteralPath $destination){
    foreach($entry in Get-ChildItem -LiteralPath $destination -Force){
        if($entry.PSIsContainer -or $entry.Name -notin $allowed){throw "Unexpected package entry: $($entry.Name). Use an empty output directory."}
    }
}else{[void][IO.Directory]::CreateDirectory($destination)}
# The game scans every installed file. Source archives and build tools belong
# in the source repository, never inside the Workshop runtime folder.
Copy-Item -LiteralPath $DllPath -Destination (Join-Path $destination 'ShopTogether.dll') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'manifest.xml') -Destination (Join-Path $destination 'manifest.xml') -Force
Copy-Item -LiteralPath $PreviewPath -Destination (Join-Path $destination 'preview.jpg') -Force
"Runtime package: $destination (DLL, manifest, preview only)"
