param([Parameter(Mandatory=$true)][string]$Message)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
# 全程使用本地时间计算，避免 UTC/本地混淆导致的 8 小时时区错位（Issue 3）
$last = [int64](Get-Content .git/simulated-date)
$lastOffset = [datetimeoffset]::FromUnixTimeSeconds($last).ToLocalTime()

# 随机推进 (80%/15%/5% 三档)
$r = Get-Random -Maximum 100
if ($r -lt 80) { $adv = 3600 + (Get-Random -Maximum 82800) }
elseif ($r -lt 95) { $adv = 86400 + (Get-Random -Maximum 172800) }
else { $adv = 259200 + (Get-Random -Maximum 345600) }
$next = $lastOffset.AddSeconds($adv)

# 周末修正：90% 概率跳到周一（Issue 1：Sunday=0 → +1 天，Saturday=6 → +2 天）
$dow = $next.DayOfWeek
if (($dow -eq [DayOfWeek]::Saturday -or $dow -eq [DayOfWeek]::Sunday) -and (Get-Random -Maximum 100) -lt 90) {
    $next = $next.AddDays((8 - [int]$dow) % 7)
}

# 不超过当前实际时间（本地时间）
$now = [datetimeoffset]::Now
if ($next -gt $now) { $next = $now }

# 时间钟点分布：5% 在 12:00 前，95% 在 13:00-23:00
if ((Get-Random -Maximum 100) -lt 5) { $hour = 8 + (Get-Random -Maximum 4) } else { $hour = 13 + (Get-Random -Maximum 11) }
$min = Get-Random -Maximum 60; $sec = Get-Random -Maximum 60
$day = $next.Date
$candidate = [datetimeoffset]::new($day.Year, $day.Month, $day.Day, $hour, $min, $sec, $next.Offset)

# 钟点重算可能早于推进后的时间（跨天/跨周末后），钳制不回跳（Issue 2）
if ($candidate -lt $next) { $candidate = $next }
# 钟点重算也可能越过当前实际时间，再次钳制
if ($candidate -gt $now) { $candidate = $now }

$sdate = $candidate.ToString("yyyy-MM-ddTHH:mm:sszzz")
$env:GIT_AUTHOR_DATE = $sdate
$env:GIT_COMMITTER_DATE = $sdate
git add -A
if ($LASTEXITCODE -ne 0) { throw "git add 失败 (exit $LASTEXITCODE)，状态文件未推进" }
git commit -m $Message
if ($LASTEXITCODE -ne 0) { throw "git commit 失败 (exit $LASTEXITCODE)，状态文件未推进，时间线无空洞" }
# 提交成功后才写回状态文件（失败路径不推进模拟日期）
Set-Content .git/simulated-date ([string]$candidate.ToUnixTimeSeconds())
Write-Host "committed at $sdate : $Message"
