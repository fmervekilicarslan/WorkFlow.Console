[CmdletBinding()]
param([Parameter(Mandatory)][string]$BaseUrl)
$ErrorActionPreference = 'Stop'
if ($BaseUrl -notmatch '^https://127\.0\.0\.1:\d+$' -or $env:WORKFLOW_BROWSER_CERT_SPKI -notmatch '^[A-Za-z0-9+/]{43}=$' -or [string]::IsNullOrWhiteSpace($env:WORKFLOW_BROWSER_COOKIE)) { throw 'Pinned isolated HTTPS and a real authenticated fixture are required.' }
if ($env:WORKFLOW_BROWSER_LOGIN_EMAIL -notmatch '^auth-[a-f0-9]{32}-b@example\.invalid$' -or $env:WORKFLOW_BROWSER_REGISTER_EMAIL -notmatch '^auth-[a-f0-9]{32}-browser@example\.invalid$' -or [string]::IsNullOrWhiteSpace($env:WORKFLOW_BROWSER_PASSWORD)) { throw 'Isolated browser registration credentials are required.' }
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
function Upload-ProfilePhoto($path) {
    $root = (Cdp 'DOM.getDocument' @{}).root.nodeId
    $node = (Cdp 'DOM.querySelector' @{nodeId=$root;selector='#profile-photo'}).nodeId
    if (!$node) { throw 'Profile photo file control not found.' }
    Cdp 'DOM.setFileInputFiles' @{nodeId=$node;files=@($path)}|Out-Null
    Eval 'document.querySelector("#profile-photo").form.requestSubmit()'|Out-Null
    WaitPage
}
$photos = @()
try {
    for($i=0;$i -lt 30;$i++){try{$targets=Invoke-RestMethod "http://127.0.0.1:$debugPort/json/list" -TimeoutSec 1;break}catch{Start-Sleep -Milliseconds 100}}
    $page=$targets|Where-Object type -eq 'page'|Select-Object -First 1
    $socket.ConnectAsync([Uri]$page.webSocketDebuggerUrl,[Threading.CancellationToken]::None).GetAwaiter().GetResult()|Out-Null
    Cdp 'Page.enable' @{}|Out-Null;Cdp 'Network.enable' @{}|Out-Null
    OpenPage '/'
    Check (Eval 'location.pathname==="/"&&!!document.querySelector(".pen-welcome-features")&&!!document.querySelector("a[href=\"/Account/Login\"]")&&!!document.querySelector("a[href=\"/Account/Register\"]")&&!document.querySelector("[data-board-card],[data-list-task],.pen-account-menu")') 'anonymous home shows public features and account links without private data'
    foreach($width in @(1440,390,320)) {
        Cdp 'Emulation.setDeviceMetricsOverride' @{width=$width;height=900;deviceScaleFactor=1;mobile=$false}|Out-Null
        foreach($theme in @('light','dark')) {
            Check (Eval ('(()=>{const b=document.querySelector("[data-theme-choice='+$theme+']");b.click();return document.documentElement.dataset.theme==="'+$theme+'"&&document.documentElement.scrollWidth<=innerWidth})()')) "public home fits at $width px $theme"
        }
    }
    $cookie=Cdp 'Network.setCookie' @{name='.WorkFlow.Identity';value=$env:WORKFLOW_BROWSER_COOKIE;url=$BaseUrl;secure=$true;httpOnly=$true;sameSite='Lax'}
    if(-not $cookie.success){throw 'Real authenticated test cookie was rejected.'}
    Cdp 'Emulation.setDeviceMetricsOverride' @{width=1440;height=900;deviceScaleFactor=1;mobile=$false}|Out-Null
    OpenPage '/'
    Check (Eval '!!document.querySelector(".pen-welcome-actions a[href=\"/Tasks\"]")&&!document.querySelector(".pen-welcome-actions a[href=\"/Account/Register\"]")') 'signed-in home links directly to the workspace'
    OpenPage '/Tasks?view=list'
    Check (Eval '!!document.querySelector(".pen-account-menu summary")') 'authenticated layout renders a native avatar menu summary'
    Check (Eval '(()=>{const s=document.querySelector(".pen-account-menu summary"),n=s?.querySelector(".pen-account-name")?.textContent.trim(),a=s?.getAttribute("aria-label");return s?.tagName==="SUMMARY"&&s.tabIndex>=0&&!!a&&!!n&&a.endsWith(n)})()') 'avatar summary has a keyboard focus target and descriptive accessible name'
    Check (Eval '(()=>{const d=document.querySelector(".pen-account-menu");return !!d.querySelector("a[href=\"/Account/Profile\"]")&&!!d.querySelector("a[href=\"/Account/Settings\"]")})()') 'avatar menu links to profile and account settings'
    Check (Eval '(()=>{const f=[...document.querySelectorAll(".pen-account-menu form[action=\"/Account/Logout\"]")];return f.length===1&&f[0].method==="post"&&!!f[0].querySelector("[name=__RequestVerificationToken]")})()') 'avatar menu preserves antiforgery-protected POST logout'
    Check (Eval '(()=>{const s=document.querySelector(".pen-account-menu summary");return s?.tagName==="SUMMARY"&&s.tabIndex===0})()') 'avatar summary is a native keyboard focus target in the tab sequence'
    Check (Eval '(()=>{const s=document.querySelector(".pen-account-menu summary");s.focus();return document.activeElement===s})()') 'avatar menu summary receives keyboard focus'
    Check (Eval '(()=>{const s=document.querySelector(".pen-account-menu summary"),d=document.querySelector(".pen-account-menu");s.click();return d.open})()') 'native avatar control opens the menu'
    Eval 'document.querySelector(".pen-account-menu a").focus()'|Out-Null
    Cdp 'Input.dispatchKeyEvent' @{type='keyDown';key='Escape';code='Escape';windowsVirtualKeyCode=27}|Out-Null
    Check (Eval '(()=>{const d=document.querySelector(".pen-account-menu");return !d.open&&document.activeElement===d.querySelector("summary")})()') 'Escape closes the account menu and restores focus'
    Check (Eval '(()=>{const d=document.querySelector(".pen-account-menu");d.querySelector("summary").click();document.querySelector("main").click();return !d.open})()') 'outside click closes the account menu'
    OpenPage '/Account/Profile'
    Check (Eval '(()=>{const f=document.querySelector(".pen-account-card form"),n=f?.querySelector("#DisplayName"),e=f?.querySelector("#profile-email");return !!f&&f.method==="post"&&!!f.querySelector("[name=__RequestVerificationToken]")&&n?.maxLength===80&&e?.readOnly===true&&e.type==="email"})()') 'profile form validates visible name and keeps email read-only'
    Eval '(()=>{const n=document.querySelector("#DisplayName");sessionStorage.setItem("qa-saved-name",n.value);n.value="Unsaved browser draft";document.querySelector(".pen-account-card form a[href=\"/Account/Profile\"]").click()})()'|Out-Null
    WaitPage
    Check (Eval 'location.pathname==="/Account/Profile"&&document.querySelector("#DisplayName").value===sessionStorage.getItem("qa-saved-name")') 'profile Cancel restores the saved display name'
    Check (Eval '!!document.querySelector(".pen-profile-summary time[datetime]")&&!!document.querySelector("#AboutMe")&&!document.querySelector(".pen-profile-avatar img")&&document.querySelector(".pen-profile-avatar").textContent.trim().length>0') 'new profile shows its membership date and fallback initials'
    Eval 'document.querySelector("#DisplayName").value="Browser QA";document.querySelector("#AboutMe").value="Browser profile check";document.querySelector("#AboutMe").form.requestSubmit()'|Out-Null
    WaitPage
    Check (Eval 'document.querySelector("[data-profile-saved-name]").textContent.trim()==="Browser QA"&&document.querySelector("#AboutMe").value==="Browser profile check"') 'profile form persists name and bio in a real browser'
    Add-Type -AssemblyName System.Drawing
    foreach($color in @([Drawing.Color]::Crimson,[Drawing.Color]::RoyalBlue)){
        $file=Join-Path $env:TEMP ("workflow-profile-qa-"+[Guid]::NewGuid().ToString('N')+".png")
        $photos += $file
        $bitmap=[Drawing.Bitmap]::new(16,16)
        $graphics=[Drawing.Graphics]::FromImage($bitmap)
        try{$graphics.Clear($color);$bitmap.Save($file,[Drawing.Imaging.ImageFormat]::Png)}
        finally{$graphics.Dispose();$bitmap.Dispose()}
    }
    Upload-ProfilePhoto $photos[0]
    $firstPhoto=Eval 'document.querySelector(".pen-profile-avatar img")?.getAttribute("src")'
    Check (Eval '(()=>{const a=document.querySelector(".pen-profile-avatar img"),b=document.querySelector(".pen-account-menu img");return a?.naturalWidth===256&&b?.naturalWidth===256&&a.src===b.src})()') 'first photo upload renders the same normalized avatar in profile and header'
    Upload-ProfilePhoto $photos[1]
    $replacementPhoto=Eval 'document.querySelector(".pen-profile-avatar img")?.getAttribute("src")'
    Check ($replacementPhoto -and $replacementPhoto -ne $firstPhoto) 'second browser upload replaces photo with a new key'
    Check (Eval ('(async()=>{const r=await fetch('+($firstPhoto|ConvertTo-Json -Compress)+');return r.status===404})()')) 'replaced photo key is no longer readable'
    foreach($width in @(1440,768,390,320)) {
        Cdp 'Emulation.setDeviceMetricsOverride' @{width=$width;height=900;deviceScaleFactor=1;mobile=$false}|Out-Null
        foreach($theme in @('light','dark')) {
            Eval ('document.querySelector("[data-theme-choice='+$theme+']").click()')|Out-Null
            Check (Eval 'document.documentElement.scrollWidth<=innerWidth&&[...document.querySelectorAll(".pen-account-card")].every(e=>e.getBoundingClientRect().right<=innerWidth)&&document.querySelector("#profile-photo").getBoundingClientRect().right<=innerWidth') "profile and upload controls fit at $width px $theme"
        }
    }
    OpenPage '/Account/Settings'
    Check (Eval '!!document.querySelector("main #settings-heading")&&!!document.querySelector("main a[href=\"/Account/Profile\"]")&&!!document.querySelector("#settings-password-form")') 'settings links to the profile and password form'
    Check (Eval '(()=>{const f=document.querySelector("#settings-password-form");return f.method==="post"&&new URL(f.action).pathname==="/Account/ChangePassword"&&!!f.querySelector("[name=__RequestVerificationToken]")&&["CurrentPassword","NewPassword","ConfirmPassword"].every(id=>{const n=f.querySelector("#"+id),described=n?.getAttribute("aria-describedby")?.trim().split(/\s+/);return n?.type==="password"&&n.value===""&&n.labels.length===1&&n.autocomplete===(id==="CurrentPassword"?"current-password":"new-password")&&described?.length>0&&described.every(target=>!!document.getElementById(target))})})()') 'password form has CSRF and labelled empty password fields with correct autocomplete'
    foreach($width in @(1440,768,390,320)) {
        Cdp 'Emulation.setDeviceMetricsOverride' @{width=$width;height=900;deviceScaleFactor=1;mobile=$false}|Out-Null
        foreach($theme in @('light','dark')) {
            Check (Eval ('(()=>{const b=document.querySelector("main [data-theme-choice='+$theme+']");if(!b?.getBoundingClientRect().width)return false;b.click();return document.documentElement.dataset.theme==="'+$theme+'"&&b.getAttribute("aria-pressed")==="true"&&document.documentElement.scrollWidth<=innerWidth})()')) "settings applies the theme and fits at $width px $theme"
            Check (Eval '[...document.querySelectorAll("#settings-password-form input[type=password]")].length===3&&[...document.querySelectorAll("#settings-password-form input[type=password]")].every(n=>{const r=n.getBoundingClientRect();return r.width>0&&r.height>=44&&r.left>=0&&r.right<=innerWidth})') "password fields fit with touch targets at $width px $theme"
        }
    }
    Eval 'document.querySelector("#settings-password-form").requestSubmit()'|Out-Null;WaitPage
    Check (Eval '!!document.querySelector("#settings-password-form [role=alert]")&&[...document.querySelectorAll("#settings-password-form input[type=password]")].every(n=>n.value===""&&n.getAttribute("aria-invalid")==="true")') 'empty password submission exposes errors and leaves all secrets empty'
    Check (Eval 'document.activeElement===document.querySelector("#settings-password-form [data-validation-alert]")') 'password validation alert receives keyboard focus'
    Cdp 'Emulation.setDeviceMetricsOverride' @{width=390;height=900;deviceScaleFactor=1;mobile=$false}|Out-Null
    Cdp 'Emulation.setScriptExecutionDisabled' @{value=$true}|Out-Null
    OpenPage '/Tasks?view=list'
    Check (Eval '(()=>{const n=document.querySelector(".pen-noscript-nav"),f=n?.querySelector("form[action=\"/Account/Logout\"]");return !!f&&f.getBoundingClientRect().width>0&&f.method==="post"&&!!f.querySelector("[name=__RequestVerificationToken]")&&!!n.querySelector("a[href=\"/Account/Profile\"]")&&!!n.querySelector("a[href=\"/Account/Settings\"]")})()') 'mobile no-script navigation retains profile, settings and CSRF logout'
    Eval 'document.querySelector(".pen-noscript-nav form[action=\"/Account/Logout\"]").requestSubmit()'|Out-Null;WaitPage
    Check (Eval 'location.pathname==="/"&&!!document.querySelector(".pen-welcome-features")&&!document.querySelector("form[action=\"/Account/Logout\"]")') 'native no-script logout clears session and returns to public home'
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
    OpenPage '/Account/Profile'
    Check (Eval 'location.pathname==="/Account/Login"&&new URL(location.href).searchParams.get("ReturnUrl")==="/Account/Profile"') 'logged-out browser cannot open the profile'
    Cdp 'Emulation.setDeviceMetricsOverride' @{width=1440;height=900;deviceScaleFactor=1;mobile=$false}|Out-Null
    $passwordJson=$env:WORKFLOW_BROWSER_PASSWORD|ConvertTo-Json -Compress
    $emailJson=$env:WORKFLOW_BROWSER_REGISTER_EMAIL|ConvertTo-Json -Compress
    OpenPage '/Account/Register'
    Eval ('document.querySelector("#Email").value='+$emailJson+';document.querySelector("#Password").value='+$passwordJson+';document.querySelector("#ConfirmPassword").value='+$passwordJson+';document.querySelector(".pen-auth form").requestSubmit()')|Out-Null
    WaitPage
    Check (Eval 'location.pathname==="/Tasks"&&!!document.querySelector(".pen-account-menu")') 'registering from the public flow opens the workspace'
    Eval 'document.querySelector(".pen-account-menu form").requestSubmit()'|Out-Null;WaitPage
    OpenPage '/Account/Login'
    $emailJson=$env:WORKFLOW_BROWSER_LOGIN_EMAIL|ConvertTo-Json -Compress
    Eval ('document.querySelector("#Email").value='+$emailJson+';document.querySelector("#Password").value='+$passwordJson+';document.querySelector(".pen-auth form").requestSubmit()')|Out-Null
    WaitPage
    Check (Eval 'location.pathname==="/Tasks"&&!!document.querySelector(".pen-account-menu")') 'signing in from the public flow opens the workspace'
    OpenPage '/Account/Profile'
    Check ((Eval 'document.querySelector(".pen-profile-avatar img")?.getAttribute("src")') -eq $replacementPhoto) 'replaced portrait persists after signing out and back in'
    Eval 'document.querySelector(".pen-photo-remove").requestSubmit()'|Out-Null;WaitPage
    Check (Eval '!document.querySelector(".pen-profile-avatar img,.pen-account-menu img,.pen-photo-remove")&&document.querySelector(".pen-profile-avatar").textContent.trim()==="BQ"') 'removing the photo restores initials in the profile and menu'
    Write-Output "$script:passed identity browser checks passed. Owned HTTPS account fixtures only."
} finally {$socket.Dispose();if(-not $chrome.HasExited){Stop-Process -Id $chrome.Id -ErrorAction SilentlyContinue};foreach($file in $photos){Remove-Item -LiteralPath $file -ErrorAction SilentlyContinue}}
