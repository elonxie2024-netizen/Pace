$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
[void][Reflection.Assembly]::LoadFrom("$PSScriptRoot\ScreenTime.exe")
$flags = [Reflection.BindingFlags]'NonPublic,Instance'
$tempFolder = Join-Path $env:TEMP ('ScreenTime-test-' + [guid]::NewGuid())
$app = $null
function Field($name) { $app.GetType().GetField($name, $flags).GetValue($app) }
function Call($name) { $app.GetType().GetMethod($name, $flags).Invoke($app, @()) }
function Elapsed([double]$seconds, [bool]$locked) { $app.GetType().GetMethod('ApplyElapsed', $flags).Invoke($app, @($seconds, $locked)) }
function Assert($condition, $message) { if (-not $condition) { throw $message } }
try {
    $app = New-Object ScreenTime -ArgumentList $tempFolder
    (Field 'timer').Stop()
    $state = Field 'state'
    $state.AlertVolume = 0
    $day = Field 'day'
    Assert ($state.Days.Count -eq 1 -and $state.Weeks.Count -eq 0) 'Isolated constructor must begin with fresh state'
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
    Assert ($corner.AccessibleDescription -eq 'Allotted: 2h. Used: 1h 1m. NEXT BREAK: 01:01') 'Corner must display allotment, usage, and next break'
    $corner.UpdateStatus($false, 0, 120, 10, $false)
    Assert ($corner.AccessibleDescription -like 'Allotted: No plan. Used: 2m.*') 'Unplanned corner must still show usage'
    $corner.UpdateStatus($true, 60, 120, 90, $true)
    Assert ($corner.AccessibleDescription -like '*BREAK LEFT: 01:30') 'Active break must change countdown label'
    $corner.UpdateStatus($true, 60, 120, 0, 'DAY COMPLETE')
    Assert ($corner.AccessibleDescription -like '*DAY COMPLETE: 00:00') 'The corner must distinguish a completed day from a break'
    $normalCornerHeight = $corner.ClientSize.Height
    $corner.SetReason('Finish the history outline')
    $reasonLabel = $corner.GetType().GetField('reason', $flags).GetValue($corner)
    $allottedTitle = $corner.GetType().GetField('allottedTitle', $flags).GetValue($corner)
    Assert ($reasonLabel.Text -eq 'Finish the history outline' -and $corner.ClientSize.Height -gt $normalCornerHeight) 'An active extra-time reason must receive a large dedicated area on the corner bar'
    Assert ($reasonLabel.Bottom -lt $allottedTitle.Top -and $corner.AccessibleDescription -like 'Reason: Finish the history outline.*') 'The reason must not overlap the existing corner-bar controls'
    $corner.SetReason('')
    Assert ($reasonLabel.Text -eq '' -and $corner.ClientSize.Height -eq $normalCornerHeight) 'The corner bar must return to its compact layout when no reason is active'
    $corner.SetFocusMismatch('Homework','YouTube - Microsoft Edge')
    Assert ($corner.AccessibleDescription -like 'Focus cue: PLANNED: Homework*YouTube*outside this block*') 'An activity mismatch must receive a clear accessible course-check message'
    Assert ($reasonLabel.Bottom -lt $allottedTitle.Top) 'The activity mismatch must expand the corner bar without overlapping its controls'
    $corner.SetReason('')
    $cornerProgress = $corner.GetType().GetField('progress', $flags).GetValue($corner)
    Assert ($cornerProgress.Value -eq 1000) 'Over-budget progress must clamp at maximum'
    Call 'RefreshView'
    Assert ($corner.AccessibleDescription -like 'Allotted: 1h 30m. Used: 2m.*') 'Dashboard refresh must update the corner from current state'
    $app.Show()
    [System.Windows.Forms.Application]::DoEvents()
    $app.Hide()
    Assert ($corner.Visible -and -not $app.Visible -and $corner.TopMost) 'Corner must remain visible above other windows when dashboard is hidden'
    $createParams = $corner.GetType().GetProperty('CreateParams', $flags).GetValue($corner, $null)
    Assert (($createParams.ExStyle -band 0x08000000) -ne 0) 'Corner must not activate when shown'
    $app.GetType().GetMethod('Notify', $flags).Invoke($app, @('Test reminder', 'Silent layout verification'))
    $toast = Field 'toast'
    Assert ($toast.Bottom -lt $corner.Top) 'Reminder must sit above the persistent corner bar'
    Assert ($toast.GetType().GetProperty('ShowWithoutActivation', $flags).GetValue($toast, $null)) 'Reminder must show without stealing focus'
    Assert (($toast.Controls | Where-Object { $_ -is [Windows.Forms.Button] } | Select-Object -First 1).Text -eq 'Open weekly plan') 'The weekly-plan reminder must open the planner instead of starting an unrelated break'
    $toast.Close()

    $day.Used = 0
    $day.SinceReminder = 0
    Elapsed 60 $false
    Assert ($day.Used -eq 60) 'Unlocked time must accrue automatically'
    Assert ($day.SinceReminder -eq 60) 'Unlocked time must advance next-break countdown'
    Elapsed 60 $true
    Assert ($day.Used -eq 60) 'Locked time must not accrue'
    Assert ($day.SinceReminder -eq 60) 'Locked time must not advance next-break countdown'
    Call 'StartBreak'
    Assert ($state.BreakUntil -gt [datetime]::UtcNow) 'Starting break must set a future timer'
    Assert ($day.SinceReminder -eq 0 -and $corner.AccessibleDescription -like '*BREAK LEFT:*') 'Starting break must reset reminder interval and show break countdown'
    Elapsed 60 $false
    Assert ($day.Used -eq 60) 'Full-screen break must stop screen-time accounting'
    Elapsed 60 $true
    Assert ($day.Used -eq 60) 'Locked time during a break must not accrue'
    $state.BreakUntil = [datetime]::MinValue
    $budget = $app.GetType().GetProperty('Budget', $flags).GetValue($app, $null)
    $day.Used = $budget - 601
    Elapsed 2 $false
    Assert $day.Warned 'Crossing ten minutes remaining must trigger warning'
    Assert ((Field 'cleanupActive') -and (Field 'cleanupClosesPlan')) 'Closing warnings must keep a live countdown to shutdown'
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
    $state.DailyShutdown = $false

    $day.Used = 0
    $day.SinceReminder = $state.Interval * 60 - 1
    Elapsed 2 $false
    Assert ($day.SinceReminder -eq 0) 'Regular reminder must restart its countdown'
    Assert ((Field 'cleanupActive') -and -not (Field 'cleanupClosesPlan')) 'Regular reminders must enter the periodic wrap-up phase'
    $periodicToast = Field 'toast'
    $periodicButtons = @($periodicToast.Controls | Where-Object { $_ -is [Windows.Forms.Button] })
    Assert ($periodicButtons.Count -eq 2 -and $periodicButtons[0].Text -eq 'Cancel break' -and $periodicButtons[1].Text -eq 'Start break') 'Every periodic break reminder must offer Cancel break and Start break'
    Assert (-not $periodicButtons[0].Bounds.IntersectsWith($periodicButtons[1].Bounds)) 'Periodic reminder actions must not overlap'
    $periodicButtons[0].PerformClick()
    Assert (-not (Field 'cleanupActive')) 'Cancel break must dismiss the periodic wrap-up without starting a break'
    Call 'BeginPeriodicCleanup'
    $startNow = @((Field 'toast').Controls | Where-Object { $_ -is [Windows.Forms.Button] -and $_.Text -eq 'Start break' })[0]
    $startNow.PerformClick()
    Assert ((Field 'state').BreakUntil -gt [datetime]::UtcNow -and -not (Field 'cleanupActive')) 'Start break must begin the full-screen break immediately'
    Call 'CancelBreak'

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
    $awareness.Invoke($app,@('YouTube - Microsoft Edge',[double]16))
    Assert ($day.BlockActivities.Count -eq 2 -and $day.BlockActivities[0].Matched -and -not $day.BlockActivities[1].Matched) 'Activity-aware blocks must record matched and outside-block time separately'
    Call 'RefreshView'
    Assert ($corner.AccessibleDescription -like 'Focus cue:*All-day test block*YouTube*') 'A mismatch lasting beyond the grace period must appear on the persistent corner bar'
    $focusToast = Field 'focusToast'
    $focusButtons = @($focusToast.Controls | Where-Object { $_ -is [Windows.Forms.Button] })
    Assert ($focusButtons.Count -eq 1 -and $focusButtons[0].Text -eq 'Dismiss for 5 min') 'The mismatch reminder must have one clear action with a real snooze effect'
    $focusButtons[0].PerformClick()
    Assert ($null -eq (Field 'focusToast') -and (Field 'mismatchSnoozedUntil') -gt [datetime]::UtcNow) 'Dismissing a mismatch must snooze its pop-up for five minutes'
    $awareness.Invoke($app,@('Canvas - Assignments',[double]1))
    Call 'RefreshView'
    Assert ($corner.AccessibleDescription -notlike 'Focus cue:*') 'Returning to a matching app must clear the corner mismatch immediately'
    $actualBlockRemaining = $app.GetType().GetProperty('CurrentRemaining',$flags).GetValue($app,$null)
    Assert ($actualBlockRemaining -le ([datetime]::Today.AddDays(1)-[datetime]::Now).TotalSeconds + 2) 'Advanced remaining time must follow the clock instead of claiming the full block duration'
    Call 'StartManualShutdown'; Call 'Tick'
    Assert ($state.DailyShutdown -and $state.ManualShutdown) 'A manual End screen time choice must not be undone by an active advanced block'
    $state.DailyShutdown = $false; $state.ManualShutdown = $false; $state.BreakOffscreen = $false; $day.Exhausted = $false
    if ($breakScreen) { $breakScreen.Hide() }
    $state.Blocks.Clear()
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
    $currentPlan.Advanced = $true
    $state.Blocks.Add($blockA)
    $invalidBlock = New-Object AdvancedBlock
    $invalidBlock.WeekStart = $blockA.WeekStart; $invalidBlock.Day = 1; $invalidBlock.Start = 'bad'; $invalidBlock.End = '10:00'; $invalidBlock.Activity = ''
    $state.Blocks.Add($invalidBlock)
    Call 'Save'
    Call 'LoadState'
    $loaded = Field 'state'
    Assert ($loaded.Weeks.Count -eq 2 -and $loaded.GetWeek($nextWeek).Minutes[1] -eq 70) 'Dated plans must survive reload'
    Assert ($loaded.GetWeek($thisWeek).Advanced -and $loaded.Blocks.Count -eq 1 -and $loaded.Blocks[0].AllowedApps -eq 'Word, Canvas') 'Advanced plan mode and normalized activity matching words must survive reload while invalid blocks are discarded'
    Assert (-not $loaded.GetWeek($nextWeek).Advanced) 'Advanced plan mode must stay attached to its own week'
    Assert ($loaded.Days[0].Used -eq $day.Used -and $loaded.Days[0].Extra -eq 15 -and $loaded.Days[0].Reasons[0] -eq 'Testing saved reason' -and $loaded.Days[0].ActiveReason -eq 'Testing visible extra-time reason') 'Usage, extra time, and reasons must survive reload'
    Assert ($loaded.AlertVolume -eq 73) 'Reminder volume must survive reload'
    Call 'Save'
    Set-Content -LiteralPath (Join-Path $tempFolder 'state.xml') -Value '<damaged>'
    Call 'LoadState'
    $loaded = Field 'state'
    Assert ($loaded.Weeks.Count -eq 2 -and $loaded.Days[0].Reasons[0] -eq 'Testing saved reason') 'A damaged primary state file must recover plans and history from the automatic backup'
    $app.GetType().GetField('day',$flags).SetValue($app,$null); Call 'Today'; $day = Field 'day'
    $loaded.DailyShutdown = $true
    $loaded.BreakWaiting = $true
    $loaded.BreakOffscreen = $true
    $loaded.BreakUntil = [datetime]::UtcNow.AddMinutes(5)
    $app.GetType().GetMethod('ResetForNewDay',$flags).Invoke($app,@())
    Assert (-not $loaded.DailyShutdown -and -not $loaded.ManualShutdown -and -not $loaded.BreakWaiting -and -not $loaded.BreakOffscreen -and $loaded.BreakUntil -eq [datetime]::MinValue -and $loaded.SessionDate -eq [datetime]::Now.ToString('yyyy-MM-dd')) 'A new day must automatically clear the previous shutdown and break states'
    $savedArea = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea; $savedX=$savedArea.Left+40; $savedY=$savedArea.Top+40
    $loaded.CornerPositioned=$true; $loaded.CornerX=$savedX; $loaded.CornerY=$savedY; $loaded.Days[0].ActiveReason=''
    $loaded.SessionDate = [datetime]::Now.AddDays(-1).ToString('yyyy-MM-dd'); $loaded.DailyShutdown = $true; $loaded.BreakOffscreen = $true
    Call 'Save'
    $app.Dispose(); $app = New-Object ScreenTime -ArgumentList $tempFolder; (Field 'timer').Stop()
    $restarted = Field 'state'
    Assert (-not $restarted.DailyShutdown -and -not $restarted.BreakOffscreen -and $restarted.SessionDate -eq [datetime]::Now.ToString('yyyy-MM-dd')) 'Restarting on a new day must not restore yesterday''s full-screen shutdown'
    $restartedCorner = Field 'cornerBar'
    Assert ($restarted.CornerPositioned -and $restartedCorner.Left -eq $savedX -and $restartedCorner.Top -eq $savedY) 'A user-positioned corner bar must return to its saved location after restart'
    Write-Host 'PASS: planning, activity-aware blocks, reporting, recovery, positioning, accounting, breaks, reminders, and persistence.'
} finally {
    if ($app) { $app.Dispose() }
    # Delete only the known test files and empty directory; never touch real application data.
    foreach ($name in @('state.xml', 'state.xml.tmp', 'state.xml.backup')) {
        $testFile = Join-Path $tempFolder $name
        if (Test-Path -LiteralPath $testFile) { Remove-Item -LiteralPath $testFile }
    }
    if (Test-Path -LiteralPath $tempFolder) { Remove-Item -LiteralPath $tempFolder }
}
