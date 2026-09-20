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
    $app.GetType().GetMethod('ContinueBreak',$flags).Invoke($app,@())
    $state.DailyShutdown = $false

    $day.Used = 0
    $day.SinceReminder = $state.Interval * 60 - 1
    Elapsed 2 $false
    Assert ($day.SinceReminder -eq 0) 'Regular reminder must restart its countdown'

    $currentPlan = $state.GetWeek($thisWeek)
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
    Call 'Save'
    Call 'LoadState'
    $loaded = Field 'state'
    Assert ($loaded.Weeks.Count -eq 2 -and $loaded.GetWeek($nextWeek).Minutes[1] -eq 70) 'Dated plans must survive reload'
    Assert ($loaded.Days[0].Used -eq $day.Used -and $loaded.Days[0].Extra -eq 15 -and $loaded.Days[0].Reasons[0] -eq 'Testing saved reason') 'Usage, extra time, and reasons must survive reload'
    Assert ($loaded.AlertVolume -eq 73) 'Reminder volume must survive reload'
    Write-Host 'PASS: weekly plans, automatic accounting, breaks, warnings, persistent corner, reminder placement, countdowns, and persistence including volume.'
} finally {
    if ($app) { $app.Dispose() }
    # Delete only the known test files and empty directory; never touch real application data.
    foreach ($name in @('state.xml', 'state.xml.tmp')) {
        $testFile = Join-Path $tempFolder $name
        if (Test-Path -LiteralPath $testFile) { Remove-Item -LiteralPath $testFile }
    }
    if (Test-Path -LiteralPath $tempFolder) { Remove-Item -LiteralPath $tempFolder }
}
