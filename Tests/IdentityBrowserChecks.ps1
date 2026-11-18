[CmdletBinding()]
param([Parameter(Mandatory)][string]$BaseUrl)
$ErrorActionPreference = 'Stop'
if ($BaseUrl -notmatch '^https://127\.0\.0\.1:\d+$' -or $env:WORKFLOW_BROWSER_CERT_SPKI -notmatch '^[A-Za-z0-9+/]{43}=$' -or [string]::IsNullOrWhiteSpace($env:WORKFLOW_BROWSER_COOKIE)) { throw 'Pinned isolated HTTPS and a real authenticated fixture are required.' }
$probe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$probe.Start();$debugPort=$probe.LocalEndpoint.Port;$probe.Stop()
$profile=Join-Path ([IO.Path]::GetTempPath()) ('workflow-identity-'+[Guid]::NewGuid().ToString('N'))
$chrome=Start-Process 'C:\Program Files\Google\Chrome\Application\chrome.exe' -WindowStyle Hidden -PassThru -ArgumentList @('--headless=new',"--ignore-certificate-errors-spki-list=$env:WORKFLOW_BROWSER_CERT_SPKI",'--remote-debugging-address=127.0.0.1',"--remote-debugging-port=$debugPort","--user-data-dir=$profile",'--no-first-run','--no-default-browser-check','about:blank')
$socket=[Net.WebSockets.ClientWebSocket]::new();$script:messageId=0;$script:passed=0
function Cdp($method,$parameters) {
    $timeout=[Threading.CancellationTokenSource]::new(15000);$script:messageId++;$id=$script:messageId
    $bytes=[Text.Encoding]::UTF8.GetBytes((@{id=$id;method=$method;params=$parameters}|ConvertTo-Json -Depth 30 -Compress))
    $socket.SendAsync([ArraySegment[byte]]::new($bytes),[Net.WebSockets.WebSocketMessageType]::Text,$true,$timeout.Token).GetAwaiter().GetResult()|Out-Null
    do {
        $stream=[IO.MemoryStream]::new()
        do {$buffer=New-Object byte[] 65536;$received=$socket.ReceiveAsync([ArraySegment[byte]]::new($buffer),$timeout.Token).GetAwaiter().GetResult();$stream.Write($buffer,0,$received.Count)}while(-not $received.EndOfMessage)
        $reply=[Text.Encoding]::UTF8.GetString($stream.ToArray())|ConvertFrom-Json;$stream.Dispose()
    }while($reply.id -ne $id)
    $timeout.Dispose();if($reply.error){throw ('CDP failed: '+$method)};return $reply.result
}
function Eval($expression) {$r=Cdp 'Runtime.evaluate' @{expression=$expression;returnByValue=$true;awaitPromise=$true};if($r.exceptionDetails){throw 'Identity browser evaluation failed.'};return $r.result.value}
function WaitPage {Start-Sleep -Milliseconds 180;Eval 'new Promise(resolve=>{function wait(){if(document.readyState==="complete"&&document.querySelector("main"))document.fonts.ready.then(()=>resolve(true));else setTimeout(wait,30)}wait()})'|Out-Null}
function OpenPage($path) {Cdp 'Page.navigate' @{url=($BaseUrl+$path)}|Out-Null;WaitPage}
function Check($ok,$label) {if(-not $ok){throw ('FAIL: '+$label)};$script:passed++;Write-Output ('PASS: Identity browser '+$label)}
try {
    for($i=0;$i -lt 30;$i++){try{$targets=Invoke-RestMethod "http://127.0.0.1:$debugPort/json/list" -TimeoutSec 1;break}catch{Start-Sleep -Milliseconds 100}}
    $page=$targets|Where-Object type -eq 'page'|Select-Object -First 1
    $socket.ConnectAsync([Uri]$page.webSocketDebuggerUrl,[Threading.CancellationToken]::None).GetAwaiter().GetResult()|Out-Null
    Cdp 'Page.enable' @{}|Out-Null;Cdp 'Network.enable' @{}|Out-Null
    $cookie=Cdp 'Network.setCookie' @{name='.WorkFlow.Identity';value=$env:WORKFLOW_BROWSER_COOKIE;url=$BaseUrl;secure=$true;httpOnly=$true;sameSite='Lax'}
    if(-not $cookie.success){throw 'Real authenticated test cookie was rejected.'}
    OpenPage '/Tasks?view=list'
    Check (Eval '!!document.querySelector(".pen-account-name")&&document.querySelector(".pen-account-name").textContent.trim().length>0&&[...document.querySelectorAll("form[action=\"/Account/Logout\"]")].every(f=>f.method==="post"&&!!f.querySelector("[name=__RequestVerificationToken]"))') 'authenticated layout shows account name and antiforgery POST logout'
    Cdp 'Emulation.setDeviceMetricsOverride' @{width=390;height=900;deviceScaleFactor=1;mobile=$false}|Out-Null
    Cdp 'Emulation.setScriptExecutionDisabled' @{value=$true}|Out-Null
    OpenPage '/Tasks?view=list'
    Check (Eval '(()=>{const f=document.querySelector(".pen-noscript-nav form[action=\"/Account/Logout\"]");return !!f&&f.getBoundingClientRect().width>0&&f.method==="post"&&!!f.querySelector("[name=__RequestVerificationToken]")})()') 'mobile no-script navigation retains a working logout form'
    Eval 'document.querySelector(".pen-noscript-nav form[action=\"/Account/Logout\"]").requestSubmit()'|Out-Null;WaitPage
    Check (Eval 'location.pathname==="/Account/Login"&&!document.querySelector("form[action=\"/Account/Logout\"]")') 'native no-script logout POST removes the authenticated session'
    Cdp 'Emulation.setScriptExecutionDisabled' @{value=$false}|Out-Null
    foreach($action in @('Login','Register')) {
        OpenPage ('/Account/'+$action+'?ReturnUrl=%2FCalendar%3Fview%3Dday')
        Check (Eval ('(()=>{const f=document.querySelector(".pen-auth form");return !!f&&f.noValidate&&f.method==="post"&&!!f.querySelector("[name=__RequestVerificationToken]")&&f.querySelector("[name=ReturnUrl]").value==="/Calendar?view=day"&&f.querySelector("#Email").autocomplete==="email"&&f.querySelector("#Password").autocomplete==="'+$(if($action -eq 'Login'){'current-password'}else{'new-password'})+'"&&[...f.querySelectorAll("input[type=password]")].every(e=>e.value===""&&e.labels.length===1&&!!document.getElementById(e.getAttribute("aria-describedby")))})()')) "$action form preserves safe return URL and accessible empty password fields"
        foreach($width in @(1440,768,390,320)) {
            Cdp 'Emulation.setDeviceMetricsOverride' @{width=$width;height=900;deviceScaleFactor=1;mobile=$false}|Out-Null
            foreach($theme in @('light','dark')) {
                Eval ('document.querySelector("[data-theme-choice='+$theme+']").click()')|Out-Null
                Check (Eval 'document.documentElement.scrollWidth<=innerWidth&&[...document.querySelectorAll(".pen-auth .pen-field input")].every(e=>{const r=e.getBoundingClientRect();return r.height>=44&&r.left>=0&&r.right<=innerWidth})') "$action form fits readable fields at $width px $theme"
            }
        }
        Eval 'document.querySelector(".pen-auth form").requestSubmit()'|Out-Null;WaitPage
        Check (Eval '!!document.querySelector("[data-validation-alert][role=alert]")&&[...document.querySelectorAll(".pen-auth .pen-field input")].every(e=>e.getAttribute("aria-invalid")==="true"&&!!document.getElementById(e.getAttribute("aria-describedby")))&&[...document.querySelectorAll("input[type=password]")].every(e=>e.value==="")') "$action blank submission exposes field errors and keeps passwords empty"
        Check (Eval 'document.activeElement===document.querySelector("[data-validation-alert]")&&document.documentElement.dataset.theme==="dark"') "$action validation receives keyboard focus and retains theme"
    }
    Check (Eval '!!document.querySelector("a[href=\"/Account/Login\"]")&&!!document.querySelector("a[href=\"/Account/Register\"]")&&!document.querySelector("form[action=\"/Account/Logout\"]")') 'anonymous layout exposes login and registration without logout'
    Write-Output "$script:passed identity browser checks passed. Owned HTTPS account fixtures only."
} finally {$socket.Dispose();if(-not $chrome.HasExited){Stop-Process -Id $chrome.Id -ErrorAction SilentlyContinue}}
