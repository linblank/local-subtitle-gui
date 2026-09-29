$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source = Join-Path $projectDir 'Program.cs'
$components = Join-Path $projectDir 'ComponentInstaller.cs'
$tests = Join-Path $projectDir 'Tests.cs'
$gui = Join-Path $projectDir 'local-subtitle-gui.exe'
$testExe = Join-Path $projectDir 'argument-tests.exe'

if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework C# compiler was not found.' }
& $compiler /nologo /target:winexe "/out:$gui" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll $source $components
if ($LASTEXITCODE -ne 0) { throw 'GUI compilation failed.' }
& $compiler /nologo /target:exe /main:LocalSubtitleGui.ArgumentTests "/out:$testExe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll $source $components $tests
if ($LASTEXITCODE -ne 0) { throw 'Argument-test compilation failed.' }
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Argument tests failed.' }

Write-Host "Build complete: $gui"
