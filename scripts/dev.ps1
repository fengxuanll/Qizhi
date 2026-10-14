param([switch]$MultiTenant, [switch]$ApiOnly)
$ErrorActionPreference = 'Stop'
$frontendProcess = $null
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    dotnet restore Qizhu.slnx --configfile NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw '依赖还原失败。' }
    if (-not $ApiOnly -and (Test-Path 'web/package.json')) {
        if (-not (Test-Path 'web/node_modules/vite/bin/vite.js')) { throw '请先运行 setup.ps1 安装前端依赖。' }
        if (-not (Test-Path 'work/certs/localhost.pem')) { throw '请先运行 setup.ps1 创建开发证书。' }
        $nodeExecutable = (Get-Command node.exe -ErrorAction Stop).Source
        $frontendProcess = Start-Process -FilePath $nodeExecutable -ArgumentList @('node_modules/vite/bin/vite.js','--host','localhost') -WorkingDirectory (Join-Path $PWD 'web') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $PWD 'work/frontend.stdout.log') -RedirectStandardError (Join-Path $PWD 'work/frontend.stderr.log') -PassThru
        Write-Host '管理端：https://localhost:5173；API：http://localhost:5080'
    }
    $runArguments = @('run', '--project', 'src/Qizhu.Api', '--no-restore', '--launch-profile', 'Qizhu.Api')
    if ($MultiTenant) { $runArguments += @('--', '--Framework:DeploymentMode=MultiTenant') }
    dotnet @runArguments
    if ($LASTEXITCODE -ne 0) { throw '服务启动失败。' }
}
finally {
    if ($frontendProcess -and -not $frontendProcess.HasExited) { taskkill.exe /PID $frontendProcess.Id /T /F | Out-Null }
    Pop-Location
}
