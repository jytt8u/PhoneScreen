$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$appRoot = $PSScriptRoot
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Нужен Windows с .NET Framework 4.8 (обычно встроен в Windows 10/11).' }
$refs = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll', '/r:System.Net.Http.dll')
$resource = "/resource:$appRoot\src\runtime.sha256,PhoneScreen.runtime.sha256"
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$appRoot\PhoneScreen.exe" "/win32icon:$appRoot\assets\app.ico" $resource $refs "$appRoot\src\AssemblyInfo.cs" "$appRoot\src\Core.cs" "$appRoot\src\Installer.cs" "$appRoot\src\Main.cs"
if ($LASTEXITCODE -ne 0) { throw 'Сборка приложения не прошла.' }
& $compiler /nologo /target:exe /platform:x64 /optimize+ "/out:$appRoot\PhoneScreen.Tests.exe" $resource $refs "$appRoot\src\AssemblyInfo.cs" "$appRoot\src\Core.cs" "$appRoot\src\Installer.cs" "$appRoot\src\Tests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Сборка проверок не прошла.' }
Write-Output 'Собраны PhoneScreen.exe и PhoneScreen.Tests.exe.'
