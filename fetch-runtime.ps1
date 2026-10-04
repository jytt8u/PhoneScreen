$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
# Downloads the official scrcpy runtime and the FFmpeg/libusb source archives from their
# upstream projects, checks each against a pinned SHA-256 and installs scrcpy into tools\scrcpy.
# Needs PhoneScreen.exe from build.ps1, which carries the pinned scrcpy hash and installer.
$appRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
[void][System.Reflection.Assembly]::LoadFile((Join-Path $appRoot 'PhoneScreen.exe'))
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$downloads = Join-Path $appRoot 'downloads'
New-Item -ItemType Directory -Path $downloads -Force | Out-Null

$files = @(
    @{ Name = 'scrcpy-win64-v4.1.zip'; Url = [PhoneScreen.Package]::Download; Hash = [PhoneScreen.Package]::Hash },
    @{ Name = 'ffmpeg-8.1.2.tar.xz'; Url = 'https://ffmpeg.org/releases/ffmpeg-8.1.2.tar.xz'; Hash = '464beb5e7bf0c311e68b45ae2f04e9cc2af88851abb4082231742a74d97b524c' },
    @{ Name = 'libusb-1.0.30.tar.bz2'; Url = 'https://github.com/libusb/libusb/releases/download/v1.0.30/libusb-1.0.30.tar.bz2'; Hash = 'fea36f34f9156400209595e300840767ab1a385ede1dc7ee893015aea9c6dbaf' }
)
foreach ($file in $files) {
    $path = Join-Path $downloads $file.Name
    if (-not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.Hash) {
        Write-Output "Downloading $($file.Url)"
        Invoke-WebRequest -Uri $file.Url -OutFile $path -UseBasicParsing
    }
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $file.Hash) {
        Remove-Item -LiteralPath $path
        throw "$($file.Name): SHA-256 is $actual, expected $($file.Hash)."
    }
    Write-Output "OK  $($file.Hash)  $($file.Name)"
}

if ([PhoneScreen.Package]::Ready($appRoot)) {
    Write-Output 'tools\scrcpy is already installed.'
} else {
    [PhoneScreen.Package]::Install((Join-Path $downloads 'scrcpy-win64-v4.1.zip'), $appRoot)
    Write-Output 'Installed scrcpy into tools\scrcpy.'
}
