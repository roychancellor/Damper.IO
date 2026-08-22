$ErrorActionPreference = "Stop"

# ============================================================================
# Damper PostgreSQL Integration Tests
# ============================================================================
#
# Tests a completely disposable PostgreSQL 18 instance using:
#
#   docker-compose-test.yml
#   postgres-init/01-roles.sh
#   postgres-init/02-schema.sh
#
# The test database is completely isolated from the normal Damper database.
#
# Test credentials are generated here and are NEVER written to source control.
# ============================================================================

$ScriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectRoot = Resolve-Path (Join-Path $ScriptDirectory "..")

$ComposeFile = Join-Path $ProjectRoot "docker-compose-test.yml"
$PostgresInit = Join-Path $ProjectRoot "postgres-init"

$ContainerName = "damper-postgres-test"
$ComposeProject = "damper-postgres-integration-test"

$Passed = 0
$Failed = 0

# Test-only credentials.
# These are disposable and never belong in production.
$Superuser = "damper-superuser"
$SuperuserPassword = "damper-superuser-test-password"

$AdminUser = "damper-admin"
$AdminPassword = "damper-admin-test-password"

$RuntimeUser = "damper-runtime"
$RuntimePassword = "damper-runtime-test-password"

# Temporary .env file.
$TempEnv = Join-Path $env:TEMP "damper-postgres-integration-test.env"

# ============================================================================
# Helpers
# ============================================================================

function Write-Header {
    param (
        [string]$Message
    )

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host $Message -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan
}

function Pass {
    param (
        [string]$Message
    )

    $script:Passed++

    Write-Host "[PASS] " -ForegroundColor Green -NoNewline
    Write-Host $Message
}

function Fail {
    param (
        [string]$Message
    )

    $script:Failed++

    Write-Host "[FAIL] " -ForegroundColor Red -NoNewline
    Write-Host $Message
}

function Assert-Equal {
    param (
        [string]$Name,
        [string]$Actual,
        [string]$Expected
    )

    if ($Actual -eq $Expected) {
        Pass $Name
    }
    else {
        Fail "$Name - expected '$Expected', got '$Actual'"
    }
}

function Invoke-Psql {
    param (
        [string]$User,
        [string]$Password,
        [string]$Sql
    )

    $result = & docker exec `
        -e "PGPASSWORD=$Password" `
        $ContainerName `
        psql `
        -U $User `
        -d damper `
        -At `
        -v ON_ERROR_STOP=1 `
        -c $Sql 2>&1

    if ($LASTEXITCODE -ne 0) {
        throw "psql failed:`n$($result -join "`n")"
    }

    return ($result -join "`n").Trim()
}

function Invoke-PsqlExpectedFailure {
    param (
        [string]$User,
        [string]$Password,
        [string]$Sql,
        [string]$TestName
    )

    $result = & docker exec `
        -e "PGPASSWORD=$Password" `
        $ContainerName `
        psql `
        -U $User `
        -d damper `
        -v ON_ERROR_STOP=1 `
        -c $Sql 2>&1

    if ($LASTEXITCODE -ne 0) {
        Pass $TestName
    }
    else {
        Fail "$TestName - command unexpectedly succeeded"
    }
}

function Invoke-Compose {
    param (
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    $previousErrorActionPreference = $ErrorActionPreference

    try {
        $ErrorActionPreference = "Continue"

        & docker compose `
            --project-name $ComposeProject `
            --env-file $TempEnv `
            --file $ComposeFile `
            @Arguments

        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($exitCode -ne 0) {
        throw "docker compose failed with exit code $exitCode"
    }
}

# ============================================================================
# Validate files
# ============================================================================

Write-Header "Damper PostgreSQL Integration Tests"

if (-not (Test-Path $ComposeFile)) {
    throw "Compose file not found: $ComposeFile"
}

if (-not (Test-Path $PostgresInit)) {
    throw "postgres-init directory not found: $PostgresInit"
}

Write-Host "Project root : $ProjectRoot"
Write-Host "Compose file : $ComposeFile"
Write-Host "Init scripts : $PostgresInit"
Write-Host "Container    : $ContainerName"
Write-Host "Compose proj : $ComposeProject"

# ============================================================================
# Create temporary test environment
# ============================================================================

Write-Header "Creating Disposable Test Environment"

@"
POSTGRES_DB=damper
POSTGRES_USER=$Superuser
POSTGRES_PASSWORD=$SuperuserPassword

DAMPER_ADMIN_USER=$AdminUser
DAMPER_ADMIN_PASSWORD=$AdminPassword

DAMPER_RUNTIME_USER=$RuntimeUser
DAMPER_RUNTIME_PASSWORD=$RuntimePassword
"@ | Set-Content -Path $TempEnv -Encoding ASCII

Write-Host "Temporary test environment created:"
Write-Host $TempEnv

# ============================================================================
# Cleanup any previous test instance
# ============================================================================

try {
    Invoke-Compose down --volumes --remove-orphans 2>&1 | Out-Null
}
catch {
    # Nothing to clean up.
}

# ============================================================================
# Start PostgreSQL
# ============================================================================

Write-Header "Starting Disposable PostgreSQL"

Invoke-Compose up --detach

# ============================================================================
# Wait for PostgreSQL
# ============================================================================

Write-Host "Waiting for PostgreSQL..."

$Ready = $false

for ($i = 1; $i -le 30; $i++) {

    $result = & docker exec `
        $ContainerName `
        pg_isready `
        -U $Superuser `
        -d damper 2>&1

    if ($LASTEXITCODE -eq 0) {
        $Ready = $true
        break
    }

    Start-Sleep -Seconds 1
}

if (-not $Ready) {

    Write-Host ""
    Write-Host "PostgreSQL failed to become ready." -ForegroundColor Red
    Write-Host ""
    Write-Host "Container logs:" -ForegroundColor Yellow

    & docker logs $ContainerName

    throw "PostgreSQL startup failed."
}

Pass "PostgreSQL became ready"

# ============================================================================
# ROLE TESTS
# ============================================================================

Write-Header "Role Tests"

$roleChecks = @(
    @{
        Name = "damper-superuser"
        Superuser = "true"
        CreateRole = "true"
        CreateDb = "true"
        CanLogin = "true"
    },
    @{
        Name = "damper-admin"
        Superuser = "false"
        CreateRole = "false"
        CreateDb = "false"
        CanLogin = "true"
    },
    @{
        Name = "damper-runtime"
        Superuser = "false"
        CreateRole = "false"
        CreateDb = "false"
        CanLogin = "true"
    }
)

foreach ($role in $roleChecks) {

    $sql = @"
SELECT
    CASE
        WHEN rolname = '$($role.Name)'
         AND rolsuper = $($role.Superuser)
         AND rolcreaterole = $($role.CreateRole)
         AND rolcreatedb = $($role.CreateDb)
         AND rolcanlogin = $($role.CanLogin)
        THEN 'true'
        ELSE 'false'
    END
FROM pg_roles
WHERE rolname = '$($role.Name)';
"@

    $result = Invoke-Psql `
        $Superuser `
        $SuperuserPassword `
        $sql

    if ($result -eq "true") {
        Pass "Role configuration: $($role.Name)"
    }
    elseif ([string]::IsNullOrWhiteSpace($result)) {
        Fail "Role does not exist: $($role.Name)"
    }
    else {
        Fail "Incorrect role configuration: $($role.Name)"
    }
}

# ============================================================================
# DATABASE OWNERSHIP
# ============================================================================

Write-Header "Database Ownership"

$dbOwner = Invoke-Psql `
    $Superuser `
    $SuperuserPassword `
    "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = 'damper';"

Assert-Equal "Database owner" $dbOwner $AdminUser

# ============================================================================
# SCHEMA OWNERSHIP
# ============================================================================

Write-Header "Schema Ownership"

$schemaOwner = Invoke-Psql `
    $Superuser `
    $SuperuserPassword `
    "SELECT pg_get_userbyid(nspowner) FROM pg_namespace WHERE nspname = 'damper';"

Assert-Equal "Schema owner" $schemaOwner $AdminUser

# ============================================================================
# TABLE OWNERSHIP
# ============================================================================

Write-Header "Table Ownership"

$tableOwners = Invoke-Psql `
    $Superuser `
    $SuperuserPassword `
    @"
SELECT tablename || '|' || tableowner
FROM pg_tables
WHERE schemaname = 'damper'
ORDER BY tablename;
"@

$tableLines = $tableOwners -split "`n"

$expectedTables = @(
    "integration|$AdminUser",
    "schema_version|$AdminUser"
)

foreach ($expected in $expectedTables) {

    if ($tableLines -contains $expected) {
        Pass "Table ownership: $expected"
    }
    else {
        Fail "Missing or incorrect table ownership: $expected"
    }
}

# ============================================================================
# CONSTRAINTS
# ============================================================================

Write-Header "Constraint Tests"

$constraints = Invoke-Psql `
    $Superuser `
    $SuperuserPassword `
    @"
SELECT conname || '|' || contype::text
FROM pg_constraint
WHERE conrelid = 'damper.integration'::regclass
ORDER BY conname;
"@

$constraintLines = $constraints -split "`n"

if ($constraintLines -contains "pk_integration|p") {
    Pass "Primary key exists"
}
else {
    Fail "Primary key missing"
}

if ($constraintLines -contains "uq_integration_api_key_hash|u") {
    Pass "API key hash unique constraint exists"
}
else {
    Fail "API key hash unique constraint missing"
}

# ============================================================================
# RUNTIME LOGIN
# ============================================================================

Write-Header "Runtime Login"

$identity = Invoke-Psql `
    $RuntimeUser `
    $RuntimePassword `
    "SELECT current_database() || '|' || current_user || '|' || session_user;"

Assert-Equal `
    "Runtime identity" `
    $identity `
    "damper|$RuntimeUser|$RuntimeUser"

# ============================================================================
# RUNTIME SCHEMA PRIVILEGES
# ============================================================================

Write-Header "Runtime Schema Privileges"

$schemaPrivileges = Invoke-Psql `
    $Superuser `
    $SuperuserPassword `
    @"
SELECT
    has_schema_privilege('$RuntimeUser', 'damper', 'USAGE') ||
    '|' ||
    has_schema_privilege('$RuntimeUser', 'damper', 'CREATE');
"@

Assert-Equal `
    "Runtime schema privileges" `
    $schemaPrivileges `
    "true|false"

# ============================================================================
# RUNTIME TABLE PRIVILEGES
# ============================================================================

Write-Header "Runtime Table Privileges"

$tablePrivileges = Invoke-Psql `
    $Superuser `
    $SuperuserPassword `
    @"
SELECT table_name || '|' || privilege_type
FROM information_schema.role_table_grants
WHERE grantee = '$RuntimeUser'
  AND table_schema = 'damper'
ORDER BY table_name, privilege_type;
"@

$tablePrivilegeLines = $tablePrivileges -split "`n"

$expectedPrivileges = @(
    "integration|DELETE",
    "integration|INSERT",
    "integration|SELECT",
    "integration|UPDATE",
    "schema_version|DELETE",
    "schema_version|INSERT",
    "schema_version|SELECT",
    "schema_version|UPDATE"
)

foreach ($expected in $expectedPrivileges) {

    if ($tablePrivilegeLines -contains $expected) {
        Pass "Runtime privilege: $expected"
    }
    else {
        Fail "Missing runtime privilege: $expected"
    }
}

# ============================================================================
# RUNTIME CRUD
# ============================================================================

Write-Header "Runtime CRUD"

$insertSql = @"
INSERT INTO damper.integration
(
    name,
    api_key_hash,
    configuration,
    created_at,
    modified_at
)
VALUES
(
    'Integration Test',
    decode(repeat('00', 32), 'hex'),
    '{}'::jsonb,
    now(),
    now()
)
RETURNING id
"@

$insertedId = Invoke-Psql `
    $RuntimeUser `
    $RuntimePassword `
    $insertSql

if ([string]::IsNullOrWhiteSpace($insertedId)) {
    Fail "Runtime INSERT did not return an ID"
    throw "Cannot continue CRUD tests without inserted ID."
}

if ($insertedId -match '^\d+$') {
    Pass "Runtime INSERT"
}
else {
    Fail "Runtime INSERT returned unexpected value: '$insertedId'"
    throw "Cannot continue CRUD tests without valid inserted ID."
}

$selectedName = Invoke-Psql `
    $RuntimeUser `
    $RuntimePassword `
    "SELECT name FROM damper.integration WHERE id = $insertedId;"

Assert-Equal `
    "Runtime SELECT" `
    $selectedName `
    "Integration Test"

$updateResult = Invoke-Psql `
    $RuntimeUser `
    $RuntimePassword `
    "UPDATE damper.integration SET name = 'Integration Test Updated', modified_at = now() WHERE id = $insertedId;"

Assert-Equal `
    "Runtime UPDATE" `
    $updateResult `
    "UPDATE 1"

$updatedName = Invoke-Psql `
    $RuntimeUser `
    $RuntimePassword `
    "SELECT name FROM damper.integration WHERE id = $insertedId;"

Assert-Equal `
    "Runtime SELECT after UPDATE" `
    $updatedName `
    "Integration Test Updated"

$deleteResult = Invoke-Psql `
    $RuntimeUser `
    $RuntimePassword `
    "DELETE FROM damper.integration WHERE id = $insertedId;"

Assert-Equal `
    "Runtime DELETE" `
    $deleteResult `
    "DELETE 1"

$remaining = Invoke-Psql `
    $RuntimeUser `
    $RuntimePassword `
    "SELECT COUNT(*) FROM damper.integration WHERE id = $insertedId;"

Assert-Equal `
    "Runtime DELETE verification" `
    $remaining `
    "0"

# ============================================================================
# SECURITY BOUNDARY
# ============================================================================

Write-Header "Runtime Security Boundary"

Invoke-PsqlExpectedFailure `
    $RuntimeUser `
    $RuntimePassword `
    "CREATE TABLE damper.runtime_should_fail (id bigint);" `
    "Runtime cannot CREATE tables"

Invoke-PsqlExpectedFailure `
    $RuntimeUser `
    $RuntimePassword `
    "ALTER TABLE damper.integration ADD COLUMN runtime_should_fail text;" `
    "Runtime cannot ALTER tables"

Invoke-PsqlExpectedFailure `
    $RuntimeUser `
    $RuntimePassword `
    "DROP TABLE damper.integration;" `
    "Runtime cannot DROP tables"

# ============================================================================
# DEFAULT PRIVILEGES
# ============================================================================

Write-Header "Default Privileges"

$defaultPrivileges = Invoke-Psql `
    $Superuser `
    $SuperuserPassword `
    @"
SELECT defaclobjtype || '|' || array_to_string(defaclacl, ',')
FROM pg_default_acl
WHERE defaclrole = '$AdminUser'::regrole
  AND defaclnamespace = 'damper'::regnamespace
ORDER BY defaclobjtype;
"@

if ($defaultPrivileges -match "r\|.*$RuntimeUser=.*arwd") {
    Pass "Default table privileges grant runtime CRUD"
}
else {
    Fail "Default table privileges are incorrect"
}

if ($defaultPrivileges -match "S\|.*$RuntimeUser=.*rU") {
    Pass "Default sequence privileges grant runtime USAGE/SELECT"
}
else {
    Fail "Default sequence privileges are incorrect"
}

# ============================================================================
# SUMMARY
# ============================================================================

Write-Header "Test Summary"

Write-Host ""
Write-Host "Passed: $Passed" -ForegroundColor Green
Write-Host "Failed: $Failed" -ForegroundColor Red
Write-Host ""

if ($Failed -eq 0) {
    Write-Host "PostgreSQL integration tests: PASS" -ForegroundColor Green
}
else {
    Write-Host "PostgreSQL integration tests: FAIL" -ForegroundColor Red
}

# ============================================================================
# CLEANUP
# ============================================================================

Write-Header "Cleaning Up Disposable PostgreSQL"

try {
    Invoke-Compose down --volumes --remove-orphans 2>&1 | Out-Null
    Write-Host "Disposable PostgreSQL instance removed." -ForegroundColor Green
}
catch {
    Write-Host "WARNING: Test cleanup failed." -ForegroundColor Yellow
}

Remove-Item $TempEnv -Force -ErrorAction SilentlyContinue

if ($Failed -ne 0) {
    exit 1
}

exit 0