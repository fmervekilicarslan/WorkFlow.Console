[CmdletBinding()]
param([Parameter(Mandatory)][string]$BaseUrl, [Parameter(Mandatory)][string]$Search, [Parameter(Mandatory)][Guid]$TaskId, [switch]$FocusedLongTitle)
$ErrorActionPreference = 'Stop'
if ($BaseUrl -notmatch '^http://127\.0\.0\.1:\d+$' -or $Search -notmatch '^CALENDAR-[a-f0-9]{32}$') { throw 'An isolated local test server and calendar fixture marker are required.' }
$probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$probe.Start(); $debugPort = $probe.LocalEndpoint.Port; $probe.Stop()
$profile = Join-Path ([IO.Path]::GetTempPath()) ('workflow-calendar-' + [Guid]::NewGuid().ToString('N'))
$chrome = Start-Process 'C:\Program Files\Google\Chrome\Application\chrome.exe' -WindowStyle Hidden -PassThru -ArgumentList @('--headless=new', '--remote-debugging-address=127.0.0.1', "--remote-debugging-port=$debugPort", "--user-data-dir=$profile", '--no-first-run', '--no-default-browser-check', 'about:blank')
$socket = [Net.WebSockets.ClientWebSocket]::new()
$script:messageId = 0
$script:passed = 0
function Cdp($method, $parameters) {
    $timeout = [Threading.CancellationTokenSource]::new(15000)
    $script:messageId++; $id = $script:messageId
    $bytes = [Text.Encoding]::UTF8.GetBytes((@{id=$id;method=$method;params=$parameters} | ConvertTo-Json -Depth 30 -Compress))
    $socket.SendAsync([ArraySegment[byte]]::new($bytes),[Net.WebSockets.WebSocketMessageType]::Text,$true,$timeout.Token).GetAwaiter().GetResult() | Out-Null
    do {
        $stream = [IO.MemoryStream]::new()
        do { $buffer=New-Object byte[] 65536; $received=$socket.ReceiveAsync([ArraySegment[byte]]::new($buffer),$timeout.Token).GetAwaiter().GetResult();$stream.Write($buffer,0,$received.Count) } while(-not $received.EndOfMessage)
        $reply = [Text.Encoding]::UTF8.GetString($stream.ToArray()) | ConvertFrom-Json
        $stream.Dispose()
    } while($reply.id -ne $id)
    $timeout.Dispose()
    if ($reply.error) { throw ('CDP failed: ' + $method) }
    return $reply.result
}
function Eval($expression) {
    $r = Cdp 'Runtime.evaluate' @{expression=$expression;returnByValue=$true;awaitPromise=$true}
    if ($r.exceptionDetails) { throw ('Browser evaluation failed: ' + $r.exceptionDetails.text) }
    return $r.result.value
}
function WaitPage {
    Start-Sleep -Milliseconds 180
    Eval 'new Promise(resolve=>{function wait(){if(document.readyState==="complete"&&document.querySelector("main"))document.fonts.ready.then(()=>resolve(true));else setTimeout(wait,30)}wait()})' | Out-Null
}
function OpenPage($path) { Cdp 'Page.navigate' @{url=($BaseUrl + $path)} | Out-Null; WaitPage }
function Check($ok, $label) {
    if (-not $ok) { throw ('FAIL: ' + $label) }
    $script:passed++; Write-Output ('PASS: Calendar browser ' + $label)
}
try {
    for($i=0;$i -lt 30;$i++) {
        try { $targets=Invoke-RestMethod "http://127.0.0.1:$debugPort/json/list" -TimeoutSec 1; break } catch { Start-Sleep -Milliseconds 100 }
    }
    $page=$targets | Where-Object type -eq 'page' | Select-Object -First 1
    $socket.ConnectAsync([Uri]$page.webSocketDebuggerUrl,[Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null
    Cdp 'Page.enable' @{} | Out-Null
    $query = '&date=2028-02-29&Filter.Search=' + $Search
    if ($FocusedLongTitle) {
        Cdp 'Emulation.setDeviceMetricsOverride' @{width=320;height=900;deviceScaleFactor=1;mobile=$false} | Out-Null
        OpenPage ('/Calendar?view=month' + $query)
        Eval 'document.querySelector("[data-calendar-day=\"2028-02-29\"] details summary").click()' | Out-Null
        Write-Output ('DIAGNOSTIC: ' + (Eval 'JSON.stringify((()=>{const t=[...document.querySelectorAll(".pen-calendar-token-text")].find(e=>e.textContent.includes("busy-long-"));return {found:!!t,whiteSpace:t&&getComputedStyle(t).whiteSpace,ellipsis:t&&getComputedStyle(t).textOverflow,scrollWidth:t&&t.scrollWidth,clientWidth:t&&t.clientWidth,actualCodes:t&&[...t.closest("a").getAttribute("aria-label").slice(-90)].map(c=>c.codePointAt(0)),expectedCodes:[..."\u011f"].map(c=>c.codePointAt(0)),unicodeExpectation:t&&t.closest("a").getAttribute("aria-label").includes("\u011f".repeat(20))}})())'))
        Check (Eval '(()=>{const t=[...document.querySelectorAll(".pen-calendar-token-text")].find(e=>e.textContent.includes("busy-long-"));return !!t&&getComputedStyle(t).whiteSpace==="nowrap"&&getComputedStyle(t).textOverflow==="ellipsis"&&t.scrollWidth>t.clientWidth&&t.closest("a").getAttribute("aria-label").includes("\u011f".repeat(20))})()') 'long title truncates visually and retains full accessible name'
        return
    }
    foreach($view in @('month','week','day')) {
        OpenPage ('/Calendar?view=' + $view + $query)
        $expected = @{month=35;week=7;day=1}[$view]
        Check ((Eval 'document.querySelectorAll("[data-calendar-day]").length') -eq $expected) "$view renders expected date-cell count"
        foreach($width in @(1440,768,390,320)) {
            Cdp 'Emulation.setDeviceMetricsOverride' @{width=$width;height=900;deviceScaleFactor=1;mobile=$false} | Out-Null
            foreach($theme in @('light','dark')) {
                Eval ('document.querySelector("[data-theme-choice=' + $theme + ']").click()') | Out-Null
                Check (Eval 'document.documentElement.scrollWidth<=innerWidth&&[...document.querySelectorAll(".pen-calendar-task,.pen-calendar-token")].filter(e=>e.getBoundingClientRect().width>0).every(e=>{const r=e.getBoundingClientRect();return e.scrollWidth<=e.clientWidth+1&&parseFloat(getComputedStyle(e.matches("a")?e:e.querySelector("a")).fontSize)>=11&&r.right<=innerWidth&&r.left>=0})') "$view fits readable cards at $width px $theme"
                Check (Eval ('document.documentElement.dataset.theme==="' + $theme + '"')) "$view applies $theme theme at $width px"
                if ($view -eq 'month') {
                    Check (Eval '(()=>{const d=document.querySelector("[data-calendar-day=\"2028-02-29\"]");return d.querySelectorAll(":scope > .pen-calendar-token").length===3&&[...d.querySelectorAll(":scope > .pen-calendar-token")].every(e=>e.getBoundingClientRect().height<=30)&&d.getBoundingClientRect().height<=240})()') "month limits busy day to three compact labels at $width px $theme"
                }
            }
        }
    }
    OpenPage ('/Calendar?view=month' + $query)
    Check (Eval '(()=>{const d=document.querySelector("[data-calendar-day=\"2028-02-29\"] details");return !!d&&!d.open&&d.querySelectorAll(".pen-calendar-token").length===7&&d.querySelector("summary").textContent.startsWith("+7")})()') 'busy day starts with seven additional tasks collapsed'
    Eval 'document.querySelector("[data-calendar-day=\"2028-02-29\"] details summary").click()' | Out-Null
    Check (Eval '(()=>{const d=document.querySelector("[data-calendar-day=\"2028-02-29\"]");return d.querySelector("details").open&&[...d.querySelectorAll(".pen-calendar-token")].every(e=>e.getBoundingClientRect().width>0)})()') 'native disclosure exposes every busy-day task'
    Check (Eval '(()=>{const l=document.querySelector(".pen-calendar-more-list");return getComputedStyle(l).overflowY==="auto"&&l.clientHeight<=144&&l.scrollHeight>l.clientHeight})()') 'expanded overflow uses bounded scroll area'
    Check (Eval '(()=>{const t=[...document.querySelectorAll(".pen-calendar-token-text")].find(e=>e.textContent.includes("busy-long-"));return !!t&&getComputedStyle(t).whiteSpace==="nowrap"&&getComputedStyle(t).textOverflow==="ellipsis"&&t.scrollWidth>t.clientWidth&&t.closest("a").getAttribute("aria-label").includes("\u011f".repeat(20))})()') 'long title truncates visually and retains full accessible name'
    Check (Eval '[...document.querySelectorAll(".pen-calendar-token")].every(e=>e.querySelector(".pen-calendar-token-symbol").textContent.trim()&&e.getAttribute("aria-label").includes("\u00f6ncelik"))') 'status and priority include symbols and accessible text'
    Check (Eval '[...document.querySelectorAll("[data-calendar-day]")].filter(e=>!e.querySelector(".pen-calendar-token")).every(e=>!e.querySelector(".pen-calendar-no-tasks")&&!e.textContent.includes("G\u00f6rev yok"))') 'empty month days omit repeated no-task text'
    Eval 'document.querySelector("[data-calendar-day=\"2028-02-29\"] details summary").click();document.querySelector("[data-calendar-day=\"2028-02-29\"] > .pen-calendar-token").focus()' | Out-Null
    Cdp 'Input.dispatchKeyEvent' @{type='keyDown';key='Tab';code='Tab';windowsVirtualKeyCode=9} | Out-Null
    Cdp 'Input.dispatchKeyEvent' @{type='keyUp';key='Tab';code='Tab';windowsVirtualKeyCode=9} | Out-Null
    Check (Eval '(()=>{const a=document.activeElement;const full=a.querySelector(".pen-calendar-token-full");return a.matches(".pen-calendar-token")&&a.matches(":focus-visible")&&!!full&&getComputedStyle(full).display!=="none"&&full.textContent===a.getAttribute("aria-label")})()') 'keyboard Tab reveals complete task explanation'
    Eval ('document.querySelector(`.pen-calendar-token[href="/Tasks/Details/' + $TaskId + '"]`).click()') | Out-Null
    WaitPage
    Check (Eval ('location.pathname.endsWith("/Tasks/Details/' + $TaskId + '")')) 'month label opens existing details route'
    OpenPage ('/Calendar?view=month' + $query)
    Check (Eval '(()=>{const i=document.querySelector("#calendar-date");return i.type==="text"&&i.value==="29.02.2028"&&i.labels.length===1&&!!document.getElementById(i.getAttribute("aria-describedby"))})()') 'Turkish date field has stable formatted value and accessible guidance'
    Eval 'document.querySelector("#calendar-date").value="01.03.2028";document.querySelector(".pen-calendar-filters").requestSubmit()' | Out-Null
    WaitPage
    Check (Eval 'document.querySelector("#calendar-date").value==="01.03.2028"&&document.querySelector("[data-calendar-view]").dataset.calendarView==="month"') 'Turkish date form navigates without native browser date formatting'
    OpenPage ('/Calendar?view=month' + $query + '&Filter.Status=1&Filter.Priority=3')
    Eval 'document.querySelector("[data-calendar-day=\"2028-02-29\"] h3 a").click()' | Out-Null
    WaitPage
    Check (Eval ('(()=>{const q=new URL(location.href).searchParams;return q.get("view")==="day"&&q.get("date")==="2028-02-29"&&q.get("Filter.Search")==="' + $Search + '"&&["1","InProgress"].includes(q.get("Filter.Status"))&&["3","Critical"].includes(q.get("Filter.Priority"))&&document.querySelectorAll("[data-calendar-day]").length===1})()')) 'day heading opens selected day with all filters retained'
    OpenPage ('/Calendar?view=month' + $query)
    Eval 'document.querySelector(".pen-calendar-undated .pen-pagination [rel=next]").click()' | Out-Null
    WaitPage
    Check (Eval 'document.querySelectorAll(".pen-calendar-undated .pen-calendar-task").length===2&&document.querySelectorAll("[data-calendar-day]").length===35&&new URL(location.href).searchParams.get("Filter.UndatedPage")==="2"') 'undated next page leaves month cells intact'
    OpenPage ('/Calendar?view=month' + $query + '&Filter.Status=1&Filter.Priority=3')
    Eval 'document.querySelector("[data-calendar-choice=week]").click()' | Out-Null
    WaitPage
    Check (Eval ('(()=>{const q=new URL(location.href).searchParams;return document.querySelector("[data-calendar-view]").dataset.calendarView==="week"&&q.get("date")==="2028-02-29"&&q.get("Filter.Search")==="' + $Search + '"&&["1","InProgress"].includes(q.get("Filter.Status"))&&["3","Critical"].includes(q.get("Filter.Priority"))})()')) 'view links retain date and all task filters'
    OpenPage ('/Calendar?view=day' + $query)
    Check (Eval 'localStorage.getItem("workflow-calendar-view")==="day"&&document.querySelector("[data-calendar-choice=day]").getAttribute("aria-current")==="page"') 'explicit day view persists accessible preference'
    OpenPage '/Tasks?view=list'
    OpenPage ('/Calendar?date=2028-02-29&Filter.Search=' + $Search)
    Check (Eval 'document.querySelector("[data-calendar-view]").dataset.calendarView==="day"&&document.documentElement.dataset.theme==="dark"') 'calendar view and theme survive navigation without view parameter'
    OpenPage ('/Calendar?view=month' + $query)
    Check (Eval 'document.querySelector("[data-calendar-view]").dataset.calendarView==="month"&&localStorage.getItem("workflow-calendar-view")==="month"') 'explicit month overrides saved day view'
    OpenPage ('/Calendar?view=day' + $query)
    Eval ('document.querySelector(`.pen-calendar-task a[href="/Tasks/Details/' + $TaskId + '"]`).click()') | Out-Null
    WaitPage
    Check (Eval ('location.pathname.endsWith("/Tasks/Details/' + $TaskId + '")&&!!document.querySelector("main h1")')) 'calendar task opens existing details page'
    OpenPage '/Calendar?view=day&date=2028-02-29&Filter.Search=CALENDAR-no-matching-task'
    Check (Eval 'document.querySelectorAll(".pen-calendar-task").length===0&&document.querySelectorAll(".pen-calendar-empty").length===2') 'empty dated and undated sections expose feedback'
    OpenPage '/Calendar?view=month'
    Eval 'document.querySelector(".pen-calendar-navigation a:nth-child(2)").click()' | Out-Null
    WaitPage
    Check (Eval 'document.querySelectorAll("[data-calendar-day].is-today").length===1') 'today navigation highlights current Istanbul day'
    Check (Eval '[...document.querySelectorAll(".pen-sidebar a[href=\"/Calendar\"]")].some(a=>!a.hasAttribute("aria-disabled"))') 'calendar navigation is enabled in shared layout'
    Write-Output "$script:passed calendar browser checks passed. Calendar GET navigation only."
} finally {
    $socket.Dispose()
    if (-not $chrome.HasExited) { Stop-Process -Id $chrome.Id -ErrorAction SilentlyContinue }
}
