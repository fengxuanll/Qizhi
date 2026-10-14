param([switch]$InitializeDatabase, [switch]$TrustCertificate)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    dotnet restore Qizhu.slnx --configfile NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw '后端依赖还原失败。' }
    if (Test-Path 'web/package-lock.json') {
        Push-Location web
        try {
            npm.cmd ci --ignore-scripts
            if ($LASTEXITCODE -ne 0) { throw '前端依赖还原失败，请确认 Node.js 24 已安装。' }
        }
        finally { Pop-Location }
        New-Item -ItemType Directory -Path 'work/certs' -Force | Out-Null
        dotnet dev-certs https --export-path work/certs/localhost.pem --format Pem --no-password
        if ($LASTEXITCODE -ne 0) { throw '开发证书创建失败。' }
        if ($TrustCertificate) {
            dotnet dev-certs https --trust
            if ($LASTEXITCODE -ne 0) { throw '开发证书未被信任。' }
        }
    }
    if ($InitializeDatabase) {
        dotnet run --project src/Qizhu.Api --launch-profile Qizhu.Api -- --Database:Initialize=true --Bootstrap:Enabled=true
        if ($LASTEXITCODE -ne 0) { throw '数据库初始化失败，原有账号密码不会被重置。' }
    }
    Write-Host '环境准备完成。使用 scripts/dev.ps1 启动前后端。'
}
finally { Pop-Location }
