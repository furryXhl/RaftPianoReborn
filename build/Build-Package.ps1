param(
    [switch]$UsePrebuilt
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$managedSource = Join-Path $projectRoot 'src\managed'
$pianoAssets = Join-Path $projectRoot 'assets\piano'
$sfizzRoot = Join-Path $projectRoot 'third_party\sfizz'
$buildOutput = Join-Path $projectRoot 'build\out'
$distribution = Join-Path $projectRoot 'dist'

if ($UsePrebuilt) {
    $nativeCore = Join-Path $projectRoot 'prebuilt\RaftPianoRebornCore.dll'
}
else {
    $nativeCore = Join-Path $buildOutput 'native\RaftPianoRebornCore.dll'
}

$requiredFiles = @(
    (Join-Path $managedSource 'modinfo.json'),
    (Join-Path $managedSource 'KeyboardBindings.cs'),
    (Join-Path $pianoAssets 'Salamander.sfz'),
    (Join-Path $sfizzRoot 'bin\sfizz.dll'),
    (Join-Path $sfizzRoot 'LICENSE.txt'),
    (Join-Path $projectRoot 'third_party\salamander\LICENSE.txt'),
    (Join-Path $projectRoot 'third_party\salamander\README-original.md'),
    (Join-Path $projectRoot 'LICENSE.txt'),
    (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md'),
    $nativeCore
)
foreach ($path in $requiredFiles) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required project file is missing: $path"
    }
}

$samples = @(Get-ChildItem -LiteralPath (Join-Path $pianoAssets 'Samples') -File -Filter *.flac)
if ($samples.Count -ne 641) {
    throw "Expected 641 FLAC samples, found $($samples.Count)."
}

$stage = Join-Path $buildOutput 'package-stage'
if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
$stageDirectories = @(
    $stage,
    (Join-Path $stage 'Data'),
    (Join-Path $stage 'Samples'),
    (Join-Path $stage 'native'),
    (Join-Path $stage 'licenses')
)
New-Item -ItemType Directory -Path $stageDirectories -Force | Out-Null

Copy-Item -Path (Join-Path $pianoAssets 'Data\*') -Destination (Join-Path $stage 'Data')
Copy-Item -Path (Join-Path $pianoAssets 'Samples\*') -Destination (Join-Path $stage 'Samples')
Copy-Item -LiteralPath (Join-Path $pianoAssets 'Salamander.sfz') -Destination $stage
Copy-Item -LiteralPath (Join-Path $sfizzRoot 'bin\sfizz.dll') -Destination (Join-Path $stage 'native\sfizz.dll')
Copy-Item -LiteralPath $nativeCore -Destination (Join-Path $stage 'native\RaftPianoRebornCore.dll')
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE.txt') -Destination (Join-Path $stage 'licenses\LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $stage 'licenses\THIRD-PARTY.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'third_party\salamander\LICENSE.txt') -Destination (Join-Path $stage 'licenses\SALAMANDER-CC-BY-3.0.txt')
Copy-Item -LiteralPath (Join-Path $projectRoot 'third_party\salamander\README-original.md') -Destination (Join-Path $stage 'licenses\SALAMANDER-README.md')
Copy-Item -LiteralPath (Join-Path $sfizzRoot 'LICENSE.txt') -Destination (Join-Path $stage 'licenses\SFIZZ-LICENSE.txt')

$payloadFiles = @(Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object { $_.FullName.Substring($stage.Length + 1) })
$manifestLines = foreach ($file in $payloadFiles) {
    $relative = $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$relative`t$($file.Length)`t$hash"
}
$manifestPath = Join-Path $buildOutput 'payload-manifest.tsv'
[IO.File]::WriteAllLines($manifestPath, $manifestLines, [Text.UTF8Encoding]::new($false))

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Path $distribution -Force | Out-Null
$outputFile = Join-Path $distribution 'RaftPianoReborn-1.0.1.rmod'
$stream = [IO.File]::Open($outputFile, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
$zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
$fixedZipTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)

function Add-Entry([string]$source, [string]$entryName) {
    $entry = $zip.CreateEntry($entryName, [IO.Compression.CompressionLevel]::NoCompression)
    $entry.LastWriteTime = $fixedZipTime
    $input = [IO.File]::OpenRead($source)
    $output = $entry.Open()
    try { $input.CopyTo($output) }
    finally { $output.Dispose(); $input.Dispose() }
}

try {
    $managedFiles = @(Get-ChildItem -LiteralPath $managedSource -File | Sort-Object Name)
    foreach ($file in $managedFiles) {
        Add-Entry $file.FullName $file.Name
    }
    Add-Entry $manifestPath 'payload/manifest.tsv'
    foreach ($file in $payloadFiles) {
        $relative = $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
        Add-Entry $file.FullName ('payload/' + $relative + '.bin')
    }
}
finally {
    $zip.Dispose()
    $stream.Dispose()
}

$readZip = [IO.Compression.ZipFile]::OpenRead($outputFile)
try {
    if ($readZip.Entries.Count -ne $payloadFiles.Count + $managedFiles.Count + 1) {
        throw 'Package entry count mismatch.'
    }
    foreach ($file in $payloadFiles) {
        $relative = $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
        $entry = $readZip.GetEntry('payload/' + $relative + '.bin')
        if ($null -eq $entry -or $entry.Length -ne $file.Length) {
            throw "Packaged resource verification failed: $relative"
        }
        $input = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($sha.ComputeHash($input)).Replace('-', '') }
        finally { $input.Dispose(); $sha.Dispose() }
        $expected = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        if ($actual -ne $expected) {
            throw "Packaged resource checksum failed: $relative"
        }
    }
}
finally {
    $readZip.Dispose()
}

Get-Item -LiteralPath $outputFile | Select-Object FullName, Length
Get-FileHash -LiteralPath $outputFile -Algorithm SHA256
