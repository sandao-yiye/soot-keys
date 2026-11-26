# Run with Windows PowerShell -STA and a running Codex desktop app. All selection,
# preference and toolbox files used here belong to a private test directory.
param([string] $Executable = (Join-Path $PSScriptRoot '..\desktop\dist\SootKeys.exe'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PickerDbFixture {
    [DllImport("winsqlite3.dll", CharSet=CharSet.Unicode, ExactSpelling=true, CallingConvention=CallingConvention.Cdecl)]
    private static extern int sqlite3_open16(string file, out IntPtr db);
    [DllImport("winsqlite3.dll", CharSet=CharSet.Ansi, ExactSpelling=true, CallingConvention=CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(IntPtr db, string sql, IntPtr fn, IntPtr data, IntPtr error);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr db);
    public static void Apply(string path, bool open) {
        IntPtr db;
        int rc = sqlite3_open16(path,out db);
        try {
            if (rc == 0) {
                string sql = "BEGIN; CREATE TABLE IF NOT EXISTS settings(scope TEXT, key TEXT, value TEXT, PRIMARY KEY(scope,key));"
                    + "INSERT OR REPLACE INTO settings VALUES ('global','electron-avatar-overlay-open','" + (open ? "true" : "false") + "'),"
                    + "('global','electron-renderer-content-owner','\"test-owner\"'),"
                    + "('electron-owner-atoms:test-owner','migrated-cloud-pet-ids-v1','{\"account\":{\"custom:soot-keys\":\"pet_fixture\"}}'); COMMIT;";
                rc = sqlite3_exec(db,sql,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
            }
            if (rc != 0) throw new InvalidOperationException("Fixture SQLite error " + rc);
        } finally { if (db != IntPtr.Zero) sqlite3_close(db); }
    }
}
'@
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Executable).Path)
$flags = [Reflection.BindingFlags]'Instance,Public,NonPublic'
function New-Internal([string]$Name, [object[]]$Values) {
    $type = $assembly.GetType("SootKeys.$Name", $true)
    $ctor = @($type.GetConstructors($flags) | Where-Object { $_.GetParameters().Count -eq $Values.Count })
    if ($ctor.Count -ne 1) { throw "Cannot resolve $Name constructor" }
    $argsForCtor = [object[]]::new($Values.Count)
    for ($i=0; $i -lt $Values.Count; $i++) { $argsForCtor[$i] = $Values[$i].PSObject.BaseObject }
    return $ctor[0].Invoke($argsForCtor)
}
function Tick-Host {
    $null = $runner.GetType().GetMethod('Refresh',$flags).Invoke($runner,@())
    [Windows.Forms.Application]::DoEvents()
    return $runner.GetType().GetField('cat',$flags).GetValue($runner)
}
function Get-WindowWidth($Form) {
    $geometry = $assembly.GetType('SootKeys.MascotMask').GetMethod('Bounds',[Reflection.BindingFlags]'Static,NonPublic')
    return $geometry.Invoke($null,@($Form.Handle)).Width
}
function Write-Fixture([string]$Selection,[bool]$IsOpen) {
    [IO.File]::WriteAllText((Join-Path $fixture 'config.toml'), "[desktop]`nselected-avatar-id = '$Selection'`n")
    # Reproduce a migrated installation: the legacy file permanently says closed.
    [IO.File]::WriteAllText((Join-Path $fixture '.codex-global-state.json'),'{"electron-avatar-overlay-open":false}')
    [PickerDbFixture]::Apply((Join-Path $fixture '.codex-global-state.sqlite'),$IsOpen)
}
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('SootKeys-Picker-' + [Guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($fixture)
$runner = $null
$link = $null
try {
    $link = New-Internal 'CodexLink' @($fixture)
    $preferences = New-Internal 'PetSettings' @((Join-Path $fixture 'settings.ini'))
    $query = $link.GetType().GetMethod('Refresh',$flags,$null,[type[]]@([bool]),$null)
    # Exercise the real file reader and host-process check. No masking is allowed
    # in this test, so the user's actual desktop companion stays untouched.
    $selected = [Func[bool,bool]]{param($unused) $query.Invoke($link,@($false))}
    $idle = [Func[bool]]{ $false }
    $runner = New-Internal 'LiveHost' @($preferences,$selected,$idle,$idle,$true)
    Write-Fixture 'custom:soot-keys' $false
    if ($null -ne (Tick-Host)) { throw 'Closed picker state displayed a cat.' }
    Write-Fixture 'custom:soot-keys' $true
    $cat = Tick-Host
    if ($null -eq $cat -or -not $cat.Visible) { throw 'Opening the selected pet failed. Is Codex running?' }
    $levels = $cat.GetType().GetField('levels',$flags).GetValue($cat)
    $previousWidth = 0
    foreach ($level in $levels) {
        $level.PerformClick()
        $null = $cat.GetType().GetMethod('Tick',$flags).Invoke($cat,@())
        [Windows.Forms.Application]::DoEvents()
        $width = Get-WindowWidth $cat
        if (-not $level.Checked -or $width -le $previousWidth) {
            throw "Right-click size change failed for $($level.Text): $previousWidth -> $width."
        }
        $previousWidth = $width
    }
    Write-Fixture 'another-pet' $true
    if ($null -ne (Tick-Host) -or -not $cat.IsDisposed) { throw 'Switching companions did not release the window.' }
    Write-Fixture 'pet_fixture' $true
    $cat = Tick-Host
    if ($null -eq $cat -or (Get-WindowWidth $cat) -ne $previousWidth) { throw 'Migrated pet failed to reopen at the selected size.' }
    Write-Fixture 'pet_fixture' $false
    if ($null -ne (Tick-Host)) { throw 'Dismissing the pet did not release the window.' }
    'PASS - SQLite supersedes stale JSON, local and cloud selection, live app detection, five sizes, reopen, switch and dismiss'
} finally {
    if ($runner) { $runner.Dispose() }
    if ($link) { $link.Dispose() }
    $dir = Get-Item -LiteralPath $fixture
    if ($dir.Parent.FullName -eq [IO.Path]::GetTempPath().TrimEnd('\') -and $dir.Name -match '^SootKeys-Picker-[a-f0-9]{32}$') {
        Remove-Item -LiteralPath $dir.FullName -Recurse -Force
    }
}
