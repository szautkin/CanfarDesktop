<#
.SYNOPSIS
    Verbinal's MCP smoke test: the QA report's checks, run against the running app through its bridge.

.DESCRIPTION
    Starts the MCP bridge the way an assistant does, speaks MCP to it over stdio, and checks what 1.4.1
    fixed: the bridge answering while Verbinal is closed (D10), tool descriptions that say when a change
    applies (D9), queries from the ADQL editor (D1, D1b, D11), export headers (D4), cutout diagnostics
    (D8) — saving DataLink's raw answer for the observation given — the compute state (D6), the launch
    form, and with -FitsPath the FITS viewer (D3, O1).

    It changes a few things, all of which a person could undo by hand: it puts two queries in the ADQL
    editor and runs them (so they appear in Recent searches), writes a CSV and the DataLink answer to
    -OutDir, opens and closes the launch form, and with -FitsPath opens that file and closes its tab.
    It launches nothing, deletes nothing and approves nothing.

    Exit code 0 when every check passed, 1 otherwise.

.PARAMETER Bridge
    The bridge exe. By default, where Verbinal keeps it for this Windows user.

.PARAMETER PublisherId
    The observation whose cutout options and DataLink answer are checked and saved.

.PARAMETER FitsPath
    A local FITS file with sky coordinates, for the viewer checks. Skipped when not given.

.PARAMETER OutDir
    Where the export and the DataLink answer are written. The current folder by default.

.EXAMPLE
    .\scripts\mcp-smoke.ps1 -FitsPath C:\Data\ib7711ndq_flt.fits
#>
param(
    [string]$Bridge,
    [string]$PublisherId = 'ivo://cadc.nrc.ca/CFHTMEGAPIPE?MegaPipe.712.165/MegaPipe.712.165.G.MP9401',
    [string]$FitsPath,
    [string]$OutDir = (Get-Location).Path,
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'

if (-not $Bridge) {
    $family = (Get-AppxPackage -Name CodeBG.Verbinal).PackageFamilyName
    if (-not $family) { throw 'Verbinal is not installed. Pass -Bridge with the bridge exe (Settings > AI agent shows it).' }
    $Bridge = Join-Path $env:LOCALAPPDATA "Packages\$family\LocalCache\Verbinal\mcp-bridge\CanfarDesktop.McpBridge.exe"
}
if (-not (Test-Path $Bridge)) { throw "No bridge at $Bridge. Turn on Settings > AI agent > Enable MCP server first." }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# ── A minimal MCP client: one JSON document per line over the bridge's stdio ────────────────────────

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $Bridge
$psi.Arguments = 'mcp'
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
$proc = [System.Diagnostics.Process]::Start($psi)

$writer = New-Object System.IO.StreamWriter($proc.StandardInput.BaseStream, (New-Object System.Text.UTF8Encoding($false)))
$writer.AutoFlush = $true
$writer.NewLine = "`n"

$script:nextId = 0
$script:pending = $null
$script:notifications = New-Object System.Collections.Generic.List[string]

# One read outstanding at a time: a StreamReader refuses a second while the first is unfinished.
function Read-Line([int]$milliseconds) {
    if ($null -eq $script:pending) { $script:pending = $proc.StandardOutput.ReadLineAsync() }
    if (-not $script:pending.Wait([Math]::Max(1, $milliseconds))) { return $null }
    $line = $script:pending.Result
    $script:pending = $null
    if ($null -eq $line) { throw 'the bridge closed its output' }
    return $line
}

function Send-Request([string]$method, $params) {
    $script:nextId++
    $id = $script:nextId
    $doc = [ordered]@{ jsonrpc = '2.0'; id = $id; method = $method }
    if ($null -ne $params) { $doc.params = $params }
    $writer.WriteLine(($doc | ConvertTo-Json -Compress -Depth 20))

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $line = Read-Line ([int]($deadline - (Get-Date)).TotalMilliseconds)
        if ($null -eq $line) { break }
        if ($line.Trim().Length -eq 0) { continue }
        $answer = $line | ConvertFrom-Json
        if ($null -eq $answer.id) { $script:notifications.Add($answer.method); continue }
        if ($answer.id -eq $id) { return $answer }
    }
    throw "no answer to $method within $TimeoutSeconds s"
}

function Send-Notification([string]$method) {
    $writer.WriteLine((@{ jsonrpc = '2.0'; method = $method } | ConvertTo-Json -Compress))
}

# A tool call: its data parsed when it answered with data, its text when it refused.
function Invoke-Tool([string]$name, $arguments = @{}) {
    $answer = Send-Request 'tools/call' @{ name = $name; arguments = $arguments }
    if ($answer.error) { throw "${name}: protocol error $($answer.error.code): $($answer.error.message)" }
    $text = $answer.result.content[0].text
    $data = $null
    if (-not $answer.result.isError) { try { $data = $text | ConvertFrom-Json } catch { } }
    return [pscustomobject]@{ IsError = [bool]$answer.result.isError; Text = $text; Data = $data }
}

$script:results = New-Object System.Collections.Generic.List[object]

function Check([string]$id, [string]$what, [scriptblock]$test) {
    try {
        $detail = & $test
        $script:results.Add([pscustomobject]@{ Id = $id; Check = $what; Result = 'PASS'; Detail = "$detail" })
    }
    catch {
        $script:results.Add([pscustomobject]@{ Id = $id; Check = $what; Result = 'FAIL'; Detail = $_.Exception.Message })
    }
}

function Expect([bool]$condition, [string]$why) { if (-not $condition) { throw $why } }

function Ok($call) {
    if ($call.IsError) { throw $call.Text }
    return $call.Data
}

# ── The bridge ───────────────────────────────────────────────────────────────────────────────────────

$init = Send-Request 'initialize' @{
    protocolVersion = '2025-06-18'
    capabilities    = @{}
    clientInfo      = @{ name = 'verbinal-smoke'; version = '1.4.1' }
}
Send-Notification 'notifications/initialized'
$appAway = "$($init.result.instructions)" -match 'not running'

Check 'B1' 'The bridge answers initialize, with or without Verbinal (D10)' {
    Expect ($null -eq $init.error) "initialize was refused: $($init.error.message)"
    Expect ($init.result.serverInfo.name -eq 'verbinal-canfar') "server name is $($init.result.serverInfo.name)"
    if ($appAway) { 'Verbinal is not running: the bridge answered for it' } else { "Verbinal $($init.result.serverInfo.version)" }
}

$tools = (Send-Request 'tools/list' @{}).result.tools
Check 'B2' 'tools/list lists the tools' {
    # While Verbinal is closed they are the ones it listed last time — none, before it ever has.
    if ($appAway) { return "$($tools.Count) tools, as Verbinal last listed them" }
    Expect ($tools.Count -gt 100) "only $($tools.Count) tools"
    "$($tools.Count) tools"
}

if ($appAway) {
    Check 'B3' 'A tool call while Verbinal is closed is a tool error saying so' {
        $call = Invoke-Tool 'describe_app'
        Expect $call.IsError 'it answered as if the app were there'
        Expect ($call.Text -match 'not running') $call.Text
        $call.Text
    }
    Write-Host 'Verbinal is not running: start it, turn on its MCP server, and run this again for the rest.' -ForegroundColor Yellow
}
else {
    # ── Tools and their descriptions ─────────────────────────────────────────────────────────────────

    Check 'A1' 'describe_app' { $app = Ok (Invoke-Tool 'describe_app'); "version $($app.version)" }

    Check 'A2' 'search_tools matches words, not the whole phrase' {
        $found = Ok (Invoke-Tool 'search_tools' @{ query = 'cube spectrum' })
        Expect ($found.count -gt 0) 'nothing found'
        "$($found.count) tools, e.g. $($found.tools[0].name)"
    }

    Check 'A3' 'A destructive tool says it always waits for approval (D9)' {
        $entry = Ok (Invoke-Tool 'man' @{ tool = 'remove_downloaded_file' })
        Expect ($entry.description -match 'always waits in Pending') $entry.description
        'says so'
    }

    Check 'A4' 'An ordinary write says it applies at once with auto-apply on (D9)' {
        $entry = Ok (Invoke-Tool 'man' @{ tool = 'save_query' })
        Expect ($entry.description -match 'Auto-apply agent writes') $entry.description
        Expect (-not ($entry.description -match 'Queues for the user')) 'still says it queues'
        'says so'
    }

    # ── Search ───────────────────────────────────────────────────────────────────────────────────────

    $query = "SELECT TOP 5 Observation.observationID, COORD1(CENTROID(Plane.position_bounds)) AS `"RA (J2000.0)`" " +
             "FROM caom2.Plane AS Plane JOIN caom2.Observation AS Observation ON Plane.obsID = Observation.obsID " +
             "WHERE Observation.collection = 'CFHT'"

    Check 'S1' 'A query run from the ADQL editor' {
        $ran = Ok (Invoke-Tool 'set_adql_query' @{ adql = $query; execute = $true })
        Expect $ran.executed "not run: $($ran.message)"
        Expect $ran.run.ran "failed: $($ran.run.message) $($ran.run.error)"
        "$($ran.run.totalRows) rows in $([int]$ran.run.elapsedMs) ms"
    }

    Check 'S2' 'Its results show its columns (D1)' {
        $grid = Ok (Invoke-Tool 'get_search_results' @{ limit = 5 })
        Expect ($grid.columns.Count -gt 0) 'no column is visible'
        "visible: $($grid.columns -join ', ')"
    }

    Check 'S3' 'It is kept as a recent search, marked as from the editor (D1b)' {
        $recent = Ok (Invoke-Tool 'list_recent_searches' @{ limit = 1 })
        Expect ($recent.searches.Count -eq 1 -and $recent.searches[0].fromEditor) 'the newest recent search is not it'
        $recent.searches[0].summary
    }

    Check 'S4' 'An export names columns as the grid does (D4)' {
        $csv = Join-Path $OutDir 'smoke-export.csv'
        $export = Ok (Invoke-Tool 'export_search_results' @{ format = 'csv'; path = $csv })
        Expect $export.exported "not exported: $($export.message)"
        $header = Get-Content -Path $csv -TotalCount 1
        Expect ($header -eq 'observationID,RA (J2000.0)') "header: $header"
        $header
    }

    Check 'S5' 'An empty query clears the editor (D11)' {
        $cleared = Ok (Invoke-Tool 'set_adql_query' @{ adql = '' })
        Expect ($cleared.applied -and -not $cleared.executed) ($cleared | ConvertTo-Json -Compress)
        $cleared.message
    }

    # ── Cutouts ──────────────────────────────────────────────────────────────────────────────────────

    Check 'C1' "Cutout options say how, or why not (D8): $PublisherId" {
        $options = Ok (Invoke-Tool 'get_cutout_options' @{ publisherId = $PublisherId })
        $ways = @($options.files | ForEach-Object { "$($_.fileName) by $($_.method)" })
        Expect ($ways.Count -gt 0 -or $options.sodaProblems.Count -gt 0) 'no way to cut, and no reason given'
        if ($ways.Count -gt 0) { $ways -join '; ' } else { 'none: ' + ($options.sodaProblems -join ' | ') }
    }

    Check 'C2' "DataLink's answer, saved as it came (D8)" {
        $links = Ok (Invoke-Tool 'get_data_links' @{ publisherId = $PublisherId; raw = $true })
        Expect ($null -ne $links.raw) "no answer: $($links.rawProblem)"
        $file = Join-Path $OutDir ('datalink-' + ($PublisherId -replace '[^A-Za-z0-9.+-]', '_') + '.xml')
        [System.IO.File]::WriteAllText($file, $links.raw, (New-Object System.Text.UTF8Encoding($false)))
        "HTTP $($links.rawStatus), $($links.cutoutServices) cutout service(s), saved to $file" +
            $(if ($links.problems.Count -gt 0) { '; problems: ' + ($links.problems -join ' | ') } else { '' })
    }

    # ── The Portal and compute ───────────────────────────────────────────────────────────────────────

    Check 'P1' 'Session images carry their project, and filter by it' {
        $all = Ok (Invoke-Tool 'list_session_images' @{ type = 'notebook' })
        Expect ($all.count -gt 0) 'no notebook images'
        $project = $all.images[0].project
        $mine = Ok (Invoke-Tool 'list_session_images' @{ type = 'notebook'; project = $project })
        Expect (@($mine.images | Where-Object { $_.project -ne $project }).Count -eq 0) 'another project got through'
        "$($all.count) notebook images; $($mine.count) in $project"
    }

    Check 'P2' 'The compute state gives a real image reference, and the session''s own size (D6)' {
        $state = Invoke-Tool 'get_compute_state'
        if ($state.IsError) { return "not available: $($state.Text)" }
        $s = $state.Data
        Expect (-not ("$($s.image)" -match '\s|://')) "image: $($s.image)"
        "state $($s.state); launches with $($s.cores) cores, $($s.ram) GB" +
            $(if ($s.sessionId) { "; session has $($s.sessionCores) cores, $($s.sessionRam) GB" } else { '' })
    }

    Check 'P3' 'The launch form opens in its dialog, and closes' {
        $shown = Ok (Invoke-Tool 'show_launch_form' @{ tab = 'advanced' })
        if (-not $shown.open) { return "not opened: $($shown.message)" }
        Expect ($shown.tab -eq 'advanced') "on tab $($shown.tab)"
        $closed = Ok (Invoke-Tool 'show_launch_form' @{ close = $true })
        Expect (-not $closed.open) 'still open'
        'opened on Advanced, then closed'
    }

    # ── The FITS viewer ──────────────────────────────────────────────────────────────────────────────

    if ($FitsPath) {
        Check 'F1' 'open_fits_file' {
            $opened = Ok (Invoke-Tool 'open_fits_file' @{ path = $FitsPath })
            Expect ($opened.opened -or $opened.loading) "not opened: $($opened.message)"
            $opened.localPath
        }

        Check 'F2' 'get_fits_wcs without an HDU reads one with a WCS (O1)' {
            $wcs = Ok (Invoke-Tool 'get_fits_wcs' @{ localPath = $FitsPath })
            Expect $wcs.isValid "HDU $($wcs.hdu) has no valid WCS ($($wcs.defaultedTo))"
            "HDU $($wcs.hdu): $($wcs.defaultedTo)"
        }

        Check 'F3' 'A Go To the WCS cannot place says so, not "no WCS" (D3)' {
            $goto = Ok (Invoke-Tool 'fits_goto_coordinate' @{ ra = 85; dec = -85 })
            $view = Ok (Invoke-Tool 'get_fits_view')
            Expect (-not $goto.moved) 'it moved'
            Expect (-not ($view.hasWcs -and $view.status -match 'No WCS available')) "status: $($view.status)"
            $view.status
        }

        Check 'F4' 'Its tab closes' {
            $closed = Ok (Invoke-Tool 'close_active_tab' @{ kind = 'fits' })
            ($closed | ConvertTo-Json -Compress)
        }
    }
}

# ── Done ─────────────────────────────────────────────────────────────────────────────────────────────

$writer.Close() # the client going away is what ends the bridge
if (-not $proc.WaitForExit(5000)) { $proc.Kill() }

# Rendered to a string at a set width: redirected, as under CI or a pipe, a table otherwise comes out blank.
$script:results | Format-Table -AutoSize -Wrap -Property Id, Result, Check, Detail | Out-String -Width 220 | Write-Host
$failed = @($script:results | Where-Object Result -eq 'FAIL').Count
if ($script:notifications.Count -gt 0) { Write-Host "Notifications: $($script:notifications -join ', ')" }
Write-Host ("{0} checks, {1} failed" -f $script:results.Count, $failed) -ForegroundColor $(if ($failed) { 'Red' } else { 'Green' })
exit $(if ($failed) { 1 } else { 0 })
