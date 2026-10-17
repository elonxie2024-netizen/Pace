$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
[void][Reflection.Assembly]::LoadFrom("$PSScriptRoot\ScreenTime.exe")
$flags = [Reflection.BindingFlags]'NonPublic,Instance'
$staticFlags = [Reflection.BindingFlags]'NonPublic,Static'
$tempFolder = Join-Path $env:TEMP ('ScreenTime-test-' + [guid]::NewGuid())
$app = $null
function Field($name) { $app.GetType().GetField($name, $flags).GetValue($app) }
function Call($name) { $app.GetType().GetMethod($name, $flags).Invoke($app, @()) }
function Elapsed([double]$seconds, [bool]$locked) { $app.GetType().GetMethod('ApplyElapsed', $flags).Invoke($app, @($seconds, $locked)) }
function Assert($condition, $message) { if (-not $condition) { throw $message } }
function Descendants($root) { foreach ($control in $root.Controls) { $control; if ($control.HasChildren) { Descendants $control } } }
Assert ((Get-Content "$PSScriptRoot\installer\Pace.iss" -Raw) -match 'autodesktop.*MyAppName' -and (Get-Content "$PSScriptRoot\ScreenTime.cs" -Raw) -match 'PaceShowCorner') 'The current installer must create a stable Pace shortcut and repeat launches must signal the corner bar'
try {
    $app = New-Object ScreenTime -ArgumentList $tempFolder
    (Field 'timer').Stop()
    $state = Field 'state'
    $state.AlertVolume = 0
    $day = Field 'day'
    Assert ($state.Days.Count -eq 1 -and $state.Weeks.Count -eq 0) 'Isolated constructor must begin with fresh state'
    Assert ($state.TrackingSessionOpen -and [datetime]::Parse($state.LastHeartbeat) -le [datetime]::Now -and $state.TrackingEvents[-1].Kind -eq 'Started') 'Starting Pace must open a heartbeat-backed tracking session'
    Call 'Today'
    Assert ($state.Days.Count -eq 1) 'Today must not duplicate records'

    Assert ([Settings]::Monday([datetime]'2026-09-20') -eq [datetime]'2026-09-14') 'Sunday belongs to preceding Monday'
    Assert ([Settings]::Monday([datetime]'2026-09-21') -eq [datetime]'2026-09-21') 'Monday starts its own week'
    Assert ([Settings]::Monday([datetime]'2027-01-01') -eq [datetime]'2026-12-28') 'Week must span the year boundary'
    $thisWeek = [Settings]::Monday([datetime]::Now)
    $nextWeek = $thisWeek.AddDays(7)
    $minutes = [int[]]@(120,120,120,120,120,120,120)
    $state.SetWeek($thisWeek, $minutes)
    $minutes[0] = 999
    Assert ($state.GetWeek($thisWeek).Minutes[0] -eq 120) 'Saved plan must own a copy of its values'
    Assert ($null -eq $state.GetWeek($nextWeek)) 'An unplanned next week must not inherit this week'
    $state.SetWeek($nextWeek, [int[]]@(60,70,80,90,100,110,120))
    $copySource=$nextWeek.AddDays(35); $copyTarget=$copySource.AddDays(7)
    $state.SetWeek($copySource,[int[]]@(45,60,75,90,105,120,135)); $copySourcePlan=$state.GetWeek($copySource); $copySourcePlan.Advanced=$true; $copySourcePlan.Hybrid=$true
    $copySourceBlock=New-Object AdvancedBlock -Property @{ WeekStart=$copySource.ToString('yyyy-MM-dd'); Day=1; Start='09:00'; End='10:30'; Activity='Copied focus'; AllowedApps='Word' }; $state.Blocks.Add($copySourceBlock)
    $ensureWeekPlan=$app.GetType().GetMethod('EnsureWeekPlan',$flags)
    $state.AutoCopyPreviousWeek=$false; $ensureWeekPlan.Invoke($app,@($copyTarget))
    Assert ($null -eq $state.GetWeek($copyTarget)) 'Turning automatic weekly copying off must leave an unplanned new week untouched'
    $state.AutoCopyPreviousWeek=$true; $ensureWeekPlan.Invoke($app,@($copyTarget)); $copiedPlan=$state.GetWeek($copyTarget); $copiedBlock=@($state.Blocks | Where-Object { $_.WeekStart -eq $copyTarget.ToString('yyyy-MM-dd') })[0]
    Assert ($null -ne $copiedPlan -and $copiedPlan.Advanced -and $copiedPlan.Hybrid -and $copiedPlan.Minutes[2] -eq 75 -and $copiedBlock.Activity -eq 'Copied focus' -and $copiedBlock.AllowedApps -eq 'Word') 'Automatic weekly copying must carry combined limits, advanced mode, and advanced blocks into the next dated week'
    $copiedPlan.Minutes[0]=33; $copySourcePlan.Minutes[0]=240; $ensureWeekPlan.Invoke($app,@($copyTarget))
    Assert ($copiedPlan.Minutes[0] -eq 33) 'Automatic weekly copying must never overwrite a week that was already planned'
    [void]$state.Blocks.Remove($copySourceBlock); [void]$state.Blocks.Remove($copiedBlock); [void]$state.Weeks.Remove($copySourcePlan); [void]$state.Weeks.Remove($copiedPlan)
    for($change=$state.PlanChanges.Count-1;$change -ge 0;$change--){if($state.PlanChanges[$change].WeekStart -eq $copyTarget.ToString('yyyy-MM-dd')){$state.PlanChanges.RemoveAt($change)}}
    $day.Used = 123
    $state.SetWeek($thisWeek.AddDays(6), [int[]]@(90,90,90,90,90,90,90))
    Assert ($state.Weeks.Count -eq 2 -and $state.GetWeek($thisWeek).Minutes[0] -eq 90) 'Editing a Sunday must update the existing dated week'
    Assert ($state.GetWeek($nextWeek).Minutes[1] -eq 70 -and $day.Used -eq 123) 'Editing must preserve other weeks and accrued usage'
    $weekPicker = Field 'weekPicker'
    Assert ($weekPicker.Items.Count -eq 6) 'Week picker must contain one current week and five future weeks without duplicates'
    $weekPicker.SelectedIndex = 1
    Assert ((Field 'selectedWeek') -eq $nextWeek) 'The Next week label must edit the actual next week'
    $weekPicker.SelectedIndex = 0
    $hours = Field 'planHours'; $minuteInputs = Field 'planMinutes'
    $minuteInputs[0].Value = 30; $hours[0].Value = 24
    Assert ($minuteInputs[0].Value -eq 0 -and -not $minuteInputs[0].Enabled) 'A 24-hour plan must not allow extra minutes beyond the day'
    $hours[0].Value = 2
    Assert $minuteInputs[0].Enabled 'Minutes must be enabled again below 24 hours'
    $planModeControl=Field 'planMode'; $planModeControl.SelectedIndex=2
    $getControlState=[Windows.Forms.Control].GetMethod('GetState',$flags)
    Assert ($planModeControl.Items.Count -eq 3 -and $planModeControl.SelectedItem -eq 'Limits + blocks' -and $getControlState.Invoke($hours[0],@(2)) -and $getControlState.Invoke((Field 'blockPreview'),@(2)) -and $getControlState.Invoke((Field 'editBlocks'),@(2))) 'The combined plan type must show daily limits and block controls together'
    Assert (-not $hours[0].Bounds.IntersectsWith((Field 'blockPreview').Bounds) -and (Field 'blockPreview').Bottom -lt 190) 'Combined limits and the block preview must fit without overlapping the plan inputs or save row'
    $planModeControl.SelectedIndex=0

    $intervalInput = Field 'interval'; $breakInput = Field 'breakMinutes'; $wrapInput = Field 'periodicWrap'; $closingInput = Field 'closingWarning'; $graceInput = Field 'mismatchGrace'; $snoozeInput = Field 'mismatchSnooze'; $idleWarningInput = Field 'inactivityWarning'; $idleOffscreenInput = Field 'inactivityOffscreen'
    $autoCopyToggle=Field 'autoCopyWeek'
    Assert ($state.AutoCopyPreviousWeek -and $autoCopyToggle.Text -eq 'Auto-copy weeks: On' -and -not $autoCopyToggle.Bounds.IntersectsWith((Field 'timingHelp').Bounds)) 'Settings must expose automatic weekly copying without covering its help text'
    Call 'ToggleAutoCopyPreviousWeek'; Assert (-not $state.AutoCopyPreviousWeek -and $autoCopyToggle.Text -eq 'Auto-copy weeks: Off') 'The weekly auto-copy setting must turn off immediately'
    Call 'ToggleAutoCopyPreviousWeek'; Assert ($state.AutoCopyPreviousWeek -and $autoCopyToggle.Text -eq 'Auto-copy weeks: On') 'The weekly auto-copy setting must turn back on immediately'
    Assert ($closingInput.TotalMinutes -eq 3) 'The default screen-time ending warning must be 3m'
    Assert ($idleWarningInput.TotalMinutes -eq 10 -and $idleOffscreenInput.TotalMinutes -eq 5) 'Inactivity must default to a 10m silent warning followed by 5m before offscreen activity'
    $intervalInput.TotalMinutes=75; $breakInput.TotalMinutes=90; $wrapInput.TotalMinutes=7; $closingInput.TotalMinutes=3; $graceInput.TotalMinutes=2; $snoozeInput.TotalMinutes=75; $idleWarningInput.TotalMinutes=12; $idleOffscreenInput.TotalMinutes=4
    (Field 'alertVolume').Value=64; Call 'SaveTimingSettings'
    Assert ($state.Interval -eq 75 -and $state.BreakMinutes -eq 90 -and $state.PeriodicWrapMinutes -eq 7 -and $state.ClosingWarningMinutes -eq 3 -and $state.MismatchGraceMinutes -eq 2 -and $state.MismatchSnoozeMinutes -eq 75 -and $state.InactivityWarningMinutes -eq 12 -and $state.InactivityOffscreenMinutes -eq 4 -and $state.AlertVolume -eq 64) 'Settings must save every user-facing duration independently'
    $gentleWave = [AlertSound]::CreateWave(100); $quietWave = [AlertSound]::CreateWave(25)
    $gentlePeak = 0; $quietPeak = 0
    for($sample=44;$sample -lt $gentleWave.Length;$sample+=2) { $value=[Math]::Abs([int][BitConverter]::ToInt16($gentleWave,$sample)); if($value -gt $gentlePeak){$gentlePeak=$value}; $value=[Math]::Abs([int][BitConverter]::ToInt16($quietWave,$sample)); if($value -gt $quietPeak){$quietPeak=$value} }
    Assert ($gentleWave.Length -eq 132344 -and $gentlePeak -gt 3000 -and $gentlePeak -lt 16000 -and $quietPeak -lt $gentlePeak/2) 'The built-in reminder must be a bounded, volume-sensitive gentle chime instead of the piercing alert'
    $builtInSounds=@([AlertSound]::BuiltInSounds); $soundFingerprints=@()
    foreach($builtIn in $builtInSounds){$bytes=[AlertSound]::CreateWave(100,$builtIn);$hash=[Security.Cryptography.SHA256]::Create();try{$soundFingerprints+=[Convert]::ToBase64String($hash.ComputeHash($bytes))}finally{$hash.Dispose()}}
    Assert ($builtInSounds.Count -ge 6 -and $builtInSounds -contains 'Rain on leaves' -and $builtInSounds -contains 'Morning bird' -and $builtInSounds -contains 'Soft kalimba' -and @($soundFingerprints | Select-Object -Unique).Count -eq $builtInSounds.Count) 'Built-in choices must include distinct nature, bird, instrument, and gentle sounds'
    $soundPath=Join-Path $tempFolder 'short-alert.wav'; [IO.File]::WriteAllBytes($soundPath,$gentleWave); $soundSeconds=0.0; $soundError=''
    Assert ([AlertSound]::TryGetDuration($soundPath,[ref]$soundSeconds,[ref]$soundError) -and $soundSeconds -le 2) 'The custom-sound validator must accept a readable short audio file'
    $settingsButtons=@((Field 'tabs').TabPages[3].Controls | Where-Object { $_ -is [Windows.Forms.Button] -and $_.Text -in @('Import','Preview','Dark: On','Dark: Off','Save settings') })
    $soundChoice=Field 'soundChoice'
    Assert ($settingsButtons.Count -eq 4 -and $soundChoice.DropDownStyle -eq [Windows.Forms.ComboBoxStyle]::DropDownList -and $soundChoice.Items.Count -eq $builtInSounds.Count) 'Settings must expose the built-in sound dropdown, import, preview, appearance, and save controls'
    for($a=0;$a -lt $settingsButtons.Count;$a++){for($b=$a+1;$b -lt $settingsButtons.Count;$b++){Assert (-not $settingsButtons[$a].Bounds.IntersectsWith($settingsButtons[$b].Bounds)) 'Sound and settings buttons must not overlap'}}
    Assert (@($settingsButtons | Where-Object { $_.Bounds.IntersectsWith($soundChoice.Bounds) }).Count -eq 0) 'The sound dropdown must not overlap any settings button'
    $durationFlags = [Reflection.BindingFlags]'NonPublic,Instance'
    $minutePart = $snoozeInput.GetType().GetField('minutes',$durationFlags).GetValue($snoozeInput)
    Assert ($minutePart.Value -eq 15 -and $minutePart.Maximum -le 59) 'Custom durations must split 1h 15m into hours and a minute component below 60'
    $numericInputs = @(Descendants $app | Where-Object { $_ -is [Windows.Forms.NumericUpDown] })
    Assert ($numericInputs.Count -ge 24 -and @($numericInputs | Where-Object { $_.TextAlign -ne [Windows.Forms.HorizontalAlignment]::Center }).Count -eq 0) 'Every visible numeric input must center its value'
    $earlySaveSettingsButton=@($settingsButtons | Where-Object { $_.Text -eq 'Save settings' })[0]
    Assert (-not $idleWarningInput.Bounds.IntersectsWith((Field 'alertVolume').Bounds) -and -not $idleOffscreenInput.Bounds.IntersectsWith($earlySaveSettingsButton.Bounds)) 'Inactivity settings must fit without covering the sound and save controls'
    if ($null -eq $app.BackgroundImage) {
        $testArt = New-Object Drawing.Bitmap -ArgumentList @((Join-Path $PSScriptRoot 'assets\pace-garden-fitted.png'))
        $app.GetType().GetField('originalArt',$flags).SetValue($app,$testArt)
        $app.GetType().GetMethod('ApplyBackgroundArt',$flags).Invoke($app,@())
    }
    $lightArt = [Drawing.Bitmap]$app.BackgroundImage
    $lightPixel = $lightArt.GetPixel([int]($lightArt.Width/2),[int]($lightArt.Height/2))
    $darkToggle = Field 'darkMode'; $app.GetType().GetMethod('DarkModeChanged',$flags).Invoke($app,@())
    Assert ($state.DarkMode -and [PaceTheme]::Dark -and $darkToggle.Text -eq 'Dark: On') 'Dark mode must apply immediately and update its setting control'
    Assert ($app.BackColor -eq [PaceTheme]::Background -and (Field 'todayCard').BackColor -eq [PaceTheme]::Card -and (Field 'tabs').TabPages[0].BackColor -eq [PaceTheme]::Surface) 'Dark mode must theme the dashboard, today card, and tab surfaces'
    Assert ((Field 'activityChart').BackColor -eq [PaceTheme]::Surface -and (Field 'trackingTimeline').BackColor -eq [PaceTheme]::Surface -and (Field 'cornerBar').BackColor -eq [Drawing.Color]::FromArgb(22,38,33)) 'Dark mode must theme reports, tracking visuals, and the corner bar'
    $darkArt = [Drawing.Bitmap]$app.BackgroundImage
    $darkPixel = $darkArt.GetPixel([int]($darkArt.Width/2),[int]($darkArt.Height/2))
    Assert (($darkPixel.R+$darkPixel.G+$darkPixel.B) -lt ($lightPixel.R+$lightPixel.G+$lightPixel.B)) 'Dark mode must retain and visibly darken the garden artwork'
    $settingsTab = (Field 'tabs').TabPages[3]; $saveSettingsButton = @($settingsTab.Controls | Where-Object { $_ -is [Windows.Forms.Button] -and $_.Text -eq 'Save settings' })[0]
    Assert (-not $darkToggle.Bounds.IntersectsWith((Field 'settingsStatus').Bounds) -and -not $darkToggle.Bounds.IntersectsWith($saveSettingsButton.Bounds)) 'The dark-mode control must not overlap settings status or save actions'

    $gapMethod = $app.GetType().GetMethod('RecordPreviousTrackingGap',$flags)
    $gapNow = [datetime]::Now
    $state.TrackingSessionOpen = $true; $state.LastHeartbeat = $gapNow.AddHours(-2).ToString('o')
    $gapMethod.Invoke($app,@($gapNow,$gapNow.AddHours(-3)))
    $state.TrackingSessionOpen = $false; $state.LastStoppedAt = $gapNow.AddHours(-1).ToString('o')
    $gapMethod.Invoke($app,@($gapNow,$gapNow.AddHours(-3)))
    Assert (($state.TrackingEvents | Where-Object { $_.Kind -eq 'Gap' -and $_.Detail -like '*unexpectedly*' }).Count -eq 1) 'An interrupted heartbeat must be identified as an unexpected tracking gap'
    Assert (($state.TrackingEvents | Where-Object { $_.Kind -eq 'Gap' -and $_.Detail -like '*was closed*' }).Count -eq 1) 'A deliberate exit must be distinguished from an unexpected tracking gap'
    $state.TrackingSessionOpen = $true; Call 'Heartbeat'
    Call 'RefreshTrackingHealth'; Call 'RefreshHistory'
    Assert ((Field 'trackingUsedStat').Text -like 'USED*' -and (Field 'trackingBreakStat').Text -like 'BREAK*' -and (Field 'trackingOffscreenStat').Text -like 'OFFSCREEN*') 'Today must show separate used, break, and offscreen-activity totals at the bottom of Tracking Health'
    Assert ((Field 'trackingExpectedStat').Text -notmatch '[×=]') 'Expected break time must be shown without exposing the calculation'
    Assert ((Field 'trackingUsedStat').BackColor -eq [TrackingHealthTimeline]::TrackedColor -and (Field 'trackingBreakStat').BackColor -eq [TrackingHealthTimeline]::BreakColor -and (Field 'trackingOffscreenStat').BackColor -eq [TrackingHealthTimeline]::OffscreenColor -and (Field 'trackingExpectedStat').BackColor -eq [TrackingHealthTimeline]::BreakColor -and (Field 'trackingBreakPercentStat').BackColor -eq [TrackingHealthTimeline]::BreakColor -and (Field 'trackingUsedStat').ForeColor -eq [Drawing.Color]::White) 'Bottom statistics must use filled boxes that match their tracking timeline colors'
    $expectedBreakMethod=$app.GetType().GetMethod('ExpectedBreakCount',$staticFlags); $breakPercentMethod=$app.GetType().GetMethod('BreakGoalPercent',$staticFlags)
    Assert ($expectedBreakMethod.Invoke($null,@([double]9000,[int]25)) -eq 6 -and $breakPercentMethod.Invoke($null,@([double]1350,[double]1800)) -eq 75) 'Two hours and thirty minutes at a 25-minute interval with 22m 30s of 30m expected break time must produce a 75% break goal'
    Assert (@((Field 'trackingEvents').Items | Where-Object { $_ -like '*NOT TRACKED*' }).Count -eq 2) 'Tracking health must list untracked intervals explicitly'
    Assert (@((Field 'history').Items | Where-Object { $_ -like '*not tracked*' }).Count -ge 1) 'Daily history must disclose tracking gaps instead of presenting incomplete data as complete'
    Assert ((Field 'trackingLaunchStatus').Text -like 'Launch at sign-in*') 'Tracking health must report whether launch at sign-in is working'
    $timelineNow = [datetime]::Today.AddHours(7)
    $timelineRecords = New-Object 'System.Collections.Generic.List[TrackingEventRecord]'
    $timelineRecords.Add((New-Object TrackingEventRecord -Property @{ Kind='Started'; Start=[datetime]::Today.AddHours(1).ToString('o') }))
    $timelineRecords.Add((New-Object TrackingEventRecord -Property @{ Kind='Locked'; Start=[datetime]::Today.AddHours(2).ToString('o') }))
    $timelineRecords.Add((New-Object TrackingEventRecord -Property @{ Kind='Unlocked'; Start=[datetime]::Today.AddHours(3).ToString('o') }))
    $timelineRecords.Add((New-Object TrackingEventRecord -Property @{ Kind='Paused'; Start=[datetime]::Today.AddHours(3.25).ToString('o') }))
    $timelineRecords.Add((New-Object TrackingEventRecord -Property @{ Kind='Resumed'; Start=[datetime]::Today.AddHours(3.75).ToString('o') }))
    $timelineRecords.Add((New-Object TrackingEventRecord -Property @{ Kind='Stopped'; Start=[datetime]::Today.AddHours(4).ToString('o') }))
    $timelineRecords.Add((New-Object TrackingEventRecord -Property @{ Kind='Gap'; Start=[datetime]::Today.AddHours(4).ToString('o'); End=[datetime]::Today.AddHours(5).ToString('o') }))
    $timelineRecords.Add((New-Object TrackingEventRecord -Property @{ Kind='Started'; Start=[datetime]::Today.AddHours(5).ToString('o') }))
    $timelineRecords.Add((New-Object TrackingEventRecord -Property @{ Kind='BreakCanceled'; Start=[datetime]::Today.AddHours(5.25).ToString('o') }))
    $timelineRecords.Add((New-Object TrackingEventRecord -Property @{ Kind='BreakStarted'; Start=[datetime]::Today.AddHours(5.5).ToString('o') }))
    $timelineRecords.Add((New-Object TrackingEventRecord -Property @{ Kind='Locked'; Start=[datetime]::Today.AddHours(5.67).ToString('o') }))
    $timelineRecords.Add((New-Object TrackingEventRecord -Property @{ Kind='Unlocked'; Start=[datetime]::Today.AddHours(5.83).ToString('o') }))
    $timelineRecords.Add((New-Object TrackingEventRecord -Property @{ Kind='BreakEndedEarly'; Start=[datetime]::Today.AddHours(6).ToString('o') }))
    $timelineRecords.Add((New-Object TrackingEventRecord -Property @{ Kind='OffscreenStarted'; Start=[datetime]::Today.AddHours(6.25).ToString('o') }))
    $timelineRecords.Add((New-Object TrackingEventRecord -Property @{ Kind='OffscreenEnded'; Start=[datetime]::Today.AddHours(6.75).ToString('o') }))
    $timelineModel = New-Object TrackingHealthTimeline
    $timelineModel.Size = New-Object Drawing.Size 790,105
    $timelineModel.SetData($timelineRecords,$timelineNow,$true,$false,$false)
    Assert ([Math]::Abs($timelineModel.TrackedSeconds-9000) -lt 1 -and [Math]::Abs($timelineModel.BreakSeconds-1800) -lt 1 -and [Math]::Abs($timelineModel.OffscreenSeconds-1800) -lt 1 -and [Math]::Abs($timelineModel.ManualPausedSeconds-1800) -lt 1 -and [Math]::Abs($timelineModel.PausedSeconds-3600) -lt 1 -and [Math]::Abs($timelineModel.GapSeconds-3600) -lt 1) 'Tracking timeline must distinguish tracked, break, offscreen activity, manually paused, locked or asleep, and untracked intervals'
    Assert ($timelineModel.BreakCanceledMarkers -eq 1 -and $timelineModel.BreakEndedEarlyMarkers -eq 1) 'Tracking health must retain separate markers for canceled scheduled breaks and active breaks ended early'
    $timelineMouse=$timelineModel.GetType().GetMethod('OnMouseMove',$flags)
    $hoverBreak=New-Object Windows.Forms.MouseEventArgs ([Windows.Forms.MouseButtons]::None),0,620,34,0; $timelineMouse.Invoke($timelineModel,@($hoverBreak.PSObject.BaseObject)); $hoverTip=$timelineModel.GetType().GetField('hoverTip',$flags).GetValue($timelineModel)
    Assert ($hoverTip.GetToolTip($timelineModel) -like 'Break*30m') 'Hovering over a timeline section must show its type and period length'
    $hoverCanceled=New-Object Windows.Forms.MouseEventArgs ([Windows.Forms.MouseButtons]::None),0,555,50,0; $timelineMouse.Invoke($timelineModel,@($hoverCanceled.PSObject.BaseObject))
    Assert ($hoverTip.GetToolTip($timelineModel) -like 'Break canceled*') 'Hovering over a cancellation line must identify the break event'
    Assert ($timelineModel.RangeStart -eq [datetime]::Today.AddHours(1) -and $timelineModel.RangeEnd -eq $timelineNow) 'The tracking bar must begin at the first recorded computer activity instead of midnight'
    $carryRecords=New-Object 'System.Collections.Generic.List[TrackingEventRecord]'; $carryRecords.Add((New-Object TrackingEventRecord -Property @{ Kind='Started'; Start=[datetime]::Today.AddDays(-1).AddHours(20).ToString('o') }))
    $currentFirstUse=[datetime]::Today.AddHours(9); $timelineModel.SetData($carryRecords,[datetime]::Today.AddHours(12),$true,$false,$false,$currentFirstUse,$null)
    Assert ($timelineModel.RangeStart -eq $currentFirstUse -and $timelineModel.RangeEnd -eq [datetime]::Today.AddHours(12)) 'An overnight open tracking session must crop today to the first actual computer use instead of midnight'
    $pastDate=[datetime]::Today.AddDays(-2); $pastRecords=New-Object 'System.Collections.Generic.List[TrackingEventRecord]'; $pastRecords.Add((New-Object TrackingEventRecord -Property @{ Kind='Started'; Start=$pastDate.AddHours(7).ToString('o') })); $pastRecords.Add((New-Object TrackingEventRecord -Property @{ Kind='OffscreenStarted'; Start=$pastDate.AddHours(18).ToString('o') })); $timelineModel.SetData($pastRecords,$pastDate.AddDays(1).AddTicks(-1),$false,$false,$false,$pastDate.AddHours(8),$pastDate.AddHours(18))
    Assert ($timelineModel.RangeStart -eq $pastDate.AddHours(8) -and $timelineModel.RangeEnd -eq $pastDate.AddHours(18)) 'A completed day must retroactively crop its right edge to the final detected use instead of midnight'
    Assert ($timelineModel.OffscreenSeconds -eq 0 -and [Math]::Abs($timelineModel.DailyOffscreenSeconds-21600) -lt 1 -and $timelineModel.DailyBreakSeconds -eq 0) 'A cropped day summary must count later offscreen activity through midnight without treating it as break time'
    $timelineModel.SetData($timelineRecords,$timelineNow,$true,$false,$false)
    $fullTimelineSpan=($timelineModel.VisibleEnd-$timelineModel.VisibleStart).TotalSeconds; $fullTimelineWidth=$timelineModel.Width; $fullTimelineHeight=$timelineModel.Height; $timelineModel.ZoomIn()
    Assert ($timelineModel.ZoomLevel -eq 2 -and $timelineModel.Width -eq 2*$fullTimelineWidth -and $timelineModel.Height -eq 2*$fullTimelineHeight -and ($timelineModel.VisibleEnd-$timelineModel.VisibleStart).TotalSeconds -lt $fullTimelineSpan) 'Tracking health must enlarge both dimensions of the complete timeline and show a shorter time window when zoomed in'
    $timelineModel.ZoomOut(); Assert ($timelineModel.ZoomLevel -eq 1 -and $timelineModel.Width -eq $fullTimelineWidth -and $timelineModel.Height -eq $fullTimelineHeight -and [Math]::Abs(($timelineModel.VisibleEnd-$timelineModel.VisibleStart).TotalSeconds-$fullTimelineSpan) -lt 1) 'Zooming out must restore the complete daily timeline and its original dimensions'
    $manualPauseColor = $timelineModel.GetType().GetField('manualPaused',$flags).GetValue($timelineModel)
    $breakColor = $timelineModel.GetType().GetField('breakColor',$flags).GetValue($timelineModel)
    $offscreenColor = $timelineModel.GetType().GetField('offscreen',$flags).GetValue($timelineModel)
    $gapColor = $timelineModel.GetType().GetField('gap',$flags).GetValue($timelineModel)
    Assert ($breakColor -ne $offscreenColor -and $breakColor -ne $manualPauseColor -and $offscreenColor -ne $gapColor -and $timelineModel.AccessibleDescription -like '*tracked*break*offscreen activity*manually paused*locked or asleep*not tracked*') 'Breaks and offscreen activity must have distinct colors and accessible labels instead of appearing as lock, sleep, or tracking gaps'
    $healthTimeline = Field 'trackingTimeline'
    Assert ($healthTimeline.AccessibleRole -eq [Windows.Forms.AccessibleRole]::Graphic -and $healthTimeline.Parent -is [Windows.Forms.Panel] -and $healthTimeline.Parent.Bottom -lt (Field 'trackingEvents').Top) 'The expandable tracking timeline viewport must fit above recent events without overlap'
    $timelineModel.Dispose()
    $navDate=[datetime]::Today.AddDays(-1); $navStart=New-Object TrackingEventRecord -Property @{ Kind='Started'; Start=$navDate.AddHours(8).ToString('o'); Detail='Previous day start.' }; $navStop=New-Object TrackingEventRecord -Property @{ Kind='Stopped'; Start=$navDate.AddHours(10).ToString('o'); Detail='Previous day stop.' }; $state.TrackingEvents.Add($navStart); $state.TrackingEvents.Add($navStop)
    $app.GetType().GetMethod('ChangeTrackingHealthDay',$flags).Invoke($app,@(-1))
    Assert ((Field 'trackingHealthDate') -eq $navDate -and (Field 'trackingDayLabel').Text -eq $navDate.ToString('ddd, MMM d') -and (Field 'trackingNextDay').Enabled) 'Tracking-health arrows must navigate to a previous day and enable the return arrow'
    Assert ((Field 'trackingUsedStat').Text -like 'USED*' -and (Field 'trackingExpectedStat').Text -like 'EXPECTED*' -and (Field 'trackingBreakPercentStat').Text -like 'BREAK GOAL*') 'Every previous day must show the same numerical break summary'
    Assert ((Field 'trackingTimeline').RangeStart -eq $navDate.AddHours(8) -and @((Field 'trackingEvents').Items | Where-Object { $_ -like '*Previous day*' }).Count -eq 2) 'Day navigation must show only that day and begin its bar at the first activity'
    $app.GetType().GetMethod('ChangeTrackingHealthDay',$flags).Invoke($app,@(1))
    Assert ((Field 'trackingHealthDate') -eq [datetime]::Today -and -not (Field 'trackingNextDay').Enabled) 'The next-day arrow must return to today and stop there'
    (Field 'trackingTimeline').ZoomIn()
    $zoomedTimeline=Field 'trackingTimeline'; $zoomedLabel=Field 'trackingZoomLabel'; $zoomedOutButton=Field 'trackingZoomOut'
    Assert ($zoomedTimeline.ZoomLevel -eq 2 -and $zoomedTimeline.Width -eq 2*$zoomedTimeline.Parent.ClientSize.Width -and $zoomedTimeline.Height -eq 2*$zoomedTimeline.Parent.ClientSize.Height -and $zoomedTimeline.Top -lt 0 -and $zoomedLabel.Text -eq '2x' -and $zoomedOutButton.Enabled) 'The visible zoom controls must magnify the timeline proportionally inside its viewport'
    (Field 'trackingTimeline').ZoomOut()
    $statsPanel=(Field 'trackingUsedStat').Parent
    Assert ((Field 'trackingEvents').Bottom -lt $statsPanel.Top -and $statsPanel.Bottom -le (Field 'tabs').Height-45) 'Bottom tracking statistics must fit below recent events without overlap'
    $healthTopControls=@((Field 'trackingPreviousDay'),(Field 'trackingDayLabel'),(Field 'trackingNextDay'),(Field 'trackingZoomOut'),(Field 'trackingZoomLabel'),(Field 'trackingZoomIn'))
    for($a=0;$a -lt $healthTopControls.Count;$a++){for($b=$a+1;$b -lt $healthTopControls.Count;$b++){Assert (-not $healthTopControls[$a].Bounds.IntersectsWith($healthTopControls[$b].Bounds)) 'Tracking day and zoom controls must not overlap'}}
    [void]$state.TrackingEvents.Remove($navStart); [void]$state.TrackingEvents.Remove($navStop)

    $blockA = New-Object AdvancedBlock
    $blockA.WeekStart = $thisWeek.ToString('yyyy-MM-dd'); $blockA.Day = 1; $blockA.Start = '09:00'; $blockA.End = '10:00'; $blockA.Activity = 'School'; $blockA.AllowedApps = 'Word, Canvas, word'
    $blockB = New-Object AdvancedBlock
    $blockB.WeekStart = $blockA.WeekStart; $blockB.Day = 1; $blockB.Start = '09:45'; $blockB.End = '11:00'; $blockB.Activity = 'Creative work'
    $blockC = New-Object AdvancedBlock
    $blockC.WeekStart = $blockA.WeekStart; $blockC.Day = 1; $blockC.Start = '10:00'; $blockC.End = '11:00'; $blockC.Activity = 'Reading'
    Assert ([AdvancedBlockRules]::Overlaps($blockA,$blockB)) 'Advanced blocks on the same day must detect overlap'
    Assert (-not [AdvancedBlockRules]::Overlaps($blockA,$blockC)) 'Adjacent advanced blocks must be allowed'
    $differentWeekBlock = [AdvancedBlockRules]::Copy($blockA,$nextWeek.ToString('yyyy-MM-dd'))
    Assert (-not [AdvancedBlockRules]::Overlaps($blockA,$differentWeekBlock)) 'Blocks in different weeks must not conflict with each other'
    Assert ([AdvancedBlockRules]::NormalizeAllowedApps($blockA.AllowedApps) -eq 'Word, Canvas') 'Activity matching words must be trimmed and deduplicated without changing their readable spelling'
    Assert ([AdvancedBlockRules]::ActivityFits($blockA,'Microsoft Word - Essay') -and -not [AdvancedBlockRules]::ActivityFits($blockA,'YouTube - Music')) 'Advanced blocks must compare the foreground title with any configured app or tab word'
    Assert ($differentWeekBlock.AllowedApps -eq $blockA.AllowedApps) 'Copying a week must retain activity matching words'
    $advancedEditor = New-Object AdvancedPlanEditorForm -ArgumentList $thisWeek,([AdvancedBlock[]]@($blockA))
    $editorBlocks = $advancedEditor.GetType().GetField('blocks',$flags).GetValue($advancedEditor)
    $editorTimeline = $advancedEditor.GetType().GetField('timeline',$flags).GetValue($advancedEditor)
    $editorTimeline.SelectBlock($editorBlocks[0])
    $allowedAppsInput = $advancedEditor.GetType().GetField('allowedApps',$flags).GetValue($advancedEditor)
    $editorStatus = $advancedEditor.GetType().GetField('status',$flags).GetValue($advancedEditor)
    Assert ($allowedAppsInput.Text -eq $blockA.AllowedApps -and $allowedAppsInput.Bottom -lt $editorStatus.Top) 'The advanced editor must load activity words into a dedicated field without overlapping its status text'
    Assert ($advancedEditor.BackColor -eq [PaceTheme]::Background -and $editorTimeline.BackColor -eq [PaceTheme]::Surface -and $allowedAppsInput.BackColor -eq [PaceTheme]::Input) 'Dark mode must carry into the advanced planner and its inputs'
    $allowedAppsInput.Text = 'Outlook; Teams; outlook'
    $advancedEditor.GetType().GetMethod('AddOrUpdate',$flags).Invoke($advancedEditor,@())
    Assert ($advancedEditor.ResultBlocks[0].AllowedApps -eq 'Outlook, Teams') 'Updating a block must save normalized app and site matching words'
    $advancedEditor.Dispose()
    Assert ([AdvancedBlockRules]::Time(1440) -eq '24:00') 'The timeline must preserve a block ending at midnight'
    Assert ([AdvancedBlockRules]::Minutes('24:00') -eq 1440) 'Midnight block endings must convert to 1440 minutes'
    Assert ([AdvancedBlockRules]::IsValid((New-Object AdvancedBlock -Property @{ WeekStart=$thisWeek.ToString('yyyy-MM-dd'); Day=1; Start='23:00'; End='24:00'; Activity='Late work' }))) 'A block ending at midnight must be valid'
    $preview = New-Object AdvancedSchedulePreview
    $preview.Size = New-Object Drawing.Size 620,132
    $preview.SetBlocks([AdvancedBlock[]]@($blockA,$blockC))
    $previewBitmap = New-Object Drawing.Bitmap 620,132
    $preview.DrawToBitmap($previewBitmap,(New-Object Drawing.Rectangle 0,0,620,132))
    $previewBitmap.Dispose(); $preview.Dispose()
    $dragBlock = [AdvancedBlockRules]::Copy($blockA,$blockA.WeekStart)
    $dragBlocks = New-Object 'System.Collections.Generic.List[AdvancedBlock]'
    $dragBlocks.Add($dragBlock)
    $timeline = New-Object AdvancedTimeline -ArgumentList (, $dragBlocks)
    $timeline.Size = New-Object Drawing.Size 980,365
    $mouseFlags = [Reflection.BindingFlags]'NonPublic,Instance'
    $down = New-Object Windows.Forms.MouseEventArgs ([Windows.Forms.MouseButtons]::Left),1,411,38,0
    $move = New-Object Windows.Forms.MouseEventArgs ([Windows.Forms.MouseButtons]::Left),1,474,38,0
    $up = New-Object Windows.Forms.MouseEventArgs ([Windows.Forms.MouseButtons]::Left),1,474,38,0
    $timeline.GetType().GetMethod('OnMouseDown',$mouseFlags).Invoke($timeline,@($down.PSObject.BaseObject))
    $timeline.GetType().GetMethod('OnMouseMove',$mouseFlags).Invoke($timeline,@($move.PSObject.BaseObject))
    $timeline.GetType().GetMethod('OnMouseUp',$mouseFlags).Invoke($timeline,@($up.PSObject.BaseObject))
    Assert ($dragBlock.Start -ne '09:00' -and ([AdvancedBlockRules]::Minutes($dragBlock.Start) % 15) -eq 0) 'Dragging must move blocks in 15-minute steps'
    $timeline.Dispose()

    $corner = Field 'cornerBar'
    Assert ([CornerBar]::Countdown(-4) -eq '00:00') 'Countdown must clamp negative values'
    Assert ([CornerBar]::Countdown(60.1) -eq '01:01' -and [CornerBar]::Countdown(60) -eq '01:00') 'Countdown must round partial seconds upward'
    Assert ([CornerBar]::Countdown(3661) -eq '1:01:01') 'Long countdowns must use hours instead of minute values above 59'
    $corner.UpdateStatus($true, 7200, 3660, 61, $false)
    Assert ($corner.AccessibleDescription -eq 'Tracking: active. Allotted: 2h. Used: 1h 1m. NEXT BREAK: 01:01') 'Corner must display tracking health, allotment, usage, and next break'
    $corner.SetTrackingHealth($false)
    Assert ($corner.AccessibleDescription -like 'Tracking: active, but data is not saving.*') 'The corner must expose a visible and accessible unsaved state'
    $corner.SetTrackingHealth($true)
    $corner.UpdateStatus($false, 0, 120, 10, $false)
    Assert ($corner.AccessibleDescription -like '*Allotted: No plan. Used: 2m.*') 'Unplanned corner must still show usage'
    $corner.UpdateStatus($true, 60, 120, 90, $true)
    Assert ($corner.AccessibleDescription -like '*BREAK LEFT: 01:30') 'Active break must change countdown label'
    $corner.UpdateStatus($true, 60, 120, 0, 'DAY COMPLETE')
    Assert ($corner.AccessibleDescription -like '*DAY COMPLETE: 00:00') 'The corner must distinguish a completed day from a break'
    $normalCornerHeight = $corner.ClientSize.Height
    $corner.SetReason('Finish the history outline')
    $reasonLabel = $corner.GetType().GetField('reason', $flags).GetValue($corner)
    $allottedTitle = $corner.GetType().GetField('allottedTitle', $flags).GetValue($corner)
    Assert ($reasonLabel.Text -eq 'Finish the history outline' -and $corner.ClientSize.Height -gt $normalCornerHeight) 'An active extra-time reason must receive a large dedicated area on the corner bar'
    Assert ($reasonLabel.Bottom -lt $allottedTitle.Top -and $corner.AccessibleDescription -like '*Reason: Finish the history outline.*') 'The reason must not overlap the existing corner-bar controls'
    $corner.SetReason('')
    Assert ($reasonLabel.Text -eq '' -and $corner.ClientSize.Height -eq $normalCornerHeight) 'The corner bar must return to its compact layout when no reason is active'
    $corner.SetFocusMismatch('Homework','YouTube - Microsoft Edge')
    Assert ($corner.AccessibleDescription -like '*Focus cue: PLANNED: Homework*YouTube*outside this block*') 'An activity mismatch must receive a clear accessible course-check message'
    Assert ($reasonLabel.Bottom -lt $allottedTitle.Top) 'The activity mismatch must expand the corner bar without overlapping its controls'
    $corner.SetReason('')
    $cornerProgress = $corner.GetType().GetField('progress', $flags).GetValue($corner)
    Assert ($cornerProgress.Value -eq 1000) 'Over-budget progress must clamp at maximum'
    Call 'RefreshView'
    Assert ($corner.AccessibleDescription -like '*Allotted: 1h 30m. Used: 2m.*') 'Dashboard refresh must update the corner from current state'
    $app.Show()
    [System.Windows.Forms.Application]::DoEvents()
    $app.Hide()
    Assert ($corner.Visible -and -not $app.Visible -and $corner.TopMost) 'Corner must remain visible above other windows when dashboard is hidden'
    $passiveNames = @('trackingHealth','reason','allottedTitle','usedTitle','nextTitle','allotted','used','next','progress')
    $passiveControls = @($passiveNames | ForEach-Object { $corner.GetType().GetField($_,$flags).GetValue($corner) })
    Assert ($corner.Cursor -eq [Windows.Forms.Cursors]::SizeAll -and @($passiveControls | Where-Object { $_.Cursor -ne [Windows.Forms.Cursors]::SizeAll }).Count -eq 0) 'Every passive corner-bar surface must be draggable'
    $heading = $corner.GetType().GetField('heading',$flags).GetValue($corner)
    $buttonNames = @('addTime','takeBreak','endDay','pause','doNotDisturb','minimize','resetPosition')
    $cornerButtons = @($buttonNames | ForEach-Object { $corner.GetType().GetField($_,$flags).GetValue($corner) })
    Assert ($heading.Cursor -eq [Windows.Forms.Cursors]::Hand -and $heading.AccessibleRole -eq [Windows.Forms.AccessibleRole]::PushButton -and @($cornerButtons | Where-Object { $_.Cursor -ne [Windows.Forms.Cursors]::Hand }).Count -eq 0) 'Only the open/close label and actual corner-bar buttons must use clickable affordances'
    $endDayButton = $corner.GetType().GetField('endDay',$flags).GetValue($corner)
    $pauseButton = $corner.GetType().GetField('pause',$flags).GetValue($corner)
    $dndButton = $corner.GetType().GetField('doNotDisturb',$flags).GetValue($corner)
    $trackingLabel = $corner.GetType().GetField('trackingHealth',$flags).GetValue($corner)
    Assert ($pauseButton.Left -gt $endDayButton.Right -and $dndButton.Left -gt $pauseButton.Right -and $dndButton.Right -lt $trackingLabel.Left) 'Pause and do not disturb must sit between End day and tracking status without overlap'
    $onClick = [Windows.Forms.Control].GetMethod('OnClick',$flags)
    $onClick.Invoke($passiveControls[0],@([EventArgs]::Empty))
    Assert (-not $app.Visible) 'Clicking a passive corner-bar surface must not open the dashboard'
    $usedBeforeDnd=$day.Used; $day.SinceReminder=$state.Interval*60-1
    $onClick.Invoke($dndButton,@([EventArgs]::Empty)); Elapsed 5 $false
    Assert ($state.DoNotDisturb -and $day.Used -gt $usedBeforeDnd -and $day.SinceReminder -eq 0 -and -not (Field 'cleanupActive')) 'Do not disturb must keep tracking screen time while suppressing periodic reminders and automatic breaks'
    Call 'BeginPeriodicCleanup'
    Assert (-not (Field 'cleanupActive') -and $dndButton.AccessibleName -eq 'Turn off do not disturb') 'Do not disturb must block periodic break cleanup and expose its active state'
    $onClick.Invoke($dndButton,@([EventArgs]::Empty))
    Assert (-not $state.DoNotDisturb -and $dndButton.AccessibleName -eq 'Turn on do not disturb') 'Clicking the crossed bell again must restore reminders'
    $onClick.Invoke($heading,@([EventArgs]::Empty))
    [System.Windows.Forms.Application]::DoEvents()
    Assert ($app.Visible) 'The invisible hit area around Click to open must open the dashboard'
    $onClick.Invoke($heading,@([EventArgs]::Empty))
    [System.Windows.Forms.Application]::DoEvents()
    Assert (-not $app.Visible) 'The same invisible hit area must close the dashboard'
    $onClick.Invoke($heading,@([EventArgs]::Empty))
    [System.Windows.Forms.Application]::DoEvents()
    $usedBeforePause = $day.Used; $reminderBeforePause = $day.SinceReminder
    $onClick.Invoke($pauseButton,@([EventArgs]::Empty)); [System.Windows.Forms.Application]::DoEvents()
    $appPauseOverlay = Field 'appPauseOverlay'
    $cornerPauseOverlay = $corner.GetType().GetField('pauseOverlay',$flags).GetValue($corner)
    Assert ((Field 'paused') -and $appPauseOverlay.Visible -and $cornerPauseOverlay.Visible -and $state.TrackingEvents[-1].Kind -eq 'Paused') 'Pausing must cover both Pace windows and record a manual-pause tracking event'
    Start-Sleep -Milliseconds 1100; Call 'Tick'
    Assert ($day.Used -eq $usedBeforePause -and $day.SinceReminder -eq $reminderBeforePause) 'Screen time and reminder time must remain frozen while Pace is paused'
    $onMouseDown = [Windows.Forms.Control].GetMethod('OnMouseDown',$flags)
    $resumeClick = New-Object Windows.Forms.MouseEventArgs ([Windows.Forms.MouseButtons]::Left),1,10,10,0
    $onMouseDown.Invoke($cornerPauseOverlay,@($resumeClick.PSObject.BaseObject)); [System.Windows.Forms.Application]::DoEvents()
    Assert (-not (Field 'paused') -and -not $appPauseOverlay.Visible -and -not $cornerPauseOverlay.Visible -and $state.TrackingEvents[-1].Kind -eq 'Resumed') 'Clicking the paused corner bar must resume Pace and close its tracking interval'
    Call 'PauseAll'; $onMouseDown.Invoke($appPauseOverlay,@($resumeClick.PSObject.BaseObject)); [System.Windows.Forms.Application]::DoEvents()
    Assert (-not (Field 'paused')) 'Clicking the paused dashboard must resume Pace'
    $app.Hide()
    $createParams = $corner.GetType().GetProperty('CreateParams', $flags).GetValue($corner, $null)
    Assert (($createParams.ExStyle -band 0x08000000) -ne 0) 'Corner must not activate when shown'
    $corner.MinimizeToTaskbar()
    [System.Windows.Forms.Application]::DoEvents()
    $minimizedParams = $corner.GetType().GetProperty('CreateParams', $flags).GetValue($corner, $null)
    Assert ($corner.IsMinimized -and $corner.ShowInTaskbar -and $corner.WindowState -eq [Windows.Forms.FormWindowState]::Minimized) 'Minimizing the corner bar must create a real taskbar entry'
    Assert (($minimizedParams.ExStyle -band 0x00040000) -ne 0 -and ($minimizedParams.ExStyle -band 0x00000080) -eq 0) 'The minimized bar must use an app-window style instead of a hidden tool-window style'
    $corner.RestoreBar()
    [System.Windows.Forms.Application]::DoEvents()
    Assert (-not $corner.IsMinimized -and -not $corner.ShowInTaskbar -and $corner.Visible -and $corner.WindowState -eq [Windows.Forms.FormWindowState]::Normal) 'Restoring the corner bar must make it visible and remove only its temporary taskbar entry'
    $corner.GetType().GetField('userPositioned',$flags).SetValue($corner,$true)
    $corner.Location = New-Object Drawing.Point 99999,99999
    $corner.EnsureVisible()
    $visibleArea = [Windows.Forms.Screen]::FromControl($corner).WorkingArea
    Assert ($corner.Left -ge $visibleArea.Left -and $corner.Right -le $visibleArea.Right -and $corner.Top -ge $visibleArea.Top -and $corner.Bottom -le $visibleArea.Bottom) 'The corner bar must return inside a current screen after a display-layout change'
    $app.GetType().GetMethod('Notify', $flags).Invoke($app, @('Test reminder', 'Silent layout verification'))
    $toast = Field 'toast'
    Assert ($toast.BackColor -eq [PaceTheme]::Reminder) 'Dark mode must carry into reminder windows'
    Assert ($toast.Bottom -lt $corner.Top) 'Reminder must sit above the persistent corner bar'
    Assert ($toast.GetType().GetProperty('ShowWithoutActivation', $flags).GetValue($toast, $null)) 'Reminder must show without stealing focus'
    Assert (($toast.Controls | Where-Object { $_ -is [Windows.Forms.Button] } | Select-Object -First 1).Text -eq 'Open weekly plan') 'The weekly-plan reminder must open the planner instead of starting an unrelated break'
    $toast.Close()

    $day.Used = 0; $day.FirstUseAt=''; $day.LastUseAt=''
    $day.SinceReminder = 0
    Elapsed 60 $false
    Assert ($day.Used -eq 60) 'Unlocked time must accrue automatically'
    Assert ([datetime]::Parse($day.FirstUseAt).Date -eq [datetime]::Today -and [datetime]::Parse($day.LastUseAt).Date -eq [datetime]::Today -and [datetime]::Parse($day.FirstUseAt).TimeOfDay -gt [timespan]::Zero) 'Actual computer use must save first-use and last-use bounds instead of defaulting the day to midnight'
    Assert ($day.SinceReminder -eq 60) 'Unlocked time must advance next-break countdown'
    Elapsed 60 $true
    Assert ($day.Used -eq 60) 'Locked time must not accrue'
    Assert ($day.SinceReminder -eq 60) 'Locked time must not advance next-break countdown'
    Call 'StartBreak'
    Assert ($state.TrackingEvents[$state.TrackingEvents.Count-1].Kind -eq 'BreakStarted') 'Starting a break must record a dedicated break event'
    Assert ([Math]::Abs(($state.BreakUntil - [datetime]::UtcNow).TotalMinutes - $state.BreakMinutes) -lt 0.1) 'Starting break must use the custom break duration'
    Assert ($day.SinceReminder -eq 0 -and $corner.AccessibleDescription -like '*BREAK LEFT:*') 'Starting break must reset reminder interval and show break countdown'
    Elapsed 60 $false
    Assert ($day.Used -eq 60) 'Full-screen break must stop screen-time accounting'
    Elapsed 60 $true
    Assert ($day.Used -eq 60) 'Locked time during a break must not accrue'
    $state.BreakUntil = [datetime]::MinValue
    $budget = $app.GetType().GetProperty('Budget', $flags).GetValue($app, $null)
    $day.Used = $budget - ($state.ClosingWarningMinutes * 60 + 1)
    Elapsed 2 $false
    Assert $day.Warned 'Crossing the custom 3m ending-warning point must trigger warning'
    Assert ((Field 'cleanupActive') -and (Field 'cleanupClosesPlan')) 'Closing warnings must keep a live countdown to shutdown'
    $closingMute=@((Field 'toast').Controls | Where-Object { $_ -is [Windows.Forms.Button] -and $_.Text -eq 'Mute sound' })[0]
    Assert ($null -ne $closingMute) 'A screen-time ending reminder must let the user mute its sound without dismissing the countdown'
    $closingMute.PerformClick()
    Assert ((Field 'cleanupActive') -and (Field 'cleanupMuted') -and -not $closingMute.Enabled -and $closingMute.Text -eq 'Sound muted') 'Muting a closing reminder must leave its countdown active and prevent later pings'
    Call 'CancelCleanup'
    Assert (Field 'cleanupActive') 'A closing countdown must remain visible instead of behaving like a cancelable periodic break'
    $day.Used = $budget - 1
    $day.BreakEarned = $true
    Elapsed 2 $false
    Assert ($day.Exhausted -and -not $day.BreakEarned) 'Exhaustion must trigger reminder and require a new break'
    $state.BreakUntil = [datetime]::UtcNow.AddSeconds(-1)
    $before = $day.Used
    Elapsed 5 $false
    Assert ($day.BreakEarned -and $state.BreakUntil -eq [datetime]::MinValue) 'Expired break must earn eligibility and clear the timer'
    Assert ($day.BreaksTaken -eq 1) 'A completed break must be counted for reporting'
    Assert ($day.Used -eq $before) 'Tracking must remain stopped until Continue'
    Assert ($day.SinceReminder -eq 0) 'Finishing break must reset the next reminder interval'
    $breakScreen = Field 'breakScreen'
    $breakFlags = [Reflection.BindingFlags]'NonPublic,Instance'
    Assert (-not $breakScreen.GetType().GetField('cancel',$breakFlags).GetValue($breakScreen).Visible -and -not $breakScreen.GetType().GetField('addMore',$breakFlags).GetValue($breakScreen).Visible) 'A completed break must show only the Continue action'
    $breakButtons = $breakScreen.GetType().GetField('buttons',$breakFlags).GetValue($breakScreen)
    $continueButton = $breakScreen.GetType().GetField('continueButton',$breakFlags).GetValue($breakScreen)
    Assert ([Math]::Abs(($continueButton.Left + $continueButton.Width/2) - $breakButtons.ClientSize.Width/2) -le 2) 'The available full-screen break action must be centered'
    $app.GetType().GetMethod('OffscreenActivity',$flags).Invoke($app,@())
    $breakScreen.GetType().GetMethod('UpdateTimer',$breakFlags).Invoke($breakScreen,@())
    Assert ($breakScreen.GetType().GetField('timerLabel',$breakFlags).GetValue($breakScreen).Text -eq '') 'Offscreen activity must stay timeless instead of restoring 00:00'
    Assert (-not $breakScreen.GetType().GetField('cancel',$breakFlags).GetValue($breakScreen).Visible) 'Offscreen activity must not duplicate Continue with Cancel'
    $app.GetType().GetMethod('ContinueBreak',$flags).Invoke($app,@())
    Assert ($state.TrackingEvents[$state.TrackingEvents.Count-1].Kind -eq 'OffscreenEnded') 'Continuing from offscreen activity must close its tracking interval'
    $state.DailyShutdown = $false
    Call 'StartBreak'
    $app.GetType().GetMethod('OffscreenActivity',$flags).Invoke($app,@())
    Assert ($state.TrackingEvents[$state.TrackingEvents.Count-2].Kind -eq 'BreakEnded' -and $state.TrackingEvents[$state.TrackingEvents.Count-1].Kind -eq 'OffscreenStarted') 'Switching from a break to offscreen activity must close the break and open a distinct offscreen interval'
    $app.GetType().GetMethod('ContinueBreak',$flags).Invoke($app,@())

    $updateInactivity=$app.GetType().GetMethod('UpdateInactivity',$flags)
    $state.InactivityWarningMinutes=12; $state.InactivityOffscreenMinutes=4
    $updateInactivity.Invoke($app,@([double](12*60+1)))
    $idleToast=Field 'inactivityToast'
    Assert ($null -ne $idleToast -and $idleToast.Visible -and $idleToast.Text -eq 'Inactivity warning') 'Crossing the inactivity warning threshold must show a dedicated silent warning'
    Assert ($idleToast -ne (Field 'toast')) 'The inactivity warning must not reuse the audio reminder window'
    $updateInactivity.Invoke($app,@([double]0))
    Assert ($null -eq (Field 'inactivityToast') -and -not (Field 'inactivityWarned')) 'Keyboard or mouse activity must dismiss and reset the inactivity warning'
    $updateInactivity.Invoke($app,@([double](16*60+1)))
    $breakScreen=Field 'breakScreen'
    $idleNote=$breakScreen.GetType().GetField('note',$breakFlags).GetValue($breakScreen)
    Assert ($state.BreakOffscreen -and $state.BreakWaiting -and $state.InactivityOffscreen -and $breakScreen.Visible) 'Continued inactivity must automatically enter the full-screen offscreen state'
    Assert ($idleNote.Text -like '*no keyboard or mouse input*') 'Automatic offscreen activity must explain that inactivity caused it'
    $beforeIdleOffscreen=$day.Used; Elapsed 60 $false
    Assert ($day.Used -eq $beforeIdleOffscreen) 'Screen-time accounting must stop while inactivity offscreen activity is active'
    Call 'ContinueBreak'
    Assert (-not $state.InactivityOffscreen -and -not $state.BreakOffscreen -and -not $state.BreakWaiting) 'Continue must restore normal tracking after inactivity offscreen activity'
    $state.DoNotDisturb=$true; $updateInactivity.Invoke($app,@([double](16*60+1)))
    Assert ($null -eq (Field 'inactivityToast') -and -not $state.BreakOffscreen) 'Do not disturb must suppress inactivity warnings and automatic offscreen activity while tracking continues'
    $state.DoNotDisturb=$false

    $day.Used = 0
    $day.SinceReminder = $state.Interval * 60 - 1
    Elapsed 2 $false
    Assert ($day.SinceReminder -eq 0) 'Regular reminder must restart its countdown'
    Assert ((Field 'cleanupActive') -and -not (Field 'cleanupClosesPlan')) 'Regular reminders must enter the periodic wrap-up phase'
    $periodicToast = Field 'toast'
    $periodicButtons = @($periodicToast.Controls | Where-Object { $_ -is [Windows.Forms.Button] })
    $cancelPeriodic=@($periodicButtons | Where-Object { $_.Text -eq 'Cancel break' })[0]; $startPeriodic=@($periodicButtons | Where-Object { $_.Text -eq 'Start break' })[0]; $mutePeriodic=@($periodicButtons | Where-Object { $_.Text -eq 'Mute sound' })[0]
    Assert ($periodicButtons.Count -eq 3 -and $null -ne $cancelPeriodic -and $null -ne $startPeriodic -and $null -ne $mutePeriodic) 'Every periodic break reminder must offer Cancel break, Start break, and Mute sound'
    for($a=0;$a -lt $periodicButtons.Count;$a++){for($b=$a+1;$b -lt $periodicButtons.Count;$b++){Assert (-not $periodicButtons[$a].Bounds.IntersectsWith($periodicButtons[$b].Bounds)) 'Periodic reminder actions must not overlap'}}
    $mutePeriodic.PerformClick()
    Assert ((Field 'cleanupActive') -and (Field 'cleanupMuted') -and -not $mutePeriodic.Enabled -and $mutePeriodic.Text -eq 'Sound muted') 'Mute sound must stop only the sound while preserving the periodic break countdown'
    $cancelPeriodic.PerformClick()
    Assert (-not (Field 'cleanupActive')) 'Cancel break must dismiss the periodic wrap-up without starting a break'
    Assert ($state.TrackingEvents[$state.TrackingEvents.Count-1].Kind -eq 'BreakCanceled') 'Canceling a periodic break must create a tracking-health marker'
    Call 'BeginPeriodicCleanup'
    Assert ([Math]::Abs(((Field 'cleanupUntil')-[datetime]::UtcNow).TotalMinutes - $state.PeriodicWrapMinutes) -lt 0.1) 'The periodic wrap-up countdown must use its custom duration'
    $startNow = @((Field 'toast').Controls | Where-Object { $_ -is [Windows.Forms.Button] -and $_.Text -eq 'Start break' })[0]
    $startNow.PerformClick()
    Assert ((Field 'state').BreakUntil -gt [datetime]::UtcNow -and -not (Field 'cleanupActive')) 'Start break must begin the full-screen break immediately'
    Call 'CancelBreak'
    Assert ($state.TrackingEvents[$state.TrackingEvents.Count-1].Kind -eq 'BreakEndedEarly') 'Leaving an active break before its timer finishes must create a separate early-exit marker'

    $currentPlan = $state.GetWeek($thisWeek)
    $currentPlan.Advanced = $true
    $activeBlock = New-Object AdvancedBlock
    $activeBlock.WeekStart = $thisWeek.ToString('yyyy-MM-dd'); $activeBlock.Day = [int][datetime]::Now.DayOfWeek; $activeBlock.Start = '00:00'; $activeBlock.End = '24:00'; $activeBlock.Activity = 'All-day test block'; $activeBlock.AllowedApps = 'Word, Canvas'
    $state.Blocks.Add($activeBlock)
    $day.ActiveBlock = ''; $day.BlockUsed = 0; $day.Used = 0; $day.Extra = 0; $day.ExtraUsed = 0; $day.Exhausted = $false
    Elapsed 60 $false
    Assert ($day.BlockUsed -eq 60 -and $day.Used -eq 60 -and $app.GetType().GetProperty('CurrentUsed',$flags).GetValue($app,$null) -eq 60) 'Advanced blocks must count both current-block usage and total reporting usage'
    $awareness = $app.GetType().GetMethod('UpdateBlockAwareness',$flags)
    $awareness.Invoke($app,@('Microsoft Word - Essay',[double]20))
    $awareness.Invoke($app,@('YouTube - Microsoft Edge',[double]121))
    Assert ($day.BlockActivities.Count -eq 2 -and $day.BlockActivities[0].Matched -and -not $day.BlockActivities[1].Matched) 'Activity-aware blocks must record matched and outside-block time separately'
    Call 'RefreshView'
    Assert ($corner.AccessibleDescription -like '*Focus cue:*All-day test block*YouTube*') 'A mismatch lasting beyond the grace period must appear on the persistent corner bar'
    $focusToast = Field 'focusToast'
    $focusButtons = @($focusToast.Controls | Where-Object { $_ -is [Windows.Forms.Button] })
    Assert ($focusButtons.Count -eq 1 -and $focusButtons[0].Text -eq 'Dismiss for 1h 15m') 'The mismatch reminder must show its custom snooze duration with hours and minutes'
    $focusButtons[0].PerformClick()
    Assert ($null -eq (Field 'focusToast') -and [Math]::Abs(((Field 'mismatchSnoozedUntil') - [datetime]::UtcNow).TotalMinutes - $state.MismatchSnoozeMinutes) -lt 0.1) 'Dismissing a mismatch must use the custom snooze duration'
    $awareness.Invoke($app,@('Canvas - Assignments',[double]1))
    Call 'RefreshView'
    Assert ($corner.AccessibleDescription -notlike 'Focus cue:*') 'Returning to a matching app must clear the corner mismatch immediately'
    $actualBlockRemaining = $app.GetType().GetProperty('CurrentRemaining',$flags).GetValue($app,$null)
    Assert ($actualBlockRemaining -le ([datetime]::Today.AddDays(1)-[datetime]::Now).TotalSeconds + 2) 'Advanced remaining time must follow the clock instead of claiming the full block duration'
    $currentPlan.Hybrid=$true; $todayPlanIndex=(([int][datetime]::Now.DayOfWeek+6)%7); $currentPlan.Minutes[$todayPlanIndex]=3
    $day.ActiveBlock=''; $day.BlockUsed=0; $day.Used=60; $day.Extra=0; $day.ExtraUsed=0; $day.Exhausted=$false; $day.Warned=$false; $state.DailyShutdown=$false; $state.BreakOffscreen=$false
    Elapsed 30 $false
    Assert ($day.BlockUsed -eq 30 -and $day.Used -eq 90 -and $app.GetType().GetProperty('CurrentUsed',$flags).GetValue($app,$null) -eq 90 -and $app.GetType().GetProperty('Budget',$flags).GetValue($app,$null) -eq 180) 'Limits + blocks must preserve used time across blocks and compare it with the overarching daily limit'
    Elapsed 91 $false
    Assert ($state.DailyShutdown -and $day.Used -ge 180 -and ($app.GetType().GetMethod('DailyShutdownMessage',$flags).Invoke($app,@())) -like '*daily screen-time limit*') 'A combined plan must shut down when its daily limit is consumed even while a block remains active'
    $state.DailyShutdown=$false; $state.BreakOffscreen=$false; $day.Exhausted=$false; $currentPlan.Hybrid=$false; $day.Used=60; $day.BlockUsed=60
    if ($breakScreen) { $breakScreen.Hide() }
    Call 'StartManualShutdown'; Call 'Tick'
    Assert ($state.DailyShutdown -and $state.ManualShutdown) 'A manual End screen time choice must not be undone by an active advanced block'
    $state.DailyShutdown = $false; $state.ManualShutdown = $false; $state.BreakOffscreen = $false; $day.Exhausted = $false
    if ($breakScreen) { $breakScreen.Hide() }
    $state.Blocks.Clear()
    $currentPlan.Hybrid=$true; $day.Extra=10; $day.ExtraUsed=0; $day.Used=0
    Call 'Tick'
    Assert ($state.DailyShutdown -and -not $state.ManualShutdown) 'Limits + blocks must remain offscreen outside an allowed block even when general daily time remains'
    $state.DailyShutdown=$false; $state.BreakOffscreen=$false; $day.Exhausted=$false; $currentPlan.Hybrid=$false; $day.Extra=0
    if ($breakScreen) { $breakScreen.Hide() }
    Call 'Tick'
    Assert ($state.DailyShutdown -and -not $state.ManualShutdown) 'Advanced plans must close screen time automatically whenever no block or extra time is active'
    $state.DailyShutdown = $false; $state.BreakOffscreen = $false; $day.Exhausted = $false
    if ($breakScreen) { $breakScreen.Hide() }
    $day.ActiveBlock = 'extra'
    $day.BlockUsed = 0
    $day.Extra = 2
    $day.ExtraUsed = 0
    $day.Used = 0
    $day.Exhausted = $false
    $day.Warned = $false
    Elapsed 60 $false
    Assert ($day.ExtraUsed -eq 60 -and $day.Used -eq 60 -and -not $state.DailyShutdown) 'Advanced extra time outside a block must count down and remain usable'
    Elapsed 61 $false
    Assert $state.DailyShutdown 'Advanced extra time must close screen time when the added allowance is consumed'
    $state.DailyShutdown = $false; $state.BreakOffscreen = $false; $day.Exhausted = $false; $day.Extra = 0; $day.ExtraUsed = 0; $currentPlan.Advanced = $false
    if ($breakScreen) { $breakScreen.Hide() }

    [void]$state.Weeks.Remove($currentPlan)
    $day.Exhausted = $false
    $day.Warned = $false
    $before = $day.Used
    Elapsed 5 $false
    Assert ($day.Used -eq $before + 5 -and -not $day.Exhausted -and -not $day.Warned) 'Missing plan must count usage without budget warnings'
    $state.Weeks.Add($currentPlan)
    $day.Activities.Clear(); $day.Activities.Add((New-Object ActivityRecord -Property @{ Name='Outlook'; Category='Communication'; Seconds=120 }))
    $reportDate = if ([datetime]::Now.Date -eq $thisWeek) { $thisWeek.AddDays(1) } else { $thisWeek }
    $reportDay = New-Object DayRecord -Property @{ Date=$reportDate.ToString('yyyy-MM-dd'); Used=180; Extra=5; BreaksTaken=1 }
    $reportDay.Activities.Add((New-Object ActivityRecord -Property @{ Name='Microsoft Teams'; Category='Communication'; Seconds=180 }))
    $reportDay.BlockActivities.Add((New-Object BlockActivityRecord -Property @{ BlockKey='report'; BlockName='Homework'; Activity='Microsoft Word'; Matched=$true; Seconds=180 }))
    $reportDay.BlockActivities.Add((New-Object BlockActivityRecord -Property @{ BlockKey='report'; BlockName='Homework'; Activity='YouTube'; Matched=$false; Seconds=60 }))
    $state.Days.Add($reportDay)
    (Field 'activityRange').SelectedIndex = 1; Call 'RefreshActivities'
    $reportItems = @((Field 'activityList').Items)
    Assert ($reportItems -contains 'Communication: 5m') 'Weekly reporting must combine activity across the current week'
    Assert (($reportItems | Where-Object { $_ -like 'BLOCK FIT:*' }).Count -eq 1 -and ($reportItems | Where-Object { $_ -like 'Outside*Homework*YouTube*' }).Count -eq 1) 'Reporting must summarize block fit and name the activities used outside a planned block'
    Assert ((Field 'activitySummary').Text -like 'This week:*planned*extra*break*') 'Weekly reporting must summarize usage, plans, extra time, and completed breaks'
    Assert ((Field 'activitySummary').Text -like '*block fit') 'Weekly reporting must include the advanced-block fit percentage when matching data exists'
    Assert ((Field 'activityChart').AccessibleDescription -like 'Communication 5 minutes*') 'The activity visualization must expose its data to accessibility tools'
    [void]$state.Days.Remove($reportDay); $day.Activities.Clear(); (Field 'activityRange').SelectedIndex = 0
    $day.Extra = 15
    $day.ActiveReason = 'Testing visible extra-time reason'
    $day.Reasons.Add('Testing saved reason')
    $state.AlertVolume = 73
    $state.CustomAlertFile = 'custom-alert.wav'
    $state.UseCustomAlert = $false; $state.BuiltInAlert = 'Ocean wash'
    $state.DoNotDisturb = $true
    $currentPlan.Advanced = $true; $currentPlan.Hybrid = $true
    $state.Blocks.Add($blockA)
    $invalidBlock = New-Object AdvancedBlock
    $invalidBlock.WeekStart = $blockA.WeekStart; $invalidBlock.Day = 1; $invalidBlock.Start = 'bad'; $invalidBlock.End = '10:00'; $invalidBlock.Activity = ''
    $state.Blocks.Add($invalidBlock)
    Call 'Save'
    Call 'LoadState'
    $loaded = Field 'state'
    Assert ($loaded.Weeks.Count -eq 2 -and $loaded.GetWeek($nextWeek).Minutes[1] -eq 70) 'Dated plans must survive reload'
    Assert ($loaded.GetWeek($thisWeek).Advanced -and $loaded.GetWeek($thisWeek).Hybrid -and $loaded.Blocks.Count -eq 1 -and $loaded.Blocks[0].AllowedApps -eq 'Word, Canvas') 'Combined plan mode and normalized activity matching words must survive reload while invalid blocks are discarded'
    Assert (-not $loaded.GetWeek($nextWeek).Advanced) 'Advanced plan mode must stay attached to its own week'
    Assert ($loaded.Days[0].Used -eq $day.Used -and $loaded.Days[0].Extra -eq 15 -and $loaded.Days[0].Reasons[0] -eq 'Testing saved reason' -and $loaded.Days[0].ActiveReason -eq 'Testing visible extra-time reason') 'Usage, extra time, and reasons must survive reload'
    Assert ($loaded.AlertVolume -eq 73 -and $loaded.CustomAlertFile -eq 'custom-alert.wav' -and -not $loaded.UseCustomAlert -and $loaded.BuiltInAlert -eq 'Ocean wash' -and $loaded.DoNotDisturb) 'Reminder volume, sound selection, and do-not-disturb state must survive reload'
    Assert ($loaded.Interval -eq 75 -and $loaded.BreakMinutes -eq 90 -and $loaded.PeriodicWrapMinutes -eq 7 -and $loaded.ClosingWarningMinutes -eq 3 -and $loaded.MismatchGraceMinutes -eq 2 -and $loaded.MismatchSnoozeMinutes -eq 75 -and $loaded.InactivityWarningMinutes -eq 12 -and $loaded.InactivityOffscreenMinutes -eq 4 -and $loaded.AutoCopyPreviousWeek -and $loaded.DarkMode) 'All custom timing, weekly copying, and appearance settings must survive reload'
    Call 'Save'
    Set-Content -LiteralPath (Join-Path $tempFolder 'state.xml') -Value '<damaged>'
    Call 'LoadState'
    $loaded = Field 'state'
    Assert ($loaded.Weeks.Count -eq 2 -and $loaded.Days[0].Reasons[0] -eq 'Testing saved reason') 'A damaged primary state file must recover plans and history from the automatic backup'
    $app.GetType().GetField('day',$flags).SetValue($app,$null); Call 'Today'; $day = Field 'day'
    $loaded.DailyShutdown = $true
    $loaded.BreakWaiting = $true
    $loaded.BreakOffscreen = $true
    $loaded.InactivityOffscreen = $true
    $loaded.BreakUntil = [datetime]::UtcNow.AddMinutes(5)
    $app.GetType().GetMethod('ResetForNewDay',$flags).Invoke($app,@())
    Assert (-not $loaded.DailyShutdown -and -not $loaded.ManualShutdown -and -not $loaded.BreakWaiting -and -not $loaded.BreakOffscreen -and -not $loaded.InactivityOffscreen -and $loaded.BreakUntil -eq [datetime]::MinValue -and $loaded.SessionDate -eq [datetime]::Now.ToString('yyyy-MM-dd')) 'A new day must automatically clear the previous shutdown and break states'
    $savedArea = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea; $savedX=$savedArea.Left+40; $savedY=$savedArea.Top+40
    $loaded.CornerPositioned=$true; $loaded.CornerX=$savedX; $loaded.CornerY=$savedY; $loaded.Days[0].ActiveReason=''
    $loaded.SessionDate = [datetime]::Now.AddDays(-1).ToString('yyyy-MM-dd'); $loaded.DailyShutdown = $true; $loaded.BreakOffscreen = $true
    Call 'Save'
    $app.Dispose(); $app = New-Object ScreenTime -ArgumentList $tempFolder; (Field 'timer').Stop()
    $restarted = Field 'state'
    Assert (-not $restarted.DailyShutdown -and -not $restarted.BreakOffscreen -and $restarted.SessionDate -eq [datetime]::Now.ToString('yyyy-MM-dd')) 'Restarting on a new day must not restore yesterday''s full-screen shutdown'
    $restartedCorner = Field 'cornerBar'
    Assert ($restarted.CornerPositioned -and $restartedCorner.Left -eq $savedX -and $restartedCorner.Top -eq $savedY) 'A user-positioned corner bar must return to its saved location after restart'
    Assert ($restarted.TrackingSessionOpen -and ($restarted.TrackingEvents | Where-Object { $_.Kind -eq 'Stopped' }).Count -ge 1) 'A clean exit and restart must preserve tracking continuity events and open a new session'
    Write-Host 'PASS: planning, customizable timing, dark mode, activity-aware blocks, tracking continuity, health timeline, reporting, recovery, positioning, accounting, breaks, reminders, and persistence.'
} finally {
    if ($app) { $app.Dispose() }
    # Delete only the known test files and empty directory; never touch real application data.
    foreach ($name in @('state.xml', 'state.xml.tmp', 'state.xml.backup', 'short-alert.wav')) {
        $testFile = Join-Path $tempFolder $name
        if (Test-Path -LiteralPath $testFile) { Remove-Item -LiteralPath $testFile }
    }
    if (Test-Path -LiteralPath $tempFolder) { Remove-Item -LiteralPath $tempFolder }
}
