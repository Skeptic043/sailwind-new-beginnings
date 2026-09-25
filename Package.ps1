#requires -Version 7.0
param(
    [string]$GameManagedDir = 'C:\Steam Games\steamapps\common\Sailwind\Sailwind_Data\Managed',
    [string]$ReferenceDir = '',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$verify = Join-Path $projectRoot 'tools/Verify-Package.ps1'
# Missing release assets must fail before a build or archive is created.
& $verify -ProjectRoot $projectRoot -SkipAssembly | Out-Null
if (-not $SkipBuild) {
    & (Join-Path $projectRoot 'Build.ps1') -GameManagedDir $GameManagedDir -ReferenceDir $ReferenceDir
    if ($LASTEXITCODE -ne 0) { throw 'Build did not succeed.' }
}
$inputs = & $verify -ProjectRoot $projectRoot
$artifactDir = Join-Path $projectRoot 'artifacts'
[IO.Directory]::CreateDirectory($artifactDir) | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmssfff'
$baseName = "NewBeginnings-$($inputs.Version)"
$pluginZip = Join-Path $artifactDir "$baseName-thunderstore-$stamp.zip"
$sourceZip = Join-Path $artifactDir "$baseName-source-$stamp.zip"
$pluginTemp = "$pluginZip.partial"
$sourceTemp = "$sourceZip.partial"
$created = [Collections.Generic.List[string]]::new()

function New-ZipFromMap([string]$destination, [object[]]$entries) {
    $stream = [IO.File]::Open($destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite)
    $created.Add($destination)
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $entries) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,
                $entry.Source, $entry.Name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose(); $stream.Dispose() }
}

try {
    New-ZipFromMap $pluginTemp $inputs.PluginEntries
    New-ZipFromMap $sourceTemp $inputs.SourceEntries
    # Read every compressed member and compare its name and bytes to the inputs.
    & $verify -ProjectRoot $projectRoot -PluginZip $pluginTemp -SourceZip $sourceTemp | Out-Null
    [IO.File]::Move($pluginTemp, $pluginZip)
    $created.Remove($pluginTemp) | Out-Null
    $created.Add($pluginZip)
    [IO.File]::Move($sourceTemp, $sourceZip)
    $created.Remove($sourceTemp) | Out-Null
    $created.Add($sourceZip)
    foreach ($path in @($pluginZip, $sourceZip)) {
        [pscustomobject]@{ Path=$path; SHA256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    }
    $created.Clear()
} finally {
    # Only files created by this invocation are removed, never directories.
    foreach ($path in $created) { if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) } }
}
