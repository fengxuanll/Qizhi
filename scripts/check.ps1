$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    dotnet restore Qizhu.slnx --configfile NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw '依赖还原失败。' }
    dotnet build Qizhu.slnx --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw '构建失败。' }
    dotnet test Qizhu.slnx --configuration Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw '测试失败。' }
    dotnet format Qizhu.slnx --no-restore --verify-no-changes --severity warn
    if ($LASTEXITCODE -ne 0) { throw '代码格式检查失败。' }
    if (Test-Path 'web/package-lock.json') {
        Push-Location web
        try {
            npm.cmd ci --ignore-scripts
            if ($LASTEXITCODE -ne 0) { throw '前端依赖还原失败。' }
            foreach ($checkName in @('typecheck','lint','format:check','test','build')) {
                npm.cmd run $checkName
                if ($LASTEXITCODE -ne 0) { throw ('前端检查失败：' + $checkName) }
            }
        }
        finally { Pop-Location }
    }
}
finally { Pop-Location }
