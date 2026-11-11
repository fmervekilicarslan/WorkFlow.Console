# Windows / Docker Desktop runner. No User Secrets, live writes or automatic database deletion.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot
$statePath = Join-Path $projectRoot '.local-checkpoints/isolated-tests.clixml'
$testDll = Join-Path $projectRoot 'Tests/WorkFlow.Stage1Checks/bin/Release/net10.0/WorkFlow.Stage1Checks.dll'
if (-not (Test-Path -LiteralPath $testDll)) { throw 'Build Release tests before provisioning.' }
$portMapping = & docker port workflow-postgres 5432/tcp
if ($LASTEXITCODE -ne 0 -or $portMapping -ne '127.0.0.1:5433') { throw 'Unexpected PostgreSQL endpoint; refusing to provision.' }

function Invoke-AdminSql([string]$Database, [string]$Sql) {
    $previousErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $sqlOutput = @($Sql | & docker exec -i workflow-postgres psql -U workflow_user -d $Database -X -q -A -t -v ON_ERROR_STOP=1 2>&1)
        $sqlExitCode = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousErrorAction }
    if ($sqlExitCode -ne 0) { throw 'PostgreSQL administration check failed; SQL details suppressed.' }
    return ($sqlOutput -join "`n").Trim()
}

function Get-LiveFingerprint {
    Invoke-AdminSql 'workflow_db' @'
BEGIN READ ONLY;
SELECT md5(jsonb_build_object(
 'tasks', (SELECT jsonb_agg(to_jsonb(t) ORDER BY "Id") FROM "Tasks" t),
 'migrations', (SELECT jsonb_agg(to_jsonb(m) ORDER BY "MigrationId") FROM "__EFMigrationsHistory" m),
 'sequence', (SELECT jsonb_build_object('last_value', last_value, 'is_called', is_called, 'log_cnt', log_cnt) FROM "TaskCodeSequence"),
 'database', (SELECT jsonb_build_object('owner', datdba, 'acl', datacl) FROM pg_database WHERE datname = current_database()),
 'schema_acl', (SELECT jsonb_agg(jsonb_build_object('name', nspname, 'owner', nspowner, 'acl', nspacl) ORDER BY nspname) FROM pg_namespace WHERE nspname = 'public'),
 'table_acl', (SELECT jsonb_agg(jsonb_build_object('name', relname, 'owner', relowner, 'acl', relacl) ORDER BY relname) FROM pg_class WHERE relnamespace = 'public'::regnamespace)
)::text);
COMMIT;
'@
}

$before = Get-LiveFingerprint
if ($before -notmatch '^[a-f0-9]{32}$') { throw 'Cannot establish live read-only baseline.' }
$originalConnection = [Environment]::GetEnvironmentVariable('WORKFLOW_TEST_CONNECTION', 'Process')
try {
    if (Test-Path -LiteralPath $statePath) {
        $state = Import-Clixml -LiteralPath $statePath
        $connectionValue = [System.Net.NetworkCredential]::new('', $state.Connection).Password
        $testName = $state.Name
    } else {
        $testName = 'workflow_test_' + [Guid]::NewGuid().ToString('N')
        $randomBytes = New-Object byte[] 32
        $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
        try { $rng.GetBytes($randomBytes) } finally { $rng.Dispose() }
        $testPassword = [Convert]::ToBase64String($randomBytes)
        $connectionValue = "Host=127.0.0.1;Port=5433;Database=$testName;Username=$testName;Password=$testPassword"
        $admin = Invoke-AdminSql 'postgres' 'SELECT rolsuper AND rolcreaterole AND rolcreatedb FROM pg_roles WHERE rolname = current_user;'
        if ($admin -ne 't') { throw 'Existing administrator cannot provision safely; no privileges will be expanded.' }
        $null = Invoke-AdminSql 'postgres' "CREATE ROLE $testName LOGIN PASSWORD '$testPassword' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS;"
        New-Item -ItemType Directory -Path (Split-Path -Parent $statePath) -Force | Out-Null
        [pscustomobject]@{ Name = $testName; Connection = (ConvertTo-SecureString $connectionValue -AsPlainText -Force) } | Export-Clixml -LiteralPath $statePath
        $null = Invoke-AdminSql 'postgres' "CREATE DATABASE $testName OWNER $testName TEMPLATE template0;"
        $null = Invoke-AdminSql 'postgres' "REVOKE ALL ON DATABASE $testName FROM PUBLIC;"
        Write-Output 'PASS: Unique test database and non-administrative role created; encrypted credentials kept only in ignored local checkpoint.'
    }
    if ($testName -notmatch '^workflow_test_[a-f0-9]{32}$') { throw 'Invalid stored test identity.' }
    $liveAccess = Invoke-AdminSql 'workflow_db' @"
BEGIN READ ONLY;
SELECT NOT has_database_privilege('$testName', current_database(), 'CREATE')
 AND NOT has_schema_privilege('$testName', 'public', 'CREATE')
 AND NOT EXISTS (SELECT 1 FROM pg_class WHERE relnamespace = 'public'::regnamespace
  AND CASE WHEN relkind IN ('r','p','v','m','f') THEN has_table_privilege('$testName', oid, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER') ELSE false END)
 AND NOT EXISTS (SELECT 1 FROM pg_class WHERE relnamespace = 'public'::regnamespace
  AND CASE WHEN relkind = 'S' THEN has_sequence_privilege('$testName', oid, 'USAGE,SELECT,UPDATE') ELSE false END);
COMMIT;
"@
    if ($liveAccess -ne 't') { throw 'Test role has access to live objects; stopping without modifying live permissions.' }
    Write-Output 'PASS: Test role has no live table, sequence or schema/database creation privileges.'
    [Environment]::SetEnvironmentVariable('WORKFLOW_TEST_CONNECTION', $connectionValue, 'Process')
    foreach ($mode in @('--prepare-test-database', '--database', '--stage2')) {
        & dotnet $testDll $mode
        if ($LASTEXITCODE -ne 0) { throw "Isolated test mode failed: $mode" }
    }
} finally {
    [Environment]::SetEnvironmentVariable('WORKFLOW_TEST_CONNECTION', $originalConnection, 'Process')
    $after = Get-LiveFingerprint
    if ($after -ne $before) { throw 'Live baseline changed during the run; investigate concurrent activity.' }
    Write-Output 'PASS: Live tasks, migration history, sequence, owners and permissions unchanged.'
    Write-Output 'Test database and dedicated role retained. Removal requires separate approval.'
}
