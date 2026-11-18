[CmdletBinding()]
param([Parameter(Mandatory)][string]$BaseUrl, [Parameter(Mandatory)][string]$Search, [Parameter(Mandatory)][Guid]$TaskId, [switch]$FocusedLongTitle)
$ErrorActionPreference = 'Stop'
if ($BaseUrl -notmatch '^https://127\.0\.0\.1:\d+$' -or $Search -notmatch '^CALENDAR-[a-f0-9]{32}$' -or $env:WORKFLOW_BROWSER_CERT_SPKI -notmatch '^[A-Za-z0-9+/]{43}=$' -or [string]::IsNullOrWhiteSpace($env:WORKFLOW_BROWSER_COOKIE)) { throw 'A pinned isolated HTTPS server, authenticated cookie and calendar fixture marker are required.' }
$probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$probe.Start(); $debugPort = $probe.LocalEndpoint.Port; $probe.Stop()
$profile = Join-Path ([IO.Path]::GetTempPath()) ('workflow-calendar-' + [Guid]::NewGuid().ToString('N'))
$chrome = Start-Process 'C:\Program Files\Google\Chrome\Application\chrome.exe' -WindowStyle Hidden -PassThru -ArgumentList @('--headless=new', "--ignore-certificate-errors-spki-list=$env:WORKFLOW_BROWSER_CERT_SPKI", '--remote-debugging-address=127.0.0.1', "--remote-debugging-port=$debugPort", "--user-data-dir=$profile", '--no-first-run', '--no-default-browser-check', 'about:blank')
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
    Cdp 'Network.enable' @{} | Out-Null
    $cookieResult = Cdp 'Network.setCookie' @{name='.WorkFlow.Identity';value=$env:WORKFLOW_BROWSER_COOKIE;url=$BaseUrl;secure=$true;httpOnly=$true;sameSite='Lax'}
    if (-not $cookieResult.success) { throw 'Authenticated HTTPS browser cookie was rejected.' }
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
                Check (Eval 'document.documentElement.scrollWidth<=innerWidth&&[...document.querySelectorAll(".pen-calendar-task,.pen-calendar-token")].filter(e=>e.getBoundingClientRect().width>0).every(e=>{const r=e.getBoundingClientRect(),cell=e.closest(".is-month [data-calendar-day]"),bounds=cell?cell.getBoundingClientRect():{left:0,right:innerWidth};return e.scrollWidth<=e.clientWidth+1&&parseFloat(getComputedStyle(e.matches("a")?e:e.querySelector("a")).fontSize)>=11&&r.right<=bounds.right+1&&r.left>=bounds.left-1})') "$view fits readable cards within intended viewport or month cells at $width px $theme"
                Check (Eval ('document.documentElement.dataset.theme==="' + $theme + '"')) "$view applies $theme theme at $width px"
                if ($view -eq 'month') {
                    Check (Eval '(()=>{const d=document.querySelector("[data-calendar-day=\"2028-02-29\"]");return d.querySelectorAll(":scope > .pen-calendar-token").length===3&&[...d.querySelectorAll(":scope > .pen-calendar-token")].every(e=>e.getBoundingClientRect().height<=30)&&d.getBoundingClientRect().height<=240})()') "month limits busy day to three compact labels at $width px $theme"
                    Check (Eval '(()=>{const g=document.querySelector(".is-month"),headers=[...g.querySelectorAll(".pen-calendar-weekday")],days=[...g.querySelectorAll("[data-calendar-day]")],names=["Pzt","Sal","\u00c7ar","Per","Cum","Cmt","Paz"];return headers.length===7&&headers.every((h,i)=>h.textContent.trim()===names[i]&&g.children[i]===h)&&days.length===35&&days.every((d,i)=>{const r=d.getBoundingClientRect(),h=headers[i%7].getBoundingClientRect(),weekday=(new Date(d.dataset.calendarDay+"T12:00:00Z").getUTCDay()+6)%7;return weekday===i%7&&Math.abs(r.left-h.left)<1&&Math.abs(r.width-h.width)<1&&(i<7||r.top>days[i-7].getBoundingClientRect().top)})})()') "weekday headers align every date in seven Monday-first columns at $width px $theme"
                    Check (Eval '(()=>{const s=document.querySelector(".pen-calendar-month-scroll"),g=document.querySelector(".is-month"),r=s.getBoundingClientRect();return !!s&&s.tabIndex===0&&s.getAttribute("role")==="region"&&!!s.getAttribute("aria-label")&&getComputedStyle(s).overflowX==="auto"&&r.left>=0&&r.right<=innerWidth&&document.documentElement.scrollWidth<=innerWidth&&getComputedStyle(g).gridTemplateColumns.split(" ").length===7&&(innerWidth>760||s.scrollWidth>s.clientWidth)})()') "month preserves seven columns inside its own accessible horizontal scroller at $width px $theme"
                    Check (Eval '(()=>{const days=[...document.querySelectorAll(".is-month [data-calendar-day]")],outside=days.find(d=>d.classList.contains("is-outside")),inside=days.find(d=>!d.classList.contains("is-outside"));const style=e=>[getComputedStyle(e).backgroundColor,getComputedStyle(e).opacity,getComputedStyle(e.querySelector("h3 a")).color].join("|");return days.every(d=>{const a=d.querySelector("h3 a");return !!a&&/^(?:[1-9]|[12][0-9]|3[01])$/.test(a.textContent.trim())&&Number(a.textContent.trim())===Number(d.dataset.calendarDay.slice(-2))&&!!a.getAttribute("aria-label")&&a.getAttribute("aria-label").length>10})&&!!outside&&!!inside&&style(outside)!==style(inside)})()') "month uses numeric accessible day links and distinguishes outside dates at $width px $theme"
                    Check (Eval '(()=>{const days=[...document.querySelectorAll(".is-month [data-calendar-day]")],outside=days.filter(d=>d.classList.contains("is-outside")),empty=days.filter(d=>!d.querySelector(".pen-calendar-token")),lum=color=>{const n=color.match(/[\d.]+/g).slice(0,3).map(Number).map(x=>{x/=255;return x<=.04045?x/12.92:((x+.055)/1.055)**2.4});return .2126*n[0]+.7152*n[1]+.0722*n[2]};return outside.length>0&&outside.every(d=>{const a=d.querySelector("h3 a"),fg=lum(getComputedStyle(a).color),bg=lum(getComputedStyle(d).backgroundColor);return (Math.max(fg,bg)+.05)/(Math.min(fg,bg)+.05)>=4.5&&getComputedStyle(d).opacity==="1"&&[...d.querySelectorAll(".pen-calendar-token")].every(t=>getComputedStyle(t).opacity==="1"&&!!t.getAttribute("href"))})&&empty.length>0&&empty.every(d=>d.getBoundingClientRect().height<=180)&&days.every(d=>!d.querySelector("details[open]"))})()') "outside dates retain contrast and task opacity while empty days stay compact at $width px $theme"
                    if ($width -ne 768) {
                        Eval 'document.querySelector(".pen-calendar-filters").requestSubmit()' | Out-Null
                        WaitPage
                        Check (Eval ('(()=>{const q=new URL(location.href).searchParams,fields=[...document.querySelectorAll(".pen-calendar-filters input:not([type=hidden]),.pen-calendar-filters select")];return fields.length===4&&fields.every(e=>{const r=e.getBoundingClientRect();return r.height>=(innerWidth<=760?44:38)&&r.left>=0&&r.right<=innerWidth})&&document.documentElement.scrollWidth<=innerWidth&&q.get("view")==="month"&&q.get("date")==="29.02.2028"&&q.get("Filter.Search")==="' + $Search + '"&&document.querySelector("#calendar-date").value==="29.02.2028"})()')) "calendar toolbar submits preserved date and search without overflow at $width px $theme"
                    }
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
    Check (Eval '[...document.querySelectorAll(".pen-calendar-token")].every(e=>{const dot=e.querySelector(".pen-calendar-token-dot"),name=e.getAttribute("aria-label");return !!dot&&dot.getAttribute("aria-hidden")==="true"&&!dot.textContent.trim()&&!e.querySelector(".pen-calendar-token-symbol")&&name.includes("\u00f6ncelik")&&["Yap\u0131lacak","Devam ediyor","Tamamland\u0131"].some(s=>name.includes(s))&&["D\u00fc\u015f\u00fck","Normal","Y\u00fcksek","Kritik"].some(p=>name.includes(p))})') 'priority uses an uncluttered dot while status and priority remain available as text'
    $expectedAccessibleName = Eval 'document.querySelector(".pen-calendar-token").getAttribute("aria-label")'
    $rootDocument = Cdp 'DOM.getDocument' @{}
    $taskLink = Cdp 'DOM.querySelector' @{nodeId=$rootDocument.root.nodeId;selector='.pen-calendar-token'}
    $accessibleTree = Cdp 'Accessibility.getPartialAXTree' @{nodeId=$taskLink.nodeId;fetchRelatives=$false}
    Check (@($accessibleTree.nodes | Where-Object { -not $_.ignored -and $_.role.value -eq 'link' -and $_.name.value -eq $expectedAccessibleName }).Count -eq 1) 'actual accessibility tree exposes full task title, status and priority as link name'
    Check (Eval '(()=>{const colors=new Set([...document.querySelectorAll(".pen-calendar-token-dot")].map(d=>getComputedStyle(d).backgroundColor));return colors.size===4&&[...document.querySelectorAll(".pen-calendar-token-dot")].every(d=>{const r=d.getBoundingClientRect();return r.width>=6&&r.width<=12&&Math.abs(r.width-r.height)<1&&parseFloat(getComputedStyle(d).borderRadius)>=r.width/2})})()') 'all four priorities retain distinct compact circular dot colors'
    Check (Eval '[...document.querySelectorAll("[data-calendar-day]")].filter(e=>!e.querySelector(".pen-calendar-token")).every(e=>!e.querySelector(".pen-calendar-no-tasks")&&!e.textContent.includes("G\u00f6rev yok"))') 'empty month days omit repeated no-task text'
    OpenPage ('/Calendar?view=month' + $query)
    $outsideDetails = Eval 'document.querySelector("[data-calendar-day=\"2028-03-01\"].is-outside .pen-calendar-token").getAttribute("href")'
    Eval 'document.querySelector("[data-calendar-day=\"2028-03-01\"].is-outside h3 a").click()' | Out-Null
    WaitPage
    Check (Eval ('(()=>{const q=new URL(location.href).searchParams;return q.get("view")==="day"&&q.get("date")==="2028-03-01"&&q.get("Filter.Search")==="' + $Search + '"&&document.querySelectorAll("[data-calendar-day]").length===1&&!!document.querySelector(".pen-calendar-task a[href=\"' + $outsideDetails + '\"]")})()')) 'outside-month day opens its correct date and keeps its task reachable'
    OpenPage ('/Calendar?view=month' + $query)
    Eval 'document.querySelector("[data-calendar-day=\"2028-03-01\"].is-outside .pen-calendar-token").click()' | Out-Null
    WaitPage
    Check (Eval ('location.pathname==="' + $outsideDetails + '"&&!!document.querySelector("main h1")')) 'outside-month task opens the existing details page'
    OpenPage ('/Calendar?view=month' + $query)
    Eval 'document.querySelector("[data-calendar-day=\"2028-02-29\"] details summary").click()' | Out-Null
    Eval 'document.querySelector("[data-calendar-day=\"2028-02-29\"] details summary").click();document.querySelector("[data-calendar-day=\"2028-02-29\"] > .pen-calendar-token").focus()' | Out-Null
    Cdp 'Input.dispatchKeyEvent' @{type='keyDown';key='Tab';code='Tab';windowsVirtualKeyCode=9} | Out-Null
    Cdp 'Input.dispatchKeyEvent' @{type='keyUp';key='Tab';code='Tab';windowsVirtualKeyCode=9} | Out-Null
    Check (Eval '(()=>{const a=document.activeElement;const full=a.querySelector(".pen-calendar-token-full");return a.matches(".pen-calendar-token")&&a.matches(":focus-visible")&&!!full&&getComputedStyle(full).display!=="none"&&full.textContent===a.getAttribute("aria-label")})()') 'keyboard Tab reveals complete task explanation'
    Eval ('document.querySelector(`.pen-calendar-token[href="/Tasks/Details/' + $TaskId + '"]`).click()') | Out-Null
    WaitPage
    Check (Eval ('location.pathname.endsWith("/Tasks/Details/' + $TaskId + '")')) 'month label opens existing details route'
    OpenPage ('/Calendar?view=month' + $query)
    Check (Eval '(()=>{const i=document.querySelector("#calendar-date");return i.type==="text"&&i.value==="29.02.2028"&&i.labels.length===1&&i.getAttribute("aria-label")==="Tarih, GG.AA.YYYY bi\u00e7iminde"&&i.placeholder==="GG.AA.YYYY"})()') 'Turkish date field has stable formatted value and accessible format name'
    Check (Eval '(()=>{const i=document.querySelector("#calendar-date"),heading=document.querySelector("[data-calendar-view=month] > .pen-calendar-heading");return !!heading.querySelector("h2")&&!heading.querySelector("p")&&!document.querySelector("#calendar-date-format")&&!i.hasAttribute("aria-describedby")&&!i.parentElement.querySelector(".pen-calendar-current-date")})()') 'month heading and date field omit removed explanatory text'
    Eval 'const dateInput=document.querySelector("#calendar-date");dateInput.value="";dateInput.focus()' | Out-Null
    Check (Eval 'document.querySelector("#calendar-date").matches(":placeholder-shown")&&document.querySelector("#calendar-date").placeholder==="GG.AA.YYYY"') 'empty date field displays only the format placeholder'
    Cdp 'Input.insertText' @{text='01.03.2028'} | Out-Null
    Check (Eval 'document.querySelector("#calendar-date").value==="01.03.2028"&&!document.querySelector("#calendar-date").matches(":placeholder-shown")') 'typing a date hides the format placeholder'
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
    foreach($theme in @('light','dark')) {
        Eval ('document.querySelector("[data-theme-choice=' + $theme + ']").click()') | Out-Null
        Check (Eval '(()=>{const a=document.querySelector(".is-month .is-today h3 a[aria-current=date]"),s=a&&getComputedStyle(a),r=a&&a.getBoundingClientRect();if(!a)return false;const lum=color=>{const n=color.match(/[\d.]+/g).slice(0,3).map(Number).map(x=>{x/=255;return x<=.04045?x/12.92:((x+.055)/1.055)**2.4});return .2126*n[0]+.7152*n[1]+.0722*n[2]},fg=lum(s.color),bg=lum(s.backgroundColor),contrast=(Math.max(fg,bg)+.05)/(Math.min(fg,bg)+.05);return r.width>=24&&Math.abs(r.width-r.height)<1&&parseFloat(s.borderRadius)>=r.width/2&&contrast>=4.5&&/^\d{1,2}$/.test(a.textContent.trim())&&!!a.getAttribute("aria-label")})()') "today circle preserves readable contrast and full date semantics in $theme theme"
    }
    Eval 'window.qaMonthScroll=document.querySelector(".pen-calendar-month-scroll");qaMonthScroll.scrollIntoView({block:"center"});qaMonthScroll.scrollLeft=0;qaMonthScroll.focus();window.qaCalendarPageY=scrollY' | Out-Null
    Cdp 'Input.dispatchKeyEvent' @{type='keyDown';key='ArrowRight';code='ArrowRight';windowsVirtualKeyCode=39} | Out-Null
    Cdp 'Input.dispatchKeyEvent' @{type='keyUp';key='ArrowRight';code='ArrowRight';windowsVirtualKeyCode=39} | Out-Null
    Eval 'new Promise(resolve=>setTimeout(()=>resolve(true),250))' | Out-Null
    Check (Eval 'qaMonthScroll.scrollLeft>0&&scrollY===qaCalendarPageY&&document.activeElement===qaMonthScroll&&qaMonthScroll.matches(":focus-visible")&&getComputedStyle(qaMonthScroll).outlineStyle!=="none"') 'keyboard scrolls the month region without moving the page and retains visible focus'
    Eval 'document.querySelector(".is-month .is-today h3 a[aria-current=date]").focus()' | Out-Null
    Check (Eval 'document.activeElement.matches(".is-month .is-today h3 a[aria-current=date]:focus-visible")&&getComputedStyle(document.activeElement).outlineStyle!=="none"') 'today day link retains visible keyboard focus inside the month scroller'
    Check (Eval '[...document.querySelectorAll(".pen-sidebar a[href=\"/Calendar\"]")].some(a=>!a.hasAttribute("aria-disabled"))') 'calendar navigation is enabled in shared layout'
    Write-Output "$script:passed calendar browser checks passed. Calendar GET navigation only."
} finally {
    $socket.Dispose()
    if (-not $chrome.HasExited) { Stop-Process -Id $chrome.Id -ErrorAction SilentlyContinue }
}
