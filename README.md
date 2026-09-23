# Pace

Pace is a small Windows desktop app for keeping a personal, trust-based screen-time plan. It helps you notice how long you have been working or playing and gives you time to wrap up. It does not enforce a lockout or require someone else to approve more time.

## What it does

- Tracks screen time and records how much time is spent on each tab or app
- Includes regular reminders of when to take a break
- Almost every time setting configurable
- Daily plans created weekly
- Daily lockout reminders
- 
## Installing Pace

Download `Pace-Setup.exe` from the latest GitHub release and open it. The installer works for the current Windows account and does not require administrator access. It creates a Start Menu shortcut and offers optional desktop and launch-at-sign-in choices.

After installation, right-click the Pace notification-area icon to open Pace, restore the corner bar, change whether Pace starts when you sign in, check for updates, or exit.

## Running from source

The repository contains the source in `ScreenTime.cs`, a PowerShell build script in `build.ps1`, and the resulting executable as `ScreenTime.exe`.

From PowerShell, open this folder and build:

```powershell
cd "C:\Users\elonx\OneDrive\Documents\Screen time control"
.\build.ps1
```

Run the app with:

```powershell
.\ScreenTime.exe
```

`build.ps1` compiles the WinForms source with the .NET Framework 64-bit C# compiler at `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`. The app targets the Windows desktop and does not need a separate project file.

## Daily use

1. Start Screen Time. At the start of each week, make and save a plan for that dated week. You can also select next week to plan ahead, or edit a saved plan whenever needed.
2. Choose a reminder interval: 15, 20, or 30 minutes. Set the break length if you want something other than the 5-minute default.
3. Tracking starts automatically. Lock the computer when you leave it; ordinary idle time still counts so videos and controller games are included.
4. When a reminder appears, use it as a cue to finish a natural stopping point and take the suggested break.
5. When the 10-minute warning appears, finish what you are doing before the daily plan runs out. If you still need time afterward, request a break, then add extra time with a reason after the countdown finishes. Press **Win+L** to lock Windows when stepping away; unlocked time keeps counting even during the break countdown.

Closing the main window minimizes Pace to the notification area and keeps tracking. Use the notification-area menu to reopen the window or choose **Exit**. **Exit** stops tracking and closes the app.

## Building the Windows installer

Run:

```powershell
.\build-installer.ps1
```

The script builds and tests Pace, downloads the official signed Inno Setup compiler into the repository's ignored `.tools` folder when needed, and creates `dist\Pace-Setup-<version>.exe`. Pushing a matching version tag such as `v0.1.0` also builds the installer and publishes it as a GitHub release.

## Saved state

Screen Time saves its weekly plans and tracking state to:

```text
%AppData%\TrustScreenTime\state.xml
```

The state is local to the Windows user account. It is not a cloud sync service and does not require an online account.

## Current limitations

- Tracking only covers time while the app runs; it cannot recover usage from when it was closed.
- Screen Time does not inspect which applications or websites are being used.
- Pace must be running to track time. The installer and tray menu can configure it to launch automatically when you sign in.
- The break countdown uses elapsed time and does not verify that you took a break. It remains on screen after reaching zero until Continue is clicked.
- Corner reminders appear while the app runs; the reminder sound depends on Windows sound settings.
- The bar is a topmost window on the primary monitor's current desktop. Exclusive fullscreen games, secure Windows screens, and other virtual desktops may hide it; it is not a game overlay. Borderless/windowed games allow ordinary desktop overlays.
- Alert volume changes only the app's generated sound. Windows master volume, the volume mixer, output device, and mute still apply; it cannot guarantee being louder than other audio.
- The plan is intentionally voluntary. The app does not lock the computer, block programs, or technically prevent extra time.

