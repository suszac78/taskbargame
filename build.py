#!/usr/bin/env python3
"""Packs src/*.cs into TaskbarTamer.bat, a single double-clickable file for Windows.

The .bat is also a PowerShell script: cmd runs the top few lines, which start
PowerShell on the same file. PowerShell compiles the embedded C# (with the
compiler that ships with Windows) and runs the game.
"""
import pathlib, re

root = pathlib.Path(__file__).parent
usings, bodies = [], []
for f in ["src/Data.cs", "src/Game.cs"]:
    text = (root / f).read_text()
    for line in text.splitlines():
        if re.match(r"^using [\w.]+;\s*$", line):
            if line not in usings:
                usings.append(line)
        else:
            bodies.append(line)
src = "\n".join(usings) + "\n" + "\n".join(bodies)
assert "'@" not in src, "C# source must not contain '@ (ends the PowerShell here-string)"
assert src.isascii(), "keep the source ASCII so cmd and PowerShell read it the same way"

bat = r'''<# :
@echo off
rem Taskbar Tamer - double-click to play. Right-click the game for options.
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command "$f='%~f0'; iex ([IO.File]::ReadAllText($f))"
exit /b
#>
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
try {
$src = @'
''' + src + r'''
'@
    Add-Type -TypeDefinition $src -ReferencedAssemblies System.Windows.Forms, System.Drawing -IgnoreWarnings -ErrorAction Stop
    [TaskbarTamer.Launcher]::Run()
} catch {
    [System.Windows.Forms.MessageBox]::Show("Taskbar Tamer couldn't start:`r`n`r`n" + $_.Exception.Message, "Taskbar Tamer") | Out-Null
}
'''
out = root / "TaskbarTamer.bat"
out.write_bytes(bat.replace("\r\n", "\n").replace("\n", "\r\n").encode("ascii"))
print("wrote", out, len(bat), "chars")
