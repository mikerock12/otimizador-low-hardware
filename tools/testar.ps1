$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $raiz
try {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    if (-not (Test-Path -LiteralPath $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe' }
    New-Item -ItemType Directory -Path bin -Force | Out-Null
    & $csc /nologo /target:exe /codepage:65001 /main:RegressionTests /out:bin\RegressionTests.exe /r:System.Management.dll /r:System.ServiceProcess.dll /r:System.Web.Extensions.dll /r:System.Core.dll /recurse:src\*.cs tests\RegressionTests.cs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & ./bin/RegressionTests.exe
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
} finally { Pop-Location }
