MATRIX RAIN SCREENSAVER
=======================
Green falling "digital rain" on every monitor at once (one window per screen, any layout).

Files
  MatrixRain.scr     the screensaver itself (Windows uses this file from this folder)
  src\MatrixRain.cs  source code
  MatrixRain.exe     same program, used by the Ctrl+M hotkey
  build.ps1          rebuilds MatrixRain.scr and MatrixRain.exe (uses the C# compiler built into Windows, no downloads)
  install.ps1        selects it as your screensaver (optional: -TimeoutMinutes 10)
  uninstall.ps1      turns the screensaver off
  Keep this folder where it is - Windows runs the screensaver from here.

Change how long before it starts / switch it off
  1. Press the Windows key, type  screen saver , and open "Change screen saver".
  2. "Wait" box = minutes of no mouse/keyboard before it starts. Click OK.
  3. To switch it off, pick "(None)" in the drop-down and click OK.
     To switch it back on, pick "MatrixRain" again (or right-click MatrixRain.scr > Install).

Change its look
  In the same window click "Settings..." - falling speed, amount of rain, font size, colour.
  "Preview" shows it full screen; move the mouse to stop it.

Keys while it is running
  0-9   speed (0 slowest, 9 fastest)
  d     less rain  (density 1.0 down to 0.1)
  D     more rain  (Shift + d)
  f     smaller text  (font size 1.0 down to 0.1)
  F     bigger text   (Shift + f)
  A box on screen shows the new value for a moment.
  Changes made with the keys are remembered next time.

Ctrl+M starts it any time
  Set up by hotkey-install.ps1 (starts with Windows). Remove with hotkey-uninstall.ps1.
  While it is on, other programs don't get Ctrl+M.
  It also checks once a minute that the screen saver is still switched on. Some apps
  (video calls, games) switch it off and never switch it back on; the listener puts back
  what you chose in Screen Saver Settings and notes the app in screensaver-restore.log.
  If you set the screen saver to (None), it leaves it off.

Stopping it
  Move the mouse, click, or press any other key.

