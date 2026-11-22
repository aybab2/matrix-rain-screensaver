# Matrix Rain Screensaver

A lightweight Windows screensaver that shows green falling "digital rain" (katakana, digits and symbols) on **every monitor at once**. It works with any monitor layout, including ultrawides, portrait screens and negative desktop coordinates.

![Screenshot](docs/screenshot.png)

- One full-screen window per monitor, sized correctly at any DPI scaling
- Low CPU use: only changed cells are redrawn, using GDI blits from a pre-rendered glyph atlas (about 3 ms per frame at 3440x1440)
- Standard screensaver modes: `/s` full screen, `/p` the small preview in Screen Saver Settings, `/c` the settings dialog
- Settings: falling speed, amount of rain, character size, colour (green, cyan, blue, purple, red, amber, white)

## Keyboard controls while it's running

| Key | Effect |
| --- | --- |
| `0`-`9` (top row or numpad) | Set falling speed: `0` slowest, `9` fastest |
| `d` | Less rain (lower density) |
| `D` (Shift+d) | More rain (higher density) |
| Any other key, a click, or moving the mouse | Exit |

Changes made with the keys are remembered next time.

## Build

No downloads are needed. It builds with the C# compiler that ships with Windows (.NET Framework 4.x):

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

This produces `MatrixRain.scr`.

## Install

Download `MatrixRain.scr` from the [latest release](https://github.com/aybab2/matrix-rain-screensaver/releases/latest) (or build it), then right-click it and choose **Install**, or run:

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1 -TimeoutMinutes 10
```

This sets the screensaver for the current user only, with no admin rights needed. Keep the folder where it is, because Windows runs the `.scr` from there. `uninstall.ps1` turns the screensaver off.

## Change the timeout or switch it off

Press the Windows key, type **screen saver**, and open **Change screen saver**. Change the **Wait** box, or choose **(None)** to switch it off.

