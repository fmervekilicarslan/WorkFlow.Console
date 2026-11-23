[CmdletBinding()]
param([Parameter(Mandatory)][string]$BaseUrl, [Parameter(Mandatory)][Guid]$TaskId)
$ErrorActionPreference = 'Stop'
if ($BaseUrl -notmatch '^https://127\.0\.0\.1:\d+$' -or $env:WORKFLOW_BROWSER_CERT_SPKI -notmatch '^[A-Za-z0-9+/]{43}=$' -or [string]::IsNullOrWhiteSpace($env:WORKFLOW_BROWSER_COOKIE)) { throw 'A pinned isolated HTTPS server and real authenticated test cookie are required.' }
$chromePath = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
if (-not (Test-Path -LiteralPath $chromePath)) { throw 'Chrome is required for optional browser checks.' }
$probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$probe.Start(); $debugPort = $probe.LocalEndpoint.Port; $probe.Stop()
$profile = Join-Path ([IO.Path]::GetTempPath()) ('workflow-kanban-' + [Guid]::NewGuid().ToString('N'))
$chrome = Start-Process $chromePath -WindowStyle Hidden -PassThru -ArgumentList @('--headless=new', "--ignore-certificate-errors-spki-list=$env:WORKFLOW_BROWSER_CERT_SPKI", '--remote-debugging-address=127.0.0.1', "--remote-debugging-port=$debugPort", "--user-data-dir=$profile", '--no-first-run', '--no-default-browser-check', 'about:blank')
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
    if ($r.exceptionDetails) { throw ('Browser evaluation failed: ' + $r.exceptionDetails.text + ' ' + $r.exceptionDetails.exception.description) }
    return $r.result.value
}
function OpenPage($path) {
    Cdp 'Page.navigate' @{url=($BaseUrl + $path)} | Out-Null
    Start-Sleep -Milliseconds 150
    Eval 'new Promise(resolve=>{function wait(){if(document.readyState==="complete"&&document.querySelector("main"))document.fonts.ready.then(()=>resolve(true));else setTimeout(wait,30)}wait()})' | Out-Null
    Eval ('window.qaTaskId="' + $TaskId + '";window.qaCard=()=>document.querySelector(`[data-board-card][data-task-id="${window.qaTaskId}"]`);') | Out-Null
}
function ClickView($view) {
    Eval ('document.querySelector("[data-task-view=' + $view + ']").click()') | Out-Null
    Start-Sleep -Milliseconds 250
    Eval 'new Promise(resolve=>{function wait(){if(document.readyState==="complete"&&document.querySelector("main"))resolve(true);else setTimeout(wait,30)}wait()})' | Out-Null
    Eval ('window.qaTaskId="' + $TaskId + '";window.qaCard=()=>document.querySelector(`[data-board-card][data-task-id="${window.qaTaskId}"]`);') | Out-Null
}
function Check($ok, $label) {
    if (-not $ok) { throw ('FAIL: ' + $label) }
    $script:passed++; Write-Output ('PASS: Browser ' + $label)
}
function WaitUpdate {
    Eval 'new Promise(resolve=>{function wait(){if(!document.querySelector("[data-board-card][aria-busy=true]"))resolve(true);else setTimeout(wait,30)}wait()})' | Out-Null
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
    OpenPage '/Tasks?view=list&Filter.PageSize=50'
    $listIds = Eval 'JSON.stringify([...document.querySelectorAll("[data-list-task]")].map(e=>e.dataset.listTask).sort())'
    ClickView 'board'
    $boardIds = Eval 'JSON.stringify([...document.querySelectorAll("[data-board-card]")].map(e=>e.dataset.taskId).sort())'
    Check ($listIds -eq $boardIds -and $boardIds.Length -gt 10) 'separate list and board pages render the same complete task IDs'
    Check (Eval '!!document.querySelector("[data-board-view]")&&document.querySelector("[data-task-view=board]").getAttribute("aria-current")==="page"&&new URL(location.href).searchParams.get("view")==="board"') 'board link changes URL view and accessible state'
    Check (Eval 'document.querySelectorAll("[data-board-card]").length>10&&!document.querySelector("[data-list-view]")') 'board renders all matched tasks and excludes inactive list DOM'
    Check (Eval '(()=>{const columns=[...document.querySelectorAll("[data-board-column]")];return columns.length===3&&columns.every(c=>{const a=[...c.querySelectorAll("a[href]")].find(a=>new URL(a.href).pathname==="/Tasks/Create");if(!a||a.tabIndex<0||!a.getAttribute("aria-label"))return false;const u=new URL(a.href);return u.searchParams.get("view")==="board"&&["Todo","InProgress","Completed"][Number(c.dataset.boardColumn)]===u.searchParams.get("status")})})()') 'each board column provides an accessible create link with its initial status'
    Check (Eval '!document.querySelector(".pen-pagination,.pen-status-tabs,select[name=\"Filter.PageSize\"],select[name=\"Filter.Sort\"]")') 'board omits list-only controls'
    foreach($width in @(1440,390,320)) {
        Cdp 'Emulation.setDeviceMetricsOverride' @{width=$width;height=900;deviceScaleFactor=1;mobile=$false} | Out-Null
        foreach($theme in @('light','dark')) {
            Eval ('document.querySelector(`[data-theme-choice="' + $theme + '"]`).click()') | Out-Null
            Check (Eval 'document.documentElement.scrollWidth<=innerWidth&&[...document.querySelectorAll("[data-board-card]")].every(e=>{const r=e.getBoundingClientRect();return r.width>=200&&r.right<=innerWidth&&r.left>=0})') "readable cards and no horizontal overflow: $width px $theme"
            Check (Eval ('document.documentElement.dataset.theme==="' + $theme + '"')) "theme applies: $width px $theme"
        }
    }
    foreach($width in @(1440,768,390,320)) {
        Cdp 'Emulation.setDeviceMetricsOverride' @{width=$width;height=900;deviceScaleFactor=1;mobile=$false} | Out-Null
        foreach($theme in @('light','dark')) {
            Eval ('document.querySelector(`[data-theme-choice="' + $theme + '"]`).click()') | Out-Null
            Check (Eval 'document.documentElement.scrollWidth<=innerWidth&&[...document.querySelectorAll("[data-board-card]")].every(c=>c.scrollWidth<=c.clientWidth+1&&[...c.querySelectorAll("a,select")].every(e=>{const r=e.getBoundingClientRect(),p=c.getBoundingClientRect();return r.left>=p.left&&r.right<=p.right+1}))') "refined cards contain titles and controls at $width px $theme"
            Check (Eval '[...document.querySelectorAll("[data-board-status]")].every(s=>s.tagName==="SELECT"&&s.getBoundingClientRect().height>=44&&s.labels.length===1&&!!s.getAttribute("aria-label"))') "native status selects retain accessible touch targets at $width px $theme"
            Check (Eval '[...document.querySelectorAll("[data-board-card]")].every(c=>{const f=c.querySelector(".pen-board-card-footer"),s=c.querySelector("[data-board-status]"),a=c.querySelector(".pen-board-title"),due=c.querySelector(".pen-board-due");if(!f||!s||!a||!due)return false;const cr=c.getBoundingClientRect(),fr=f.getBoundingClientRect(),sr=s.getBoundingClientRect(),ar=a.getBoundingClientRect();return f.contains(s)&&f.contains(due)&&s.tagName==="SELECT"&&sr.height>=44&&parseFloat(getComputedStyle(s).fontSize)<=12&&sr.width<cr.width&&sr.left>=fr.left-1&&sr.right<=fr.right+1&&fr.left>=cr.left&&fr.right<=cr.right+1&&fr.top>=ar.bottom-1&&f.scrollWidth<=f.clientWidth+1})') "compact secondary footer retains title space and native status target at $width px $theme"
            Check (Eval '(()=>{const fields=[...document.querySelectorAll(".pen-search input,.pen-extra-filters select")];return fields.length>0&&fields.filter(e=>e.getBoundingClientRect().width>0).every(e=>e.getBoundingClientRect().height>=(innerWidth<=760?44:38))})()') "toolbar fields keep readable desktop and mobile sizes at $width px $theme"
            if ($width -gt 760) {
                Check (Eval '(()=>{const lists=[...document.querySelectorAll("[data-board-cards]")];return lists.length===3&&lists.every(e=>e.tabIndex===0&&e.getAttribute("role")==="region"&&!!e.getAttribute("aria-label")&&getComputedStyle(e).overflowY==="auto"&&e.clientHeight<=650)})()') "desktop columns have bounded keyboard-accessible scroll regions at $width px $theme"
                Check (Eval '(()=>{const lists=[...document.querySelectorAll("[data-board-cards]")],busy=lists.find(e=>e.scrollHeight>e.clientHeight),others=lists.filter(e=>e!==busy);if(!busy)return false;const h=busy.closest("section").querySelector("h2"),top=h.getBoundingClientRect().top,page=scrollY,positions=others.map(e=>e.scrollTop);busy.scrollTop=80;const ok=busy.scrollTop>0&&h.getBoundingClientRect().top===top&&scrollY===page&&others.every((e,i)=>e.scrollTop===positions[i]);busy.scrollTop=0;return ok})()') "one column scrolls without moving heading or other columns at $width px $theme"
            } else {
                Check (Eval '[...document.querySelectorAll("[data-board-cards]")].every(e=>getComputedStyle(e).maxHeight==="none"&&e.scrollHeight<=e.clientHeight+1)') "mobile columns use page scrolling at $width px $theme"
            }
        }
    }
    Cdp 'Emulation.setDeviceMetricsOverride' @{width=1440;height=900;deviceScaleFactor=1;mobile=$false} | Out-Null
    Eval 'window.qaScrollRegion=[...document.querySelectorAll("[data-board-cards]")].find(e=>e.scrollHeight>e.clientHeight);qaScrollRegion.scrollIntoView({block:"center"});qaScrollRegion.focus();window.qaPageY=scrollY;window.qaHeaderY=qaScrollRegion.closest("section").querySelector("h2").getBoundingClientRect().top' | Out-Null
    Cdp 'Input.dispatchKeyEvent' @{type='keyDown';key='ArrowDown';code='ArrowDown';windowsVirtualKeyCode=40} | Out-Null
    Cdp 'Input.dispatchKeyEvent' @{type='keyUp';key='ArrowDown';code='ArrowDown';windowsVirtualKeyCode=40} | Out-Null
    Eval 'new Promise(resolve=>setTimeout(()=>resolve(true),250))' | Out-Null
    Check (Eval 'document.activeElement===qaScrollRegion&&qaScrollRegion.matches(":focus-visible")&&getComputedStyle(qaScrollRegion).outlineStyle!=="none"&&qaScrollRegion.scrollTop>0&&scrollY===qaPageY&&qaScrollRegion.closest("section").querySelector("h2").getBoundingClientRect().top===qaHeaderY') 'keyboard scroll region keeps visible focus, heading and page stationary'
    Eval 'qaScrollRegion.scrollTop=0' | Out-Null
    Check (Eval '(()=>{const a=[...document.querySelectorAll(".pen-board-title")].find(e=>e.textContent.length===120);return !!a&&a.scrollWidth<=a.clientWidth+1&&a.textContent.length===120&&!!a.getAttribute("href")})()') 'maximum-length board title stays readable with its full details link'
    Cdp 'Emulation.setDeviceMetricsOverride' @{width=1440;height=900;deviceScaleFactor=1;mobile=$false} | Out-Null
    Eval 'qaCard().querySelector("select").focus()' | Out-Null
    foreach($key in @(@{key='Home';code='Home';windowsVirtualKeyCode=36},@{key='ArrowDown';code='ArrowDown';windowsVirtualKeyCode=40},@{key='Tab';code='Tab';windowsVirtualKeyCode=9})) {
        Cdp 'Input.dispatchKeyEvent' (@{type='keyDown'} + $key) | Out-Null
        Cdp 'Input.dispatchKeyEvent' (@{type='keyUp'} + $key) | Out-Null
        WaitUpdate
    }
    Check (Eval 'qaCard().dataset.status==="1"&&qaCard().closest("[data-board-column]").dataset.boardColumn==="1"&&!qaCard().querySelector("select").disabled') 'real browser keyboard changes native status select'
    Cdp 'Emulation.setDeviceMetricsOverride' @{width=390;height=900;deviceScaleFactor=1;mobile=$true} | Out-Null
    Cdp 'Emulation.setTouchEmulationEnabled' @{enabled=$true;maxTouchPoints=1} | Out-Null
    $tap = Eval '(()=>{const a=qaCard().querySelector("a");a.scrollIntoView({block:"center"});const r=a.getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2}})()'
    Cdp 'Input.dispatchTouchEvent' @{type='touchStart';touchPoints=@(@{x=$tap.x;y=$tap.y})} | Out-Null
    Cdp 'Input.dispatchTouchEvent' @{type='touchEnd';touchPoints=@()} | Out-Null
    Start-Sleep -Milliseconds 250
    Check (Eval ('location.pathname.endsWith("/Tasks/Details/' + $TaskId + '")&&!!document.querySelector("main h1")')) 'emulated touch on board title opens details'
    Cdp 'Emulation.setTouchEmulationEnabled' @{enabled=$false} | Out-Null
    Cdp 'Emulation.setDeviceMetricsOverride' @{width=320;height=900;deviceScaleFactor=1;mobile=$false} | Out-Null
    OpenPage '/Tasks?view=board'
    Eval '(()=>{const c=qaCard(),s=c.querySelector("select");s.focus();s.value="0";s.dispatchEvent(new Event("change",{bubbles:true}))})()' | Out-Null
    WaitUpdate
    Check (Eval 'qaCard().dataset.status==="0"&&qaCard().closest("[data-board-column]").dataset.boardColumn==="0"&&!qaCard().querySelector("select").disabled') 'select alternative persists a successful status update'
    Check (Eval 'document.activeElement===qaCard().querySelector("select")') 'select retains keyboard focus after moving card'
    Check (Eval '[...document.querySelectorAll("[data-board-column]")].every(c=>Number(c.querySelector("[data-board-count]").textContent)===c.querySelectorAll("[data-board-card]").length)') 'column counters match moved cards'
    Check (Eval '(async()=>{const html=await (await fetch("/Tasks?view=list&Filter.PageSize=50")).text();const doc=new DOMParser().parseFromString(html,"text/html");return doc.querySelector(`[data-list-task="${qaTaskId}"] select`).value==="Todo"})()') 'separately requested list reflects saved board status'
    Cdp 'Emulation.setDeviceMetricsOverride' @{width=1440;height=900;deviceScaleFactor=1;mobile=$false} | Out-Null
    $drag = Eval '(()=>{const title=qaCard().querySelector(".pen-board-title"),heading=document.querySelector("[data-board-column=\"1\"] h2"),scroller=title.closest("[data-board-cards]");heading.scrollIntoView({block:"start"});scroller.scrollTop+=title.getBoundingClientRect().top-scroller.getBoundingClientRect().top-16;const a=title.getBoundingClientRect(),b=heading.getBoundingClientRect();const x=a.left+Math.min(a.width/2,24),y=a.top+a.height/2,tx=b.left+20,ty=b.top+b.height/2;return {x,y,tx,ty,source:document.elementFromPoint(x,y)?.closest("a")===title,target:heading.contains(document.elementFromPoint(tx,ty))}})()'
    Check ($drag.source -and $drag.target) 'native drag starts on visible title link and ends on destination heading'
    Eval 'window.qaDragEvents=[];window.qaDragErrors=0;performance.clearResourceTimings();for(const type of ["dragstart","drop"])document.addEventListener(type,e=>qaDragEvents.push({type,target:e.target.tagName,trusted:e.isTrusted,cancelled:e.defaultPrevented}));addEventListener("error",()=>qaDragErrors++);addEventListener("unhandledrejection",()=>qaDragErrors++)' | Out-Null
    Cdp 'Input.dispatchMouseEvent' @{type='mouseMoved';x=$drag.x;y=$drag.y} | Out-Null
    Cdp 'Input.dispatchMouseEvent' @{type='mousePressed';x=$drag.x;y=$drag.y;button='left';buttons=1;clickCount=1} | Out-Null
    Cdp 'Input.dispatchMouseEvent' @{type='mouseMoved';x=($drag.x+12);y=$drag.y;button='left';buttons=1} | Out-Null
    Cdp 'Input.dispatchMouseEvent' @{type='mouseMoved';x=$drag.tx;y=$drag.ty;button='left';buttons=1} | Out-Null
    Cdp 'Input.dispatchMouseEvent' @{type='mouseMoved';x=($drag.tx+1);y=$drag.ty;button='left';buttons=1} | Out-Null
    Cdp 'Input.dispatchMouseEvent' @{type='mouseReleased';x=($drag.tx+1);y=$drag.ty;button='left';buttons=0;clickCount=1} | Out-Null
    WaitUpdate
    $trace = Eval '({events:qaDragEvents,errors:qaDragErrors,status:performance.getEntriesByType("resource").filter(e=>new URL(e.name).pathname==="/Tasks/UpdateBoardStatus").map(e=>e.responseStatus)})'
    Write-Output ('DIAGNOSTIC: native drag ' + ($trace | ConvertTo-Json -Compress -Depth 4))
    Check (Eval 'qaDragEvents.some(e=>e.type==="dragstart"&&e.trusted&&!e.cancelled)&&qaDragEvents.some(e=>e.type==="drop"&&e.trusted)&&qaDragErrors===0') 'Chrome mouse gesture produces trusted drag and drop events without JS errors'
    Check ($trace.status.Count -eq 1 -and $trace.status[0] -eq 200) 'native drag sends exactly one successful UpdateBoardStatus request'
    Check (Eval 'qaCard().dataset.status==="1"&&qaCard().closest("[data-board-column]").dataset.boardColumn==="1"') 'native title drag moves card after successful fetch'
    OpenPage '/Tasks?view=board'
    Check (Eval 'qaCard().dataset.status==="1"&&qaCard().closest("[data-board-column]").dataset.boardColumn==="1"') 'fresh board request reads the dragged status from PostgreSQL'
    Eval '(()=>{window.qaOriginalFetch=window.fetch;window.qaPreviousNext=qaCard().nextElementSibling;window.fetch=async()=>new Response(JSON.stringify({success:false,message:"Durum kaydedilemedi. Lütfen tekrar deneyin."}),{status:503,headers:{"Content-Type":"application/json"}});const s=qaCard().querySelector("select");s.value="2";s.dispatchEvent(new Event("change",{bubbles:true}));})()' | Out-Null
    WaitUpdate
    Check (Eval 'qaCard().dataset.status==="1"&&qaCard().closest("[data-board-column]").dataset.boardColumn==="1"&&qaCard().nextElementSibling===window.qaPreviousNext&&qaCard().querySelector("select").value==="1"') 'server failure restores previous column, order and select'
    Check (Eval 'document.querySelector("[data-board-message]").textContent.includes("Durum kaydedilemedi")') 'server failure exposes Turkish live message'
    Eval '(()=>{window.fetch=async()=>{throw new TypeError("Failed to fetch")};const s=qaCard().querySelector("select");s.value="2";s.dispatchEvent(new Event("change",{bubbles:true}));})()' | Out-Null
    WaitUpdate
    Check (Eval 'qaCard().dataset.status==="1"&&!document.querySelector("[data-board-message]").textContent.includes("Failed to fetch")') 'network error rolls back and uses Turkish message'
    Eval '(()=>{window.fetch=()=>new Promise(resolve=>window.qaResolve=resolve);const s=qaCard().querySelector("select");s.focus();s.value="2";s.dispatchEvent(new Event("change",{bubbles:true}))})()' | Out-Null
    Check (Eval 'qaCard().getAttribute("aria-busy")==="true"&&qaCard().querySelector("select").disabled&&!qaCard().draggable&&qaCard().dataset.pending==="true"') 'pending request prevents duplicate changes and announces busy state'
    Eval 'window.qaResolve(new Response("not-json",{status:200,headers:{"Content-Type":"text/plain"}}))' | Out-Null
    WaitUpdate
    Check (Eval 'qaCard().dataset.status==="1"&&qaCard().closest("[data-board-column]").dataset.boardColumn==="1"&&qaCard().nextElementSibling===window.qaPreviousNext&&qaCard().querySelector("select").value==="1"&&!qaCard().querySelector("select").disabled&&qaCard().draggable&&!qaCard().hasAttribute("aria-busy")&&document.activeElement===qaCard().querySelector("select")') 'malformed response restores card order, select, focus and interaction'
    Check (Eval '[...document.querySelectorAll("[data-board-column]")].every(c=>Number(c.querySelector("[data-board-count]").textContent)===c.querySelectorAll("[data-board-card]").length)&&document.querySelector("[data-board-message]").dataset.error==="true"') 'failed pending update restores counts and accessible error feedback'
    Eval 'window.fetch=window.qaOriginalFetch' | Out-Null
    OpenPage '/Tasks/Create'
    Check (Eval 'document.documentElement.dataset.theme==="dark"&&!!document.querySelector("form[novalidate]")') 'theme and existing create form survive navigation'
    OpenPage '/Tasks?Filter.PageSize=50'
    Check (Eval '!!document.querySelector("[data-board-view]")&&localStorage.getItem("workflow-task-view")==="board"') 'board preference survives navigation without explicit view'
    Check (Eval ('document.querySelector(`[data-board-card][data-task-id="' + $TaskId + '"]`).dataset.status==="1"')) 'reload confirms saved status and failed updates did not persist'
    OpenPage ("/Tasks/Edit/$TaskId")
    Check (Eval '!!document.querySelector("form[novalidate]")&&!!document.querySelector("#Title")') 'existing edit form renders'
    OpenPage ("/Tasks/Details/$TaskId")
    Check (Eval '!!document.querySelector("main h1")&&document.documentElement.scrollWidth<=innerWidth') 'existing details layout fits mobile'
    OpenPage '/Tasks?Filter.Search=nonexistent-kanban-qa-value'
    Check (Eval '!!document.querySelector("[data-board-view]")&&document.querySelectorAll("[data-board-column]").length===3&&[...document.querySelectorAll("[data-board-count]")].every(e=>e.textContent==="0")') 'empty board keeps three zero-count columns'
    OpenPage '/Tasks?view=board&Filter.Status=1&Filter.Page=2&Filter.PageSize=5'
    Eval '(()=>{const s=qaCard().querySelector("select");s.value="2";s.dispatchEvent(new Event("change",{bubbles:true}))})()' | Out-Null
    WaitUpdate
    Check (Eval 'qaCard().dataset.status==="2"&&document.querySelector("[data-board-message]").dataset.error==="false"') 'legacy list filter does not prevent successful board move'
    OpenPage '/Tasks?view=board&Filter.Status=1&Filter.Page=2&Filter.PageSize=5'
    Check (Eval 'qaCard().dataset.status==="2"&&document.querySelectorAll("[data-board-card]").length>10') 'board refresh retains all statuses despite legacy status and page'
    OpenPage '/Tasks?Filter.PageSize=50'
    ClickView 'list'
    OpenPage '/Tasks?Filter.PageSize=50'
    Check (Eval '!document.querySelector("[data-board-view]")&&document.querySelector("[data-task-view=list]").getAttribute("aria-current")==="page"') 'list preference also persists'
    Eval 'localStorage.setItem("workflow-task-view","board")' | Out-Null
    OpenPage '/Tasks?view=list&Filter.PageSize=50'
    Check (Eval '!!document.querySelector("[data-list-view]")&&!document.querySelector("[data-board-view]")&&localStorage.getItem("workflow-task-view")==="list"') 'explicit list URL overrides stored board preference'
    OpenPage '/Tasks?view=list&Filter.Status=0&Filter.Page=2&Filter.PageSize=5'
    ClickView 'board'
    Check (Eval 'document.querySelectorAll("[data-board-card]").length>10&&!document.querySelector(".pen-status-tabs")') 'filtered second list page transitions to complete unfiltered board'
    ClickView 'list'
    Check (Eval '(()=>{const q=new URL(location.href).searchParams;return q.get("view")==="list"&&["0","Todo"].includes(q.get("Filter.Status"))&&q.get("Filter.Page")==="2"&&q.get("Filter.PageSize")==="5"&&!document.querySelector("[data-board-view]")&&document.querySelectorAll("[data-list-task]").length>0&&document.querySelectorAll("[data-list-task]").length<=5})()') 'return link preserves selected status, second page and page size'
    foreach($width in @(1440,390,320)) {
        Cdp 'Emulation.setDeviceMetricsOverride' @{width=$width;height=900;deviceScaleFactor=1;mobile=$false} | Out-Null
        foreach($theme in @('light','dark')) {
            OpenPage '/Tasks?view=list&Filter.Status=0&Filter.Priority=3&Filter.PageSize=20&Filter.Sort=Oldest'
            Eval ('document.querySelector(`[data-theme-choice="' + $theme + '"]`).click();document.querySelector(".pen-search").requestSubmit()') | Out-Null
            Start-Sleep -Milliseconds 200
            Eval 'new Promise(resolve=>{function wait(){if(document.readyState==="complete"&&document.querySelector("main"))document.fonts.ready.then(()=>resolve(true));else setTimeout(wait,30)}wait()})' | Out-Null
            Check (Eval '(()=>{const q=new URL(location.href).searchParams,fields=[...document.querySelectorAll(".pen-search input[type=search],.pen-extra-filters select")];return fields.length===4&&fields.every(e=>{const r=e.getBoundingClientRect();return r.height>=(innerWidth<=760?44:38)&&r.left>=0&&r.right<=innerWidth})&&document.documentElement.scrollWidth<=innerWidth&&q.get("view")==="list"&&["0","Todo"].includes(q.get("Filter.Status"))&&["3","Critical"].includes(q.get("Filter.Priority"))&&q.get("Filter.PageSize")==="20"&&["1","Oldest"].includes(q.get("Filter.Sort"))&&!document.querySelector("[data-board-view]")})()') "list toolbar submission retains filters and readable fields at $width px $theme"
        }
    }
    Write-Output "$script:passed browser checks passed. Owned test task only; browser profile retained in temporary directory."
} finally {
    $socket.Dispose()
    if (-not $chrome.HasExited) { Stop-Process -Id $chrome.Id -ErrorAction SilentlyContinue }
}
