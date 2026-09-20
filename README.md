# Screen Time

Screen Time is a small Windows desktop app for keeping a personal, trust-based screen-time plan. It helps you notice how long you have been working or playing and gives you time to wrap up. It does not enforce a lockout or require someone else to approve more time.

## What it does

- Tracks unlocked computer time automatically while Screen Time is running, including idle time spent watching videos or playing with a controller. There is no pause button.
- Lets you make a separate, dated plan for each Monday-start week, with an hours-and-minutes daily budget. Select this week or next week to edit and save its plan independently; minute fields are always 0-59.
- Lets you plan five weeks ahead, copy the previous week's plan as a starting point, and records each saved plan change locally.
- Prompts you to plan a week that has no saved plan instead of silently repeating the previous week's budgets.
- Shows a sound and visual corner reminder at a repeating interval of 15, 20, or 30 minutes.
- Periodic reminders offer **Take a break** or **Start break**; either choice opens the configured break flow instead of silently dismissing the reminder.
- Periodic reminders now have a one-minute cleanup phase. The next-break countdown pauses while you wrap up, the reminder stays visible, and the full break starts automatically afterward. **Cancel break** cancels that transition.
- Keeps a compact bar in the bottom-right corner above the taskbar, showing today's allotted time (including extra time), used time, a progress bar, and the countdown to the next break reminder. During a break, the countdown shows the time left in that break.
- Places reminders above the persistent bar. Click the bar to reopen the dashboard; minimizing or closing the dashboard keeps the bar visible.
- Adds a **Where time went** tab with category totals for Communication, School / work, Creative, Entertainment, and Other, followed by individual foreground apps and visible browser titles. Entries can look like `Communication: 3h 55m`, `Communication · Outlook: 2h 5m`, or `Creative · CapCut: 0h 35m`. Browser entries use the visible tab title; exact website URLs need a browser extension.
- Plays a stronger three-chime alert. Open **Reminders & sound** to adjust its volume (default 85%) and test it. Changes save immediately; 0% mutes the alert.
- Lets you choose the reminder break length; the default is 5 minutes.
- Gives a similar warning 10 minutes before the day's planned time is used.
- After the daily budget is reached, requires completing a break countdown before you add extra time. Extra time also requires a reason, so the decision remains deliberate.
- A break opens a full-screen timer. It offers **Cancel break**, **Offscreen activity**, and, after the timer finishes, **Continue**. Choosing Offscreen activity removes the timer and leaves an open-ended “Continue when you’re ready” screen. Screen-time accounting stays stopped until Continue is clicked. Locking Windows with Win+L does not stop the break timer.
- Provides an honor-system break countdown before adding time after the daily budget runs out. The countdown does not pause usage tracking; unlocked time continues to count. Locked time and sleep gaps are excluded from usage.

The app follows the honor system. It does not block applications, close windows, monitor other apps, or prevent you from continuing after a warning. The reminders are prompts to make a choice, not a hard wall.

## Running the app

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

Closing the main window minimizes Screen Time to the notification area and keeps tracking. Use the notification-area menu to reopen the window or choose **Exit**. **Exit** stops tracking and closes the app.

## Saved state

Screen Time saves its weekly plans and tracking state to:

```text
%AppData%\TrustScreenTime\state.xml
```

The state is local to the Windows user account. It is not a cloud sync service and does not require an online account.

## Current limitations

- Tracking only covers time while the app runs; it cannot recover usage from when it was closed.
- Screen Time does not inspect which applications or websites are being used.
- There is no automatic startup, background service, or tamper protection yet; launch `ScreenTime.exe` yourself to track computer use.
- The break countdown uses elapsed time and does not verify that you took a break. It remains on screen after reaching zero until Continue is clicked.
- Corner reminders appear while the app runs; the reminder sound depends on Windows sound settings.
- The bar is a topmost window on the primary monitor's current desktop. Exclusive fullscreen games, secure Windows screens, and other virtual desktops may hide it; it is not a game overlay. Borderless/windowed games allow ordinary desktop overlays.
- Alert volume changes only the app's generated sound. Windows master volume, the volume mixer, output device, and mute still apply; it cannot guarantee being louder than other audio.
- The plan is intentionally voluntary. The app does not lock the computer, block programs, or technically prevent extra time.

