# Taskbar Tamer

A small creature-catching adventure in a rounded strip that floats just above your Windows taskbar.
It has no title bar and no taskbar button, and it doesn't show in Alt+Tab. You can shrink it into a small bubble whenever you want.

## Play (Windows 10/11)

1. Download **`TaskbarTamer.bat`** (open it on GitHub, then click the download button).
2. Double-click it. If Windows shows "Windows protected your PC", click **More info → Run anyway**.

There's nothing to install. It uses PowerShell, which comes with Windows, and the first launch takes a few seconds.

## Controls

| Key | Action |
| --- | --- |
| ← / → or A / D, or click the ground | Walk |
| Z, Enter or Space | Talk / confirm / next message |
| X or Esc | Back |
| Tab | Your team |
| M or the `–` button | Minimise to a bubble (click the bubble to bring it back) |
| H | Help |
| Drag the dotted left edge (or Alt + drag) | Move the strip |
| Right-click | Size, snap back above taskbar, new game, quit |

## The game

Pick one of three partners: Emberpup (fire), Driplet (water) or Sproutle (leaf), then head east through five regions.
Each region ends in a town with a Rest Hut, a Shop and a Gym. Beat the gym leader to earn their badge:

| Region | Route | Town | Gym leader | Badge |
| --- | --- | --- | --- | --- |
| Meadowmark | Breezy Path | Cobbleton | Slate (rock) | Pebble |
| Tidewater | Misty Shore | Port Ripple | Marina (water) | Wave |
| Verdant | Glowwood | Mossbrook | Fern (leaf) | Sprout |
| Cinder | Ember Cave | Kilnburg | Blaze (fire) | Flame |
| Stormcrest | Thunder Ridge | Boltgate | Volta (volt) | Spark |

With all five badges, walk Victory Road and take on Champion Vex. Catch critters in the tall grass with Orbs.
Fire beats Leaf, Leaf beats Water, Water beats Fire, Volt zaps Water, and Rock crushes Fire and Volt.
Critters evolve at around level 14-16. Badges show at the top of the strip.

Saves from the older, shorter version keep your team and items but start the journey again from Pixelvale.

The sky follows your real clock (day, sunset, night). Progress saves automatically to
`%APPDATA%\TaskbarTamer\save.txt`.

## For developers

The game is C# 5 (`src/`), so the compiler that ships with Windows PowerShell 5.1 can build it.
`python3 build.py` packs the source into `TaskbarTamer.bat`.
Every push runs `.github/workflows/build.yml` on Windows. It builds `TaskbarTamer.exe`, checks the `.bat` compiles, and auto-plays the game. Download the results from the **Actions** tab.
`tests/Sim.cs` renders screenshots and auto-plays the whole game under Mono:

```
mcs -langversion:5 -r:System.Windows.Forms -r:System.Drawing -out:sim.exe src/*.cs tests/Sim.cs
xvfb-run -a mono sim.exe out/
```
