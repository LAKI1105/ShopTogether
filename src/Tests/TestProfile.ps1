function New-IsolatedTestProfile([string]$Path){
    $source=Join-Path $env:USERPROFILE 'AppData\LocalLow\Questing Goose Studio\Probably Stolen'
    [void][IO.Directory]::CreateDirectory($Path)
    foreach($name in @('SaveFile.es3','saves_index.es3','mod-trust-list.json')){
        $file=Join-Path $source $name
        if(Test-Path -LiteralPath $file -PathType Leaf){Copy-Item -LiteralPath $file -Destination $Path}
    }
    $configs=Join-Path $source 'ModConfigs'
    if(Test-Path -LiteralPath $configs -PathType Container){
        if((Get-Item -LiteralPath $configs -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Refusing to copy a linked mod configuration directory'}
        Copy-Item -LiteralPath $configs -Destination $Path -Recurse
    }
}
