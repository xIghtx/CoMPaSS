param([string]$RuntimeDirectory = (Join-Path $env:LOCALAPPDATA 'CoMPaSS/PrimalRuntime'))
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$runtimeRoot = [IO.Path]::GetFullPath($RuntimeDirectory)
[IO.Directory]::CreateDirectory($runtimeRoot) | Out-Null
$pythonRoot = Join-Path $runtimeRoot 'python'
$modelRoot = Join-Path $runtimeRoot 'model'
$repositoryRoot = Join-Path $runtimeRoot 'upstream'
foreach ($folder in @($pythonRoot,$modelRoot,$repositoryRoot)) { [IO.Directory]::CreateDirectory($folder) | Out-Null }
function Download([string]$Url,[string]$Destination) {
    if (Test-Path -LiteralPath $Destination) { return }
    $client = New-Object Net.WebClient
    try { $client.DownloadFile($Url,$Destination + '.part'); Move-Item -LiteralPath ($Destination + '.part') -Destination $Destination -Force }
    finally { $client.Dispose() }
}
try {
    Write-Output '1/5 Python 3.7.9 isolated runtime'
    $pythonZip = Join-Path $runtimeRoot 'python-3.7.9-embed-amd64.zip'
    Download 'https://www.python.org/ftp/python/3.7.9/python-3.7.9-embed-amd64.zip' $pythonZip
    if (-not (Test-Path (Join-Path $pythonRoot 'python.exe'))) { Expand-Archive -LiteralPath $pythonZip -DestinationPath $pythonRoot -Force }
    [IO.File]::WriteAllText((Join-Path $pythonRoot 'python37._pth'), "python37.zip`r`n.`r`nLib/site-packages`r`nimport site`r`n")
    $python = Join-Path $pythonRoot 'python.exe'
    $bootstrap = Join-Path $runtimeRoot 'get-pip.py'
    Download 'https://bootstrap.pypa.io/pip/3.7/get-pip.py' $bootstrap
    if (-not (Test-Path (Join-Path $pythonRoot 'Lib/site-packages/pip'))) {
        & $python $bootstrap '--no-warn-script-location' 'pip==24.0' 'setuptools==68.0.0' 'wheel==0.42.0'
        if ($LASTEXITCODE -ne 0) { throw 'pip bootstrap failed' }
    }
    Write-Output '2/5 TensorFlow CPU and pinned dependencies'
    & $python -m pip install --disable-pip-version-check --no-warn-script-location 'tensorflow==1.15.0' 'numpy==1.18.5' 'protobuf==3.20.3' 'h5py==2.10.0'
    if ($LASTEXITCODE -ne 0) { throw 'TensorFlow installation failed' }
    Write-Output '3/5 Original PRIMAL source'
    foreach ($name in @('ACNet.py','mapgenerator.py','README.md')) {
        Download ('https://raw.githubusercontent.com/gsartoretti/PRIMAL/master/' + $name) (Join-Path $repositoryRoot $name)
    }
    Write-Output '4/5 Public trained model (about 180 MB download)'
    $archive = Join-Path $runtimeRoot 'model_primal.7z'
    Download 'https://drive.usercontent.google.com/download?id=1AtAeUwLF1Rn_X3b2FHkHi4fI5vveUHF6&export=download&confirm=t' $archive
    if ((Get-FileHash -LiteralPath $archive).Hash -ne '284BE3A068AE304F085CFF58B13D0544819A09ECF83DA5D10963B31A1BBCF25F') {
        Move-Item -LiteralPath $archive -Destination ($archive + '.invalid-' + (Get-Date -Format 'yyyyMMddHHmmss'))
        throw 'Public checkpoint checksum differs from the verified model. Retry download or check the upstream release.'
    }
    $entries = @(& tar.exe -tf $archive)
    if ($LASTEXITCODE -ne 0) { throw 'Model archive is invalid or the download provider returned an HTML page.' }
    foreach ($entry in $entries) {
        $resolved = [IO.Path]::GetFullPath((Join-Path $modelRoot $entry))
        if (-not $resolved.StartsWith($modelRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Archive entry escapes model folder.' }
    }
    & tar.exe -xf $archive -C $modelRoot
    if ($LASTEXITCODE -ne 0) { throw 'Model extraction failed' }
    $index = Get-ChildItem -LiteralPath $modelRoot -Recurse -File -Filter '*.index' | Sort-Object FullName | Select-Object -First 1
    if ($null -eq $index) { throw 'No TensorFlow checkpoint found in the public model.' }
    $configuration = @{ repository=$repositoryRoot; checkpoint=$index.DirectoryName }
    [IO.File]::WriteAllText((Join-Path $runtimeRoot 'config.json'), ($configuration | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
    Write-Output '5/5 Real policy inference check'
    $env:COMPASS_PRIMAL_CONFIG = Join-Path $runtimeRoot 'config.json'
    $env:TF_CPP_MIN_LOG_LEVEL = '2'
    $adapter = Join-Path $PSScriptRoot 'primal_adapter.py'
    $probe = '{"protocolVersion":1,"width":5,"height":5,"blockedCells":"0000000000000000000000000","maxTimeSteps":32,"seed":23,"agents":[{"id":0,"startX":1,"startY":2,"goalX":3,"goalY":2}]}'
    $output = $probe | & $python $adapter
    if ($LASTEXITCODE -ne 0) { throw 'Python inference check failed' }
    $result = $output | ConvertFrom-Json
    if (-not $result.success -or $result.decisions.Count -lt 1) { throw ('Model check failed: ' + $result.failureReason) }
    [IO.File]::WriteAllText((Join-Path $runtimeRoot 'verification.json'), ($output -join "`n"), (New-Object Text.UTF8Encoding($false)))
    $ready = @{ python=$python; modelSource=$result.modelSource; checkedAt=(Get-Date).ToString('o'); checkpointArchiveSha256=(Get-FileHash -LiteralPath $archive).Hash }
    [IO.File]::WriteAllText((Join-Path $runtimeRoot 'ready.json'), ($ready | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
    Write-Output 'READY: public PRIMAL checkpoint restored and real inference passed.'
} catch { Write-Error $_; exit 1 }
