# Embeddable Python (win-amd64) + pip + shazamio. Invoked from PomogatorLauncher.csproj BeforeBuild.
# ASCII only: avoids parse errors under non-UTF8 default code pages.
param(
    [Parameter(Mandatory = $true)]
    [string] $EmbedRoot,
    [string] $PythonFullVersion = "3.12.8"
)

$ErrorActionPreference = "Stop"
$Requirements = Join-Path $PSScriptRoot "requirements.txt"
if (-not (Test-Path $Requirements)) {
    throw "requirements.txt not found next to script: $Requirements"
}

$pythonExe = Join-Path $EmbedRoot "python.exe"
$shazamMarker = Join-Path $EmbedRoot "Lib\site-packages\shazamio\__init__.py"
if ((Test-Path $pythonExe) -and (Test-Path $shazamMarker)) {
    Write-Host "[Shazam embed] OK (cached): $EmbedRoot"
    exit 0
}

Write-Host "[Shazam embed] Preparing Python $PythonFullVersion at $EmbedRoot (internet required)..."

$zipName = "python-$PythonFullVersion-embed-amd64.zip"
$zipUrl = "https://www.python.org/ftp/python/$PythonFullVersion/$zipName"
$zipPath = Join-Path ([System.IO.Path]::GetTempPath()) "pomogator-$zipName"

if (Test-Path $EmbedRoot) {
    Remove-Item -LiteralPath $EmbedRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $EmbedRoot | Out-Null

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -UseBasicParsing
}
catch {
    throw "Download failed: $zipUrl - $($_.Exception.Message)"
}

Expand-Archive -LiteralPath $zipPath -DestinationPath $EmbedRoot -Force

$pth = Get-ChildItem -LiteralPath $EmbedRoot -Filter "python*._pth" -File | Select-Object -First 1
if (-not $pth) {
    throw "No python*._pth in embed zip"
}
$pthLines = Get-Content -LiteralPath $pth.FullName
$out = foreach ($line in $pthLines) {
    if ($line -match '^\s*#\s*import\s+site\s*$') {
        "import site"
    }
    else {
        $line
    }
}
if (@($out) -notcontains "import site") {
    $out = @($out) + "import site"
}
Set-Content -LiteralPath $pth.FullName -Value $out -Encoding ascii

$getPip = Join-Path ([System.IO.Path]::GetTempPath()) "get-pip-pomogator.py"
Invoke-WebRequest -Uri "https://bootstrap.pypa.io/get-pip.py" -OutFile $getPip -UseBasicParsing

function Invoke-Python {
    param([string[]] $Arguments)
    $p = Start-Process -FilePath $pythonExe -ArgumentList $Arguments -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0) {
        throw "python.exe exit code $($p.ExitCode): $($Arguments -join ' ')"
    }
}

Invoke-Python -Arguments @($getPip, "--no-warn-script-location")
Invoke-Python -Arguments @("-m", "pip", "install", "--upgrade", "pip", "--no-warn-script-location", "--disable-pip-version-check")
Invoke-Python -Arguments @("-m", "pip", "install", "-r", $Requirements, "--no-warn-script-location", "--disable-pip-version-check")

if (-not (Test-Path $shazamMarker)) {
    throw "shazamio not found under Lib\site-packages after pip install."
}

Write-Host "[Shazam embed] Done."
exit 0
