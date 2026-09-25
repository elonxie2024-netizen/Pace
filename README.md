# Pace

Pace is a small Windows desktop app for keeping a personal, trust-based screen-time plan. It helps you notice how long you have been working or playing and gives you time to wrap up. It does not enforce a lockout or require someone else to approve more time.

## What it does

- Tracks screen time and records how much time is spent on each foreground tab or app
- Shows daily and weekly activity summaries with category visuals
- Gives periodic wrap-up reminders with options to start or cancel the break
- Supports dated weekly plans with configurable reminder and break timing
- Includes visual advanced plans with draggable blocks for named activities and times
- Lets advanced blocks name the apps or visible browser-tab words that fit, then gives a calm course-check warning and reports matched versus outside-block time
- Keeps a local heartbeat and records starts, stops, locks, sleep, wake, and any interval when Pace was not tracking
- Makes every user-facing timer configurable in hours and minutes from one Settings tab
- Uses a full-screen end-of-day or between-block state while still allowing intentional extra time with a reason
- Keeps the corner bar position, plans, usage, and reports across restarts

## Installing Pace

Download `Pace-Setup-<version>.exe` from the latest GitHub release and open it. The installer works for the current Windows account and does not require administrator access. It creates a Start Menu shortcut and offers optional desktop and launch-at-sign-in choices.

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

1. Start Pace. At the start of each week, make and save a plan for that dated week. You can also select next week to plan ahead, or edit a saved plan whenever needed.
2. Open **Settings** to choose the break-reminder frequency, break length, periodic wrap-up time, screen-time ending warning, activity-mismatch grace period, mismatch snooze, and alert volume. Every duration uses separate hours and minutes.
3. Tracking starts automatically. Lock the computer when you leave it; ordinary idle time still counts so videos and controller games are included.
4. When a periodic reminder appears, choose **Start break** immediately or **Cancel break**. If you leave it alone, Pace uses your configured wrap-up time and then starts the break automatically.
5. When the closing warning appears—3 minutes before the end by default—finish what you are doing before the day or block ends. If you still need time afterward, choose **Add more time**, select hours and minutes, and write a reason. That reason remains visible on the corner bar while the extra-time session is active.
6. Press **Win+L** when stepping away. A timed break continues while Windows is locked and waits for **Continue** after the timer finishes.

For an activity-aware advanced block, open **Edit blocks** and enter matching words under **Apps or visible tab words**. For example, a Homework block could use `Word, Canvas, Google Docs`. Pace waits for the grace period configured in **Settings** before showing a mismatch. The reminder uses the configured snooze duration, and the time is still shown in **Where time went**. Leaving the matching field blank keeps the block unrestricted.

Closing the main window minimizes Pace to the notification area and keeps tracking. Use the notification-area menu to reopen the window or choose **Exit**. **Exit** stops tracking and closes the app.

The **Tracking health** tab confirms that the heartbeat is current, checks whether launch at sign-in points to the running Pace installation, and lists recent tracking events. If Pace was closed or stopped unexpectedly, the next launch records the unknown interval in both **Tracking health** and **Time & reasons**. Time in a tracking gap is identified as unknown and is never silently added to screen usage.

## Building the Windows installer

Run:

```powershell
.\build-installer.ps1
```

The script builds and tests Pace, downloads the official signed Inno Setup compiler into the repository's ignored `.tools` folder when needed, and creates `dist\Pace-Setup-<version>.exe`. Pushing a matching version tag such as `v0.1.0` also builds the installer and publishes it as a GitHub release.

## Saved state

Pace saves its weekly plans and tracking state to:

```text
%AppData%\TrustScreenTime\state.xml
```

The state is local to the Windows user account. It is not a cloud sync service and does not require an online account.

Pace also maintains `state.xml.backup` in the same folder. If the main state file is damaged or interrupted during a save, Pace automatically restores the last good backup and reports the recovery through the notification-area icon.

## Current limitations

- Tracking only covers time while the app runs; it cannot recover usage from when it was closed.
- The heartbeat can identify when tracking was interrupted, but Pace cannot determine what happened on screen during that gap.
- Activity reporting and advanced-block matching read the foreground application name and visible window or browser-tab title. Matching ignores capitalization and checks whether any planned word appears in that text. Pace cannot read exact browser URLs without a browser extension.
- Pace must be running to track time. The installer and tray menu can configure it to launch automatically when you sign in.
- The break countdown uses elapsed time and does not verify that you took a break. It remains on screen after reaching zero until Continue is clicked.
- Corner reminders appear while the app runs; the reminder sound depends on Windows sound settings.
- The bar is a topmost window on the primary monitor's current desktop. Exclusive fullscreen games, secure Windows screens, and other virtual desktops may hide it; it is not a game overlay. Borderless/windowed games allow ordinary desktop overlays.
- Alert volume changes only the app's generated sound. Windows master volume, the volume mixer, output device, and mute still apply; it cannot guarantee being louder than other audio.
- The plan is intentionally voluntary. The app does not lock the computer, block programs, or technically prevent extra time.

