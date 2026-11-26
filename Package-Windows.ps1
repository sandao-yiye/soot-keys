[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
& (Join-Path $PSScriptRoot 'desktop/build.ps1')
$exe = Join-Path $PSScriptRoot 'desktop/dist/SootKeys.exe'
$check = Start-Process -FilePath $exe -ArgumentList '--self-test' -Wait -PassThru
if ($check.ExitCode -ne 0) { throw "Animation self-test failed (exit $($check.ExitCode))." }

$outDir = Join-Path $PSScriptRoot 'dist'
[IO.Directory]::CreateDirectory($outDir) | Out-Null
$outputFile = Join-Path $outDir 'SootKeys-Windows.zip'
$entries = [ordered]@{
    'SootKeys.exe' = 'desktop/dist/SootKeys.exe'
    'Install-CodexPet.cmd' = 'Install-CodexPet.cmd'
    'Remove-CodexLink.cmd' = 'Remove-CodexLink.cmd'
    'Install-CodexPet.ps1' = 'Install-CodexPet.ps1'
    'WindowsLink.ps1' = 'WindowsLink.ps1'
    'pet/pet.json' = 'pet/pet.json'
    'pet/spritesheet.webp' = 'pet/spritesheet.webp'
    'preview/soot-keys.gif' = 'preview/soot-keys.gif'
    'README.md' = 'README.md'
    'LICENSE' = 'LICENSE'
}

$tempFile = Join-Path $outDir ([IO.Path]::GetRandomFileName())
try {
    $zip = [IO.Compression.ZipFile]::Open($tempFile, 'Create')
    try {
        foreach ($entry in $entries.GetEnumerator()) {
            $path = Join-Path $PSScriptRoot $entry.Value
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $path, $entry.Key, [IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null
        }
    } finally {
        $zip.Dispose()
    }
    [IO.File]::Copy($tempFile, $outputFile, $true)
} finally {
    [IO.File]::Delete($tempFile)
}
Get-FileHash -Algorithm SHA256 -LiteralPath $outputFile | Format-List
