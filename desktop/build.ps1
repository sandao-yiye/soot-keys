param([string] $OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$compiler = @(
    (Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe')
    (Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe')
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$compiler) { throw 'Install .NET Framework 4.8 before building Soot Keys.' }
$assetPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'art/live'
$artFiles = @('eyes.csv')
foreach ($pose in 'idle','tapLeft','tapRight','space','enter','mouse','combo') {
    foreach ($index in 0..5) { $artFiles += "$pose-$index.png" }
}
$resources = foreach ($name in $artFiles) {
    $path = Join-Path $assetPath $name
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing artwork: $path" }
    "/resource:$path,cat.$name"
}
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$output = Join-Path (Resolve-Path -LiteralPath $OutputDirectory) 'SootKeys.exe'
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | ForEach-Object { $_.FullName })
$icon = Join-Path $PSScriptRoot 'cat.ico'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output "/win32icon:$icon" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/out:$output" @resources @sources
if ($LASTEXITCODE -ne 0) { throw "C# compiler returned $LASTEXITCODE" }
Write-Output $output
