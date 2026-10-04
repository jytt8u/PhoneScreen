$ErrorActionPreference = 'Stop'
$appRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$releaseDirectory = Join-Path $appRoot 'dist'
$archivePath = Join-Path $releaseDirectory 'PhoneScreen-1.1.0-win64.zip'
$stageDirectory = Join-Path $releaseDirectory ('package-' + [Guid]::NewGuid().ToString('N'))
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
[void][System.Reflection.Assembly]::LoadFile((Join-Path $appRoot 'PhoneScreen.exe'))
[PhoneScreen.FileSafety]::CheckPath($releaseDirectory)
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
$runtimeLease = [PhoneScreen.Package]::AcquireRuntime($appRoot)
try {
    New-Item -ItemType Directory -Path $stageDirectory | Out-Null
    foreach ($file in @('PhoneScreen.exe', 'README.md', 'GUIDE.md', 'LICENSE', 'SECURITY.md', 'THIRD_PARTY.md', 'CHANGELOG.md')) {
        Copy-Item -LiteralPath (Join-Path $appRoot $file) -Destination $stageDirectory
    }
    Copy-Item -LiteralPath (Join-Path $appRoot 'assets') -Destination $stageDirectory -Recurse
    Copy-Item -LiteralPath (Join-Path $appRoot 'licenses') -Destination $stageDirectory -Recurse
    $runtimeDestination = Join-Path $stageDirectory 'tools\scrcpy'
    New-Item -ItemType Directory -Path $runtimeDestination -Force | Out-Null
    foreach ($entry in [PhoneScreen.Package]::RuntimeManifest().Keys) {
        $destination = Join-Path $runtimeDestination $entry
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path (Join-Path $appRoot 'tools\scrcpy') $entry) -Destination $destination
    }
    if (Test-Path -LiteralPath $archivePath) { [PhoneScreen.FileSafety]::DeleteChild($releaseDirectory, $archivePath) }
    # Windows PowerShell writes backslashes into entry names; use forward slashes so every unzip tool agrees.
    $archive = [System.IO.Compression.ZipFile]::Open($archivePath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $stageDirectory -Recurse -File) {
            $entry = $file.FullName.Substring($stageDirectory.Length + 1).Replace([char]92, [char]47)
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $archive.Dispose() }
} finally {
    $runtimeLease.Dispose()
    [PhoneScreen.FileSafety]::DeleteChild($releaseDirectory, $stageDirectory)
}
$releaseFiles = @($archivePath, (Join-Path $appRoot 'PhoneScreen.exe'), (Join-Path $appRoot 'downloads\ffmpeg-8.1.2.tar.xz'), (Join-Path $appRoot 'downloads\libusb-1.0.30.tar.bz2'))
$checksums = foreach ($file in $releaseFiles) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Missing release file: $file" }
    (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [System.IO.Path]::GetFileName($file)
}
[System.IO.File]::WriteAllLines((Join-Path $releaseDirectory 'SHA256SUMS.txt'), $checksums, (New-Object System.Text.UTF8Encoding($false)))
Get-Item -LiteralPath $archivePath | Select-Object Name, Length
