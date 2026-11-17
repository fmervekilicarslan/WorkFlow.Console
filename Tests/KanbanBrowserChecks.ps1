[CmdletBinding()]
param([Parameter(Mandatory)][string]$BaseUrl, [Parameter(Mandatory)][Guid]$TaskId)
$ErrorActionPreference = 'Stop'
if ($BaseUrl -notmatch '^http://127\.0\.0\.1:\d+$') { throw 'An isolated local test server is required.' }
$chromePath = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
if (-not (Test-Path -LiteralPath $chromePath)) { throw 'Chrome is required for optional browser checks.' }
$probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$probe.Start(); $debugPort = $probe.LocalEndpoint.Port; $probe.Stop()
$profile = Join-Path ([IO.Path]::GetTempPath()) ('workflow-kanban-' + [Guid]::NewGuid().ToString('N'))
$chrome = Start-Process $chromePath -WindowStyle Hidden -PassThru -ArgumentList @('--headless=new', '--remote-debugging-address=127.0.0.1', "--remote-debugging-port=$debugPort", "--user-data-dir=$profile", '--no-first-run', '--no-default-browser-check', 'about:blank')
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
    OpenPage '/Tasks?view=list&Filter.PageSize=50'
    $listIds = Eval 'JSON.stringify([...document.querySelectorAll("[data-list-task]")].map(e=>e.dataset.listTask).sort())'
    ClickView 'board'
    $boardIds = Eval 'JSON.stringify([...document.querySelectorAll("[data-board-card]")].map(e=>e.dataset.taskId).sort())'
    Check ($listIds -eq $boardIds -and $boardIds.Length -gt 10) 'separate list and board pages render the same complete task IDs'
    Check (Eval '!!document.querySelector("[data-board-view]")&&document.querySelector("[data-task-view=board]").getAttribute("aria-current")==="page"&&new URL(location.href).searchParams.get("view")==="board"') 'board link changes URL view and accessible state'
    Check (Eval 'document.querySelectorAll("[data-board-card]").length>10&&!document.querySelector("[data-list-view]")') 'board renders all matched tasks and excludes inactive list DOM'
    Check (Eval '!document.querySelector(".pen-pagination,.pen-status-tabs,select[name=\"Filter.PageSize\"],select[name=\"Filter.Sort\"]")') 'board omits list-only controls'
    foreach($width in @(1440,390,320)) {
        Cdp 'Emulation.setDeviceMetricsOverride' @{width=$width;height=900;deviceScaleFactor=1;mobile=$false} | Out-Null
        foreach($theme in @('light','dark')) {
            Eval ('document.querySelector(`[data-theme-choice="' + $theme + '"]`).click()') | Out-Null
            Check (Eval 'document.documentElement.scrollWidth<=innerWidth&&[...document.querySelectorAll("[data-board-card]")].every(e=>{const r=e.getBoundingClientRect();return r.width>=200&&r.right<=innerWidth&&r.left>=0})') "readable cards and no horizontal overflow: $width px $theme"
            Check (Eval ('document.documentElement.dataset.theme==="' + $theme + '"')) "theme applies: $width px $theme"
        }
    }
    Eval '(()=>{const c=qaCard(),s=c.querySelector("select");s.focus();s.value="0";s.dispatchEvent(new Event("change",{bubbles:true}))})()' | Out-Null
    WaitUpdate
    Check (Eval 'qaCard().dataset.status==="0"&&qaCard().closest("[data-board-column]").dataset.boardColumn==="0"&&!qaCard().querySelector("select").disabled') 'select alternative persists a successful status update'
    Check (Eval 'document.activeElement===qaCard().querySelector("select")') 'select retains keyboard focus after moving card'
    Check (Eval '[...document.querySelectorAll("[data-board-column]")].every(c=>Number(c.querySelector("[data-board-count]").textContent)===c.querySelectorAll("[data-board-card]").length)') 'column counters match moved cards'
    Check (Eval '(async()=>{const html=await (await fetch("/Tasks?view=list&Filter.PageSize=50")).text();const doc=new DOMParser().parseFromString(html,"text/html");return doc.querySelector(`[data-list-task="${qaTaskId}"] select`).value==="Todo"})()') 'separately requested list reflects saved board status'
    # Synthetic HTML drag/drop exercises the actual registered handlers without writing outside the owned test task.
    Eval '(()=>{const c=qaCard(),target=document.querySelector("[data-board-column=\"1\"]"),data=new DataTransfer();c.dispatchEvent(new DragEvent("dragstart",{bubbles:true,dataTransfer:data}));target.dispatchEvent(new DragEvent("dragover",{bubbles:true,cancelable:true,dataTransfer:data}));target.dispatchEvent(new DragEvent("drop",{bubbles:true,cancelable:true,dataTransfer:data}));c.dispatchEvent(new DragEvent("dragend",{bubbles:true,dataTransfer:data}))})()' | Out-Null
    WaitUpdate
    Check (Eval 'qaCard().dataset.status==="1"&&qaCard().closest("[data-board-column]").dataset.boardColumn==="1"') 'drag/drop handlers move card after successful fetch'
    Eval '(()=>{window.qaOriginalFetch=window.fetch;window.qaPreviousNext=qaCard().nextElementSibling;window.fetch=async()=>new Response(JSON.stringify({success:false,message:"Durum kaydedilemedi. Lütfen tekrar deneyin."}),{status:503,headers:{"Content-Type":"application/json"}});const s=qaCard().querySelector("select");s.value="2";s.dispatchEvent(new Event("change",{bubbles:true}));})()' | Out-Null
    WaitUpdate
    Check (Eval 'qaCard().dataset.status==="1"&&qaCard().closest("[data-board-column]").dataset.boardColumn==="1"&&qaCard().nextElementSibling===window.qaPreviousNext&&qaCard().querySelector("select").value==="1"') 'server failure restores previous column, order and select'
    Check (Eval 'document.querySelector("[data-board-message]").textContent.includes("Durum kaydedilemedi")') 'server failure exposes Turkish live message'
    Eval '(()=>{window.fetch=async()=>{throw new TypeError("Failed to fetch")};const s=qaCard().querySelector("select");s.value="2";s.dispatchEvent(new Event("change",{bubbles:true}));})()' | Out-Null
    WaitUpdate
    Check (Eval 'qaCard().dataset.status==="1"&&!document.querySelector("[data-board-message]").textContent.includes("Failed to fetch")') 'network error rolls back and uses Turkish message'
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
    Write-Output "$script:passed browser checks passed. Owned test task only; browser profile retained in temporary directory."
} finally {
    $socket.Dispose()
    if (-not $chrome.HasExited) { Stop-Process -Id $chrome.Id -ErrorAction SilentlyContinue }
}
