#requires -Version 7.0
param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$PluginZip = '',
    [string]$SourceZip = '',
    [switch]$SkipAssembly
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
$utf8 = [Text.UTF8Encoding]::new($false, $true)
function Read-ReleaseText([string]$name) {
    $path = Join-Path $ProjectRoot $name
    if (-not [IO.File]::Exists($path)) { throw "Required release input is missing: $name" }
    $value = $utf8.GetString([IO.File]::ReadAllBytes($path)).TrimStart([char]0xFEFF)
    if ([string]::IsNullOrWhiteSpace($value)) { throw "Required release input is empty: $name" }
    return $value
}

$manifest = Read-ReleaseText 'manifest.json' | ConvertFrom-Json
foreach ($field in @('name','version_number','website_url','description','dependencies')) {
    if ($null -eq $manifest.PSObject.Properties[$field]) { throw "Missing manifest field: $field" }
}
if ($manifest.name -isnot [string] -or $manifest.name -cne 'New_Beginnings') {
    throw 'manifest.name must be New_Beginnings.'
}
$version = $manifest.version_number
if ($version -isnot [string] -or $version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'manifest.version_number must be Major.Minor.Patch without leading zeroes.'
}
if ($manifest.description -isnot [string] -or [string]::IsNullOrWhiteSpace($manifest.description) -or
    $manifest.description.Length -gt 250) { throw 'manifest.description must contain 1-250 characters.' }
if ($manifest.website_url -isnot [string]) { throw 'manifest.website_url must be a string.' }
if ($manifest.website_url.Length -gt 0) {
    $uri = $null
    if (-not [Uri]::TryCreate($manifest.website_url, [UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -notin @('https','http')) { throw 'manifest.website_url must be an HTTP(S) URL or empty.' }
}
if ($manifest.dependencies -isnot [Array]) { throw 'manifest.dependencies must be an array.' }
$dependencies = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($dependency in $manifest.dependencies) {
    if ($dependency -isnot [string] -or
        $dependency -cnotmatch '^[A-Za-z0-9_]+-[A-Za-z0-9_]+-(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or
        -not $dependencies.Add($dependency)) { throw "Invalid or duplicate manifest dependency: $dependency" }
}
if (-not ($manifest.dependencies | Where-Object { $_ -cmatch '^BepInEx-BepInExPack-' })) {
    throw 'manifest.dependencies must include BepInEx-BepInExPack.'
}
foreach ($name in @('README.md','CHANGELOG.md','LICENSE','BUILD.md')) { Read-ReleaseText $name | Out-Null }
$changes = Read-ReleaseText 'CHANGELOG.md'
if ($changes -notmatch ('(?m)^#{1,3}\s+(?:Version\s+)?\[?v?' + [regex]::Escape($version) + '(?:\]|\s|$)')) {
    throw "CHANGELOG.md must have a heading for $version."
}

$iconPath = Join-Path $ProjectRoot 'icon.png'
if (-not [IO.File]::Exists($iconPath)) { throw 'Required release input is missing: icon.png' }
$iconBytes = [IO.File]::ReadAllBytes($iconPath)
if ($iconBytes.Length -lt 24 -or [BitConverter]::ToString($iconBytes[0..7]) -cne '89-50-4E-47-0D-0A-1A-0A') {
    throw 'icon.png must be a valid PNG.'
}
Add-Type -AssemblyName System.Drawing.Common
$imageStream = [IO.MemoryStream]::new($iconBytes, $false)
$image = $null
try {
    $image = [Drawing.Image]::FromStream($imageStream, $false, $true)
    if ($image.RawFormat.Guid -ne [Drawing.Imaging.ImageFormat]::Png.Guid -or
        $image.Width -ne 256 -or $image.Height -ne 256) { throw 'icon.png must be a 256x256 PNG.' }
} finally { if ($null -ne $image) { $image.Dispose() }; $imageStream.Dispose() }

$dllPath = Join-Path $ProjectRoot 'bin/Release/net471/NewBeginnings.dll'
if (-not $SkipAssembly) {
    $stream = [IO.File]::OpenRead($dllPath)
    $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $assembly = $metadata.GetAssemblyDefinition()
        if ($metadata.GetString($assembly.Name) -cne 'NewBeginnings') { throw 'Unexpected DLL assembly name.' }
        $plugins = @()
        foreach ($handle in $metadata.TypeDefinitions) {
            $type = $metadata.GetTypeDefinition($handle)
            foreach ($attributeHandle in $type.GetCustomAttributes()) {
                $attribute = $metadata.GetCustomAttribute($attributeHandle)
                if ($attribute.Constructor.Kind -ne [System.Reflection.Metadata.HandleKind]::MemberReference) { continue }
                $constructor = $metadata.GetMemberReference([System.Reflection.Metadata.MemberReferenceHandle]$attribute.Constructor)
                if ($constructor.Parent.Kind -ne [System.Reflection.Metadata.HandleKind]::TypeReference) { continue }
                $reference = $metadata.GetTypeReference([System.Reflection.Metadata.TypeReferenceHandle]$constructor.Parent)
                if ($metadata.GetString($reference.Namespace) -cne 'BepInEx' -or
                    $metadata.GetString($reference.Name) -cne 'BepInPlugin') { continue }
                $blob = $metadata.GetBlobReader($attribute.Value)
                if ($blob.ReadUInt16() -ne 1) { throw 'Invalid BepInPlugin metadata.' }
                $plugins += [pscustomobject]@{ Guid=$blob.ReadSerializedString(); Name=$blob.ReadSerializedString(); Version=$blob.ReadSerializedString() }
            }
        }
        if ($plugins.Count -ne 1 -or $plugins[0].Guid -cne 'com.skeptic043.sailwind.newbeginnings' -or
            $plugins[0].Name -cne 'New Beginnings' -or $plugins[0].Version -cne $version) {
            throw 'DLL BepInPlugin GUID, name or version does not match this release.'
        }
    } finally { $pe.Dispose(); $stream.Dispose() }
} elseif ($PluginZip -or $SourceZip) { throw 'Archive verification cannot skip DLL metadata.' }

$releaseNames = @('manifest.json','icon.png','README.md','CHANGELOG.md','LICENSE')
$pluginEntries = @($releaseNames | ForEach-Object { [pscustomobject]@{ Source=(Join-Path $ProjectRoot $_); Name=$_ } })
$pluginEntries += [pscustomobject]@{ Source=$dllPath; Name='BepInEx/plugins/NewBeginnings/NewBeginnings.dll' }
# Exact public source allowlist: never recursively archive the working directory.
$sourceNames = $releaseNames + @('.gitignore','.gitattributes','BUILD.md','TESTING.md','assets/icon.svg',
    'NewBeginnings.csproj','Build.ps1','Package.ps1','tools/Verify-Package.ps1')
$sourceEntries = @($sourceNames | ForEach-Object { [pscustomobject]@{ Source=(Join-Path $ProjectRoot $_); Name=$_ } })
$sourceEntries += @(Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'src') -File -Filter '*.cs' |
    Sort-Object Name | ForEach-Object { [pscustomobject]@{ Source=$_.FullName; Name=('src/' + $_.Name) } })
if (-not ($sourceEntries.Name -ccontains 'src/Plugin.cs')) { throw 'Source package is missing src/Plugin.cs.' }
foreach ($entry in $sourceEntries) {
    if (-not [IO.File]::Exists($entry.Source)) { throw "Required source input is missing: $($entry.Name)" }
}

function Assert-Zip([string]$path, [object[]]$expected) {
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $path).Path)
    try {
        if ($archive.Entries.Count -ne $expected.Count) { throw "Unexpected entry count in $path" }
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($entry in $archive.Entries) {
            $inputFiles = @($expected | Where-Object { $_.Name -ceq $entry.FullName })
            if ($inputFiles.Count -ne 1 -or -not $seen.Add($entry.FullName)) { throw "Unexpected/duplicate ZIP entry: $($entry.FullName)" }
            if ($entry.Length -ne ([IO.FileInfo]$inputFiles[0].Source).Length) { throw "ZIP length differs: $($entry.FullName)" }
            $member = $entry.Open()
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($sha.ComputeHash($member)).Replace('-', '') }
            finally { $sha.Dispose(); $member.Dispose() }
            if ($hash -cne (Get-FileHash -LiteralPath $inputFiles[0].Source -Algorithm SHA256).Hash) {
                throw "ZIP contents differ: $($entry.FullName)"
            }
        }
    } finally { $archive.Dispose() }
}
if ($PluginZip) { Assert-Zip $PluginZip $pluginEntries }
if ($SourceZip) { Assert-Zip $SourceZip $sourceEntries }
[pscustomobject]@{ Version=$version; PluginEntries=$pluginEntries; SourceEntries=$sourceEntries; AssemblyChecked=(-not $SkipAssembly) }
