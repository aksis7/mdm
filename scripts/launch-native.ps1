$ErrorActionPreference = 'Stop'
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
if ($env:PDM_BACKEND_DIR) {
    $Backend = $env:PDM_BACKEND_DIR
} elseif (Test-Path (Join-Path $Here 'backend\docker-compose.yml')) {
    $Backend = Join-Path $Here 'backend'
} elseif (Test-Path (Join-Path $Here '..\docker-compose.yml')) {
    $Backend = (Resolve-Path (Join-Path $Here '..')).Path
} else {
    throw 'Mini-PDM: Backend sources were not found. Set PDM_BACKEND_DIR to the backend directory.'
}
$Port = if ($env:PDM_API_PORT) { $env:PDM_API_PORT } else { '5000' }
$ApiUrl = "http://127.0.0.1:$Port"
$Project = if ($env:PDM_COMPOSE_PROJECT_NAME) { $env:PDM_COMPOSE_PROJECT_NAME } else { 'cad' }
$Timeout = if ($env:PDM_STARTUP_TIMEOUT_SECONDS) { [int]$env:PDM_STARTUP_TIMEOUT_SECONDS } else { 180 }
$env:PDM_API_PORT = $Port
$NativeBuildDirectory = $null

function Fail([string]$Message) { throw "Mini-PDM: $Message" }
function Test-DockerReady {
    try { docker info *> $null; return ($LASTEXITCODE -eq 0) } catch { return $false }
}
try {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { Fail 'Docker is required. Install Docker Desktop, then retry.' }
    if (-not (Test-DockerReady)) {
        $DockerDesktop = Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
        if (Test-Path $DockerDesktop) { Start-Process $DockerDesktop }
        $Started = $false
        for ($i = 0; $i -lt 45; $i++) { Start-Sleep 2; if (Test-DockerReady) { $Started = $true; break } }
        if (-not $Started) { Fail 'Docker daemon did not start. Open Docker Desktop and retry.' }
    }
    if ($env:PDM_BUILD_DESKTOP_IN_DOCKER -eq '1') {
        $Architecture = $env:PROCESSOR_ARCHITEW6432
        if (-not $Architecture) { $Architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() }
        if (-not $Architecture -or $Architecture -eq 'Unknown') { $Architecture = $env:PROCESSOR_ARCHITECTURE }
        switch ($Architecture.ToUpperInvariant()) {
            { $_ -in @('AMD64', 'X64') } { $Rid = 'win-x64'; break }
            { $_ -eq 'ARM64' } { $Rid = 'win-arm64'; break }
            default { Fail "Unsupported Windows architecture '$Architecture'. Supported: x64 and arm64." }
        }
        docker buildx version *> $null
        if ($LASTEXITCODE -ne 0) { Fail 'Docker Buildx is required to build the native Desktop without installing the .NET SDK.' }
        $NativeRoot = Join-Path $Backend "artifacts\native\$Rid"
        New-Item -ItemType Directory -Path $NativeRoot -Force | Out-Null
        $NativeBuildDirectory = Join-Path $NativeRoot ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $NativeBuildDirectory -Force | Out-Null
        docker buildx build --target native-export --build-arg "PDM_DESKTOP_RID=$Rid" --output "type=local,dest=$NativeBuildDirectory" $Backend
        if ($LASTEXITCODE -ne 0) { Fail 'Docker could not build the native Desktop. Existing backend services were not stopped.' }
        $NativeExecutable = Join-Path $NativeBuildDirectory 'MiniPdm.Desktop.exe'
        if (-not (Test-Path $NativeExecutable)) { Fail "Docker export did not produce the native executable for $Rid." }
        $env:PDM_DESKTOP_EXECUTABLE = $NativeExecutable
    }
    $Compose = @('--project-directory', $Backend, '-p', $Project, '-f', (Join-Path $Backend 'docker-compose.yml'), '-f', (Join-Path $Backend 'compose.native.yml'))
    if (Test-Path (Join-Path $Backend '.env')) { $Compose += @('--env-file', (Join-Path $Backend '.env')) }
    docker compose @Compose up -d --build api
    if ($LASTEXITCODE -ne 0) { Fail 'Could not start the API and its database/migration dependencies.' }
    $Ready = $false
    $Clock = [Diagnostics.Stopwatch]::StartNew()
    for ($i = 0; $i -lt [math]::Ceiling($Timeout / 2); $i++) {
        if ($Clock.Elapsed.TotalSeconds -ge $Timeout) { break }
        try { Invoke-WebRequest -UseBasicParsing -Uri "$ApiUrl/health" -TimeoutSec 2 | Out-Null; $Ready = $true; break } catch { Start-Sleep 2 }
    }
    if (-not $Ready) { Fail "API did not become healthy within $Timeout seconds at $ApiUrl/health." }
    Remove-Item Env:PDM_BROWSER_PICKER -ErrorAction SilentlyContinue
    $env:PDM_API_BASE_URL = $ApiUrl
    $App = if ($env:PDM_DESKTOP_EXECUTABLE) { $env:PDM_DESKTOP_EXECUTABLE } else { Join-Path $Here 'desktop\MiniPdm.Desktop.exe' }
    if (Test-Path $App) { $Process = Start-Process -FilePath $App -Wait -PassThru; exit $Process.ExitCode }
    Fail 'Desktop application executable was not found in this distribution.'
} catch {
    [Console]::Error.WriteLine("Mini-PDM: $_")
    exit 1
} finally {
    if ($NativeBuildDirectory -and (Test-Path $NativeBuildDirectory)) {
        Remove-Item -LiteralPath $NativeBuildDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
