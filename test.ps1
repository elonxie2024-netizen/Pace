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
    $blockA.WeekStart = $thisWeek.ToString('yyyy-MM-dd'); $blockA.Day = 1; $blockA.Start = '09:00'; $blockA.End = '10:00'; $blockA.Activity = 'School'
    $blockB = New-Object AdvancedBlock
    $blockB.WeekStart = $blockA.WeekStart; $blockB.Day = 1; $blockB.Start = '09:45'; $blockB.End = '11:00'; $blockB.Activity = 'Creative work'
    $blockC = New-Object AdvancedBlock
    $blockC.WeekStart = $blockA.WeekStart; $blockC.Day = 1; $blockC.Start = '10:00'; $blockC.End = '11:00'; $blockC.Activity = 'Reading'
    Assert ([AdvancedBlockRules]::Overlaps($blockA,$blockB)) 'Advanced blocks on the same day must detect overlap'
    Assert (-not [AdvancedBlockRules]::Overlaps($blockA,$blockC)) 'Adjacent advanced blocks must be allowed'
    $differentWeekBlock = [AdvancedBlockRules]::Copy($blockA,$nextWeek.ToString('yyyy-MM-dd'))
    Assert (-not [AdvancedBlockRules]::Overlaps($blockA,$differentWeekBlock)) 'Blocks in different weeks must not conflict with each other'
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
    $corner.UpdateStatus($true, 7200, 3660, 61, $false)
    Assert ($corner.AccessibleDescription -eq 'Allotted: 2h. Used: 1h 1m. NEXT BREAK: 01:01') 'Corner must display allotment, usage, and next break'
    $corner.UpdateStatus($false, 0, 120, 10, $false)
    Assert ($corner.AccessibleDescription -like 'Allotted: No plan. Used: 2m.*') 'Unplanned corner must still show usage'
    $corner.UpdateStatus($true, 60, 120, 90, $true)
    Assert ($corner.AccessibleDescription -like '*BREAK LEFT: 01:30') 'Active break must change countdown label'
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
    Assert ($day.Used -eq $before) 'Tracking must remain stopped until Continue'
    Assert ($day.SinceReminder -eq 0) 'Finishing break must reset the next reminder interval'
    $breakScreen = Field 'breakScreen'
    $breakFlags = [Reflection.BindingFlags]'NonPublic,Instance'
    Assert (-not $breakScreen.GetType().GetField('cancel',$breakFlags).GetValue($breakScreen).Visible -and -not $breakScreen.GetType().GetField('addMore',$breakFlags).GetValue($breakScreen).Visible) 'A completed break must show only the Continue action'
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
    Call 'CancelCleanup'

    $currentPlan = $state.GetWeek($thisWeek)
    $currentPlan.Advanced = $true
    $activeBlock = New-Object AdvancedBlock
    $activeBlock.WeekStart = $thisWeek.ToString('yyyy-MM-dd'); $activeBlock.Day = [int][datetime]::Now.DayOfWeek; $activeBlock.Start = '00:00'; $activeBlock.End = '24:00'; $activeBlock.Activity = 'All-day test block'
    $state.Blocks.Add($activeBlock)
    $day.ActiveBlock = ''; $day.BlockUsed = 0; $day.Used = 0; $day.Extra = 0; $day.ExtraUsed = 0; $day.Exhausted = $false
    Elapsed 60 $false
    Assert ($day.BlockUsed -eq 60 -and $day.Used -eq 60 -and $app.GetType().GetProperty('CurrentUsed',$flags).GetValue($app,$null) -eq 60) 'Advanced blocks must count both current-block usage and total reporting usage'
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
    $day.Extra = 15
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
    Assert ($loaded.GetWeek($thisWeek).Advanced -and $loaded.Blocks.Count -eq 1) 'Advanced plan mode must survive reload while invalid blocks are discarded'
    Assert (-not $loaded.GetWeek($nextWeek).Advanced) 'Advanced plan mode must stay attached to its own week'
    Assert ($loaded.Days[0].Used -eq $day.Used -and $loaded.Days[0].Extra -eq 15 -and $loaded.Days[0].Reasons[0] -eq 'Testing saved reason') 'Usage, extra time, and reasons must survive reload'
    Assert ($loaded.AlertVolume -eq 73) 'Reminder volume must survive reload'
    $loaded.DailyShutdown = $true
    $loaded.BreakWaiting = $true
    $loaded.BreakOffscreen = $true
    $loaded.BreakUntil = [datetime]::UtcNow.AddMinutes(5)
    $app.GetType().GetMethod('ResetForNewDay',$flags).Invoke($app,@())
    Assert (-not $loaded.DailyShutdown -and -not $loaded.ManualShutdown -and -not $loaded.BreakWaiting -and -not $loaded.BreakOffscreen -and $loaded.BreakUntil -eq [datetime]::MinValue -and $loaded.SessionDate -eq [datetime]::Now.ToString('yyyy-MM-dd')) 'A new day must automatically clear the previous shutdown and break states'
    $loaded.SessionDate = [datetime]::Now.AddDays(-1).ToString('yyyy-MM-dd'); $loaded.DailyShutdown = $true; $loaded.BreakOffscreen = $true
    Call 'Save'
    $app.Dispose(); $app = New-Object ScreenTime -ArgumentList $tempFolder; (Field 'timer').Stop()
    $restarted = Field 'state'
    Assert (-not $restarted.DailyShutdown -and -not $restarted.BreakOffscreen -and $restarted.SessionDate -eq [datetime]::Now.ToString('yyyy-MM-dd')) 'Restarting on a new day must not restore yesterday''s full-screen shutdown'
    Write-Host 'PASS: weekly plans, visual advanced blocks, dragging, conflict rules, automatic accounting, breaks, warnings, reminders, and persistence.'
} finally {
    if ($app) { $app.Dispose() }
    # Delete only the known test files and empty directory; never touch real application data.
    foreach ($name in @('state.xml', 'state.xml.tmp')) {
        $testFile = Join-Path $tempFolder $name
        if (Test-Path -LiteralPath $testFile) { Remove-Item -LiteralPath $testFile }
    }
    if (Test-Path -LiteralPath $tempFolder) { Remove-Item -LiteralPath $tempFolder }
}
