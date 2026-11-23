[CmdletBinding()]
param([ValidateRange(1024, 65535)][int]$Port = 5001)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$url = "https://localhost:$Port"
$checkpoint = Join-Path $root '.local-checkpoints'
$statePath = Join-Path $checkpoint 'auth-demo.clixml'
$output = Join-Path $checkpoint "presentation-release-$Port"
$dll = Join-Path $output 'WorkFlow.Mvc.dll'
$localDotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet.exe).Source }

# Fail closed before starting the application. Never use the normal project connection.
try {
    $demo = Import-Clixml -LiteralPath $statePath
    if ($demo.Name -notmatch '^workflow_demo_[a-f0-9]{32}$' -or $demo.Connection -isnot [securestring]) { throw 'Invalid demo state' }
    $connection = [Net.NetworkCredential]::new('', $demo.Connection).Password
    $settings = [Data.Common.DbConnectionStringBuilder]::new()
    $settings.set_ConnectionString($connection)
    $allowedKeys = @('Host', 'Port', 'Database', 'Username', 'Password', 'SSL Mode', 'Timeout', 'Command Timeout')
    if (@($settings.Keys | Where-Object { $_ -notin $allowedKeys }).Count -or
        $settings['Host'] -notin @('127.0.0.1', 'localhost') -or $settings['Port'] -ne '5433' -or
        $settings['Database'] -ne $demo.Name -or $settings['Username'] -ne $demo.Name -or !$settings['Password']) { throw 'Invalid demo target' }
} catch {
    throw 'Bu Windows hesabıyla açılabilen, izole demo hedefini gösteren .local-checkpoints/auth-demo.clixml gerekli.'
}

$curl = (Get-Command curl.exe).Source
function Test-DemoPort {
    foreach ($address in @([Net.IPAddress]::Loopback, [Net.IPAddress]::IPv6Loopback)) {
        $socket = [Net.Sockets.TcpClient]::new($address.AddressFamily)
        try {
            if ($socket.ConnectAsync($address, $Port).Wait(300) -and $socket.Connected) { return $true }
        } catch {
            # A refused connection means there is no listener on this address.
        } finally { $socket.Dispose() }
    }
    return $false
}
if (@([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() |
        Where-Object Port -eq $Port).Count -or (Test-DemoPort)) {
    $expectedArguments = '"' + $dll + '" --urls ' + $url
    $running = @(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -eq $dotnet -and $_.CommandLine.TrimEnd().EndsWith($expectedArguments, [StringComparison]::OrdinalIgnoreCase) })
    if ($running.Count -eq 1 -and
        (& $curl --noproxy '*' --silent --max-time 2 --output NUL --write-out '%{http_code}' "$url/Account/Login") -eq '200') {
        Write-Host "WorkFlow demo zaten hazır: $url" -ForegroundColor Green
        return
    }
    throw "$Port portu kullanımda. Yalnızca kendi açık demo terminalinizi Ctrl+C ile kapatın veya -Port 5002 kullanın."
}

$cert = Get-ChildItem Cert:\CurrentUser\My -SSLServerAuthentication |
    Where-Object {
        $_.Subject -eq 'CN=localhost' -and $_.HasPrivateKey -and
        $_.NotBefore -le (Get-Date) -and $_.NotAfter -gt (Get-Date) -and
        ($_.Extensions | Where-Object { $_.Oid.Value -eq '1.3.6.1.4.1.311.84.1.1' }) -and
        (Test-Path -LiteralPath ('Cert:\CurrentUser\Root\' + $_.Thumbprint))
    } | Sort-Object NotAfter -Descending | Select-Object -First 1
if (!$cert) { throw 'Geçerli ve güvenilir .NET localhost geliştirme sertifikası gerekli.' }

try {
    if (!(Get-Command docker.exe -ErrorAction SilentlyContinue)) { throw 'Docker unavailable' }
    $published = @(& docker port workflow-postgres 5432/tcp 2>$null)
    if ($LASTEXITCODE -ne 0 -or '127.0.0.1:5433' -notin $published) { throw 'Unexpected mapping' }
    $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$settings['Password']))
    $identity = @($encoded, 'SELECT current_database(),current_user;') |
        docker exec -i workflow-postgres sh -c 'IFS= read -r encoded; PGPASSWORD=$(printf %s $encoded | tr -d ''\015'' | base64 -d); export PGPASSWORD; export PGOPTIONS=-cdefault_transaction_read_only=on; exec psql -X -w -At -h 127.0.0.1 -U $1 -d $1' sh $demo.Name 2>$null
    if ($LASTEXITCODE -ne 0 -or ($identity -join '').Trim() -ne ($demo.Name + '|' + $demo.Name)) { throw 'Unexpected identity' }
} catch {
    throw 'İzole demo veritabanı doğrulanamadı. Docker Desktop ve mevcut workflow-postgres konteynerinin açık olduğunu kontrol edin.'
} finally {
    $encoded = $null
    $settings.Clear()
}

& $dotnet build (Join-Path $root 'WorkFlow.Mvc.csproj') -c Release -o $output --tl:off
if ($LASTEXITCODE -ne 0) { throw 'Release derlemesi başarısız.' }

$certificatePath = Join-Path ([IO.Path]::GetTempPath()) ('workflow-demo-' + [Guid]::NewGuid().ToString('N') + '.pfx')
$passwordBytes = New-Object byte[] 24
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($passwordBytes) } finally { $rng.Dispose() }
$certificatePassword = ([BitConverter]::ToString($passwordBytes)).Replace('-', '')
$process = $null
try {
    Export-PfxCertificate -Cert $cert -FilePath $certificatePath -ChainOption EndEntityCertOnly `
        -Password (ConvertTo-SecureString $certificatePassword -AsPlainText -Force) | Out-Null

    $start = [Diagnostics.ProcessStartInfo]::new($dotnet)
    $start.WorkingDirectory = $root
    $start.Arguments = '"' + $dll + '" --urls ' + $url
    $start.UseShellExecute = $false
    $start.EnvironmentVariables['ConnectionStrings__WorkFlowDb'] = $connection
    $start.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = 'Production'
    $start.EnvironmentVariables['DOTNET_ENVIRONMENT'] = 'Production'
    $start.EnvironmentVariables['Kestrel__Certificates__Default__Path'] = $certificatePath
    $start.EnvironmentVariables['Kestrel__Certificates__Default__Password'] = $certificatePassword
    $start.EnvironmentVariables['Logging__LogLevel__Default'] = 'Warning'
    $start.EnvironmentVariables['Logging__LogLevel__Microsoft.Hosting.Lifetime'] = 'Information'
    $start.EnvironmentVariables['Logging__LogLevel__Microsoft.EntityFrameworkCore'] = 'None'
    $start.EnvironmentVariables['Logging__LogLevel__Npgsql'] = 'None'
    $process = [Diagnostics.Process]::Start($start)
    $start.EnvironmentVariables.Clear()
    $connection = $null
    $certificatePassword = $null

    $ready = $false
    $deadline = (Get-Date).AddSeconds(20)
    while (!$process.HasExited -and (Get-Date) -lt $deadline) {
        $status = & $curl --noproxy '*' --silent --max-time 2 --output NUL --write-out '%{http_code}' "$url/Account/Login"
        if ($LASTEXITCODE -eq 0 -and $status -eq '200') { $ready = $true; break }
        Start-Sleep -Milliseconds 300
    }
    if (!$ready) { throw 'Demo HTTPS giriş sayfası hazır olmadı.' }
    Write-Host ""
    Write-Host "WorkFlow demo hazır: $url" -ForegroundColor Green
    Write-Host 'Durdurmak için bu terminalde Ctrl+C kullanın.'
    while (!$process.WaitForExit(500)) { }
    if ($process.ExitCode -ne 0) { throw "Demo süreci hata koduyla kapandı: $($process.ExitCode)" }
} finally {
    if ($process) {
        if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
    if (Test-Path -LiteralPath $certificatePath) { Remove-Item -LiteralPath $certificatePath }
}
