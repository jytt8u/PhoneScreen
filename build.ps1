$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$appRoot = $PSScriptRoot
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows with .NET Framework 4.8 is required (it ships with Windows 10 and 11).' }
$refs = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll', '/r:System.Net.Http.dll')
$resource = "/resource:$appRoot\src\runtime.sha256,PhoneScreen.runtime.sha256"
$core = @('AssemblyInfo.cs', 'Core.cs', 'Installer.cs', 'Preferences.cs', 'Discovery.cs', 'QrCode.cs') | ForEach-Object { Join-Path $appRoot "src\$_" }
$ui = @('Widgets.cs', 'Main.cs') | ForEach-Object { Join-Path $appRoot "src\$_" }

& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$appRoot\PhoneScreen.exe" "/win32icon:$appRoot\assets\app.ico" $resource $refs $core $ui
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
& $compiler /nologo /target:exe /platform:x64 /optimize+ "/out:$appRoot\PhoneScreen.Tests.exe" $resource $refs $core (Join-Path $appRoot 'src\Tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
& $compiler /nologo /target:exe /main:PhoneScreen.UiChecks /platform:x64 /optimize+ "/out:$appRoot\PhoneScreen.UiChecks.exe" $resource $refs $core $ui (Join-Path $appRoot 'src\UiChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'UI check build failed.' }
Write-Output 'Built PhoneScreen.exe, PhoneScreen.Tests.exe and PhoneScreen.UiChecks.exe.'
