param([Parameter(Mandatory=$true)][string]$Message)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$last = [int64](Get-Content .git/simulated-date)
$r = Get-Random -Maximum 100
if ($r -lt 80) { $adv = 3600 + (Get-Random -Maximum 82800) }
elseif ($r -lt 95) { $adv = 86400 + (Get-Random -Maximum 172800) }
else { $adv = 259200 + (Get-Random -Maximum 345600) }
$next = $last + $adv
$dow = [datetimeoffset]::FromUnixTimeSeconds($next).DayOfWeek
if (($dow -eq [DayOfWeek]::Saturday -or $dow -eq [DayOfWeek]::Sunday) -and (Get-Random -Maximum 100) -lt 90) {
    $next = $next + (8 - [int]$dow) * 86400
}
$now = [int64][datetimeoffset]::UtcNow.ToUnixTimeSeconds()
if ($next -gt $now) { $next = $now }
if ((Get-Random -Maximum 100) -lt 5) { $hour = 8 + (Get-Random -Maximum 4) } else { $hour = 13 + (Get-Random -Maximum 11) }
$min = Get-Random -Maximum 60; $sec = Get-Random -Maximum 60
$day = [datetimeoffset]::FromUnixTimeSeconds($next).Date
$next = [int64]([datetimeoffset]::new($day.Year, $day.Month, $day.Day, $hour, $min, $sec, [TimeSpan]::Zero).ToUnixTimeSeconds())
Set-Content .git/simulated-date ([string]$next)
$sdate = [datetimeoffset]::FromUnixTimeSeconds($next).ToString("yyyy-MM-ddTHH:mm:ss")
$env:GIT_AUTHOR_DATE = $sdate
$env:GIT_COMMITTER_DATE = $sdate
git add -A
git commit -m $Message
Write-Host "committed at $sdate : $Message"
