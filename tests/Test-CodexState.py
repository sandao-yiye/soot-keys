"""Exercise the compiled .NET reader against real rollback and WAL databases."""
import base64
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import sys
import tempfile
import time


PROBE = r"""
$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::LoadFrom($env:SOOT_TEST_EXE)
$type = $assembly.GetType('SootKeys.CodexLink', $true)
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
$link = $type.GetConstructors($flags)[0].Invoke(@($env:SOOT_TEST_HOME))
try {
    while (($null -ne ($line = [Console]::ReadLine())) -and ($line -eq 'probe')) {
        [Console]::WriteLine($type.GetMethod('IsSelected',$flags).Invoke($link,@()))
    }
} finally { $link.Dispose() }
"""


def exercise(executable, journal):
    with tempfile.TemporaryDirectory(prefix="soot-state-") as temp:
        home = Path(temp) / "墨爪"
        home.mkdir()
        (home / ".codex-global-state.json").write_text(
            '{"electron-avatar-overlay-open":true}', encoding="utf-8"
        )
        config = home / "config.toml"
        config.write_text('[desktop]\nselected-avatar-id="custom:soot-keys"\n', encoding="utf-8")
        database = home / ".codex-global-state.sqlite"
        db = sqlite3.connect(database, isolation_level=None)
        encoded = base64.b64encode(PROBE.encode("utf-16le")).decode()
        probe = subprocess.Popen(
            ["powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True,
            env=dict(os.environ, SOOT_TEST_HOME=str(home), SOOT_TEST_EXE=str(executable)),
        )

        def expect(value, label):
            probe.stdin.write("probe\n")
            probe.stdin.flush()
            got = probe.stdout.readline().strip()
            if got != str(value):
                raise AssertionError(f"{journal}: {label}: expected {value}, got {got!r}")

        def put(scope, key, value):
            db.execute("INSERT OR REPLACE INTO settings VALUES (?,?,?)", (scope, key, json.dumps(value)))

        try:
            db.execute(f"PRAGMA journal_mode={journal}")
            db.execute("PRAGMA wal_autocheckpoint=0")
            db.execute("CREATE TABLE settings (scope TEXT, key TEXT, value TEXT, PRIMARY KEY(scope,key))")
            expect(False, "SQLite present, missing visibility must override stale JSON")
            put("global", "electron-avatar-overlay-open", True)
            expect(True, "local id")
            put("global", "electron-renderer-content-owner", "active-owner")
            migrations = "migrated-cloud-pet-ids-v1"
            put("electron-owner-atoms:other-owner", migrations, {"other": {"custom:soot-keys": "pet_test"}})
            config.write_text('[desktop]\nselected-avatar-id="pet_test"\n', encoding="utf-8")
            expect(False, "mapping from another account")
            put("electron-owner-atoms:active-owner", migrations, {"active": {"custom:soot-keys": "pet_test"}})
            expect(True, "active owner's migrated id")
            db.execute("BEGIN " + ("EXCLUSIVE" if journal == "DELETE" else "IMMEDIATE"))
            put("global", "electron-avatar-overlay-open", False)
            expect(True, "last committed state during concurrent write")
            if journal == "DELETE":
                time.sleep(1.1)
                expect(False, "bounded retention during writer lock")
            db.execute("COMMIT")
            expect(False, "dismissal observed at commit")
            put("global", "electron-avatar-overlay-open", True)
            expect(True, "reopen observed without touching config or legacy JSON")
            put("global", "electron-renderer-content-owner", "unmapped-owner")
            expect(False, "active account switched")
            db.execute("DELETE FROM settings WHERE scope='global' AND key='electron-renderer-content-owner'")
            put("atom", migrations, {"single-user": {"custom:soot-keys": "pet_test"}})
            expect(True, "unscoped single-user atom store")
            db.execute("DROP TABLE settings")
            config.write_text('[desktop]\nselected-avatar-id="custom:soot-keys"\n', encoding="utf-8")
            expect(False, "unreadable settings table must not revive stale JSON")
        finally:
            probe.stdin.close()
            try:
                probe.wait(timeout=10)
            finally:
                if probe.poll() is None:
                    probe.kill()
                    probe.wait()
                db.close()
        if probe.returncode:
            raise RuntimeError(f"probe exited with code {probe.returncode}")
        print(f"PASS {journal} SQLite state, concurrent reads, cloud mapping and visibility")


if __name__ == "__main__":
    exe = (Path(sys.argv[1]) if len(sys.argv) > 1 else
           Path(__file__).resolve().parents[1] / "desktop" / "dist" / "SootKeys.exe")
    for journal_mode in ("DELETE", "WAL"):
        exercise(exe.resolve(strict=True), journal_mode)
