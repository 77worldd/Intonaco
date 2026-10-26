# Intonaco Build Script
# Usage: .\build.ps1 [-Configuration Release] [-Publish] [-Installer]

param(
    [string]$Configuration = "Release",
    [switch]$Publish,
    [switch]$Installer,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$ProjectPath = "src\Intonaco\Intonaco.csproj"

Write-Host "╔══════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║       Building Intonaco              ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Clean
if ($Clean) {
    Write-Host "[1/4] Cleaning..." -ForegroundColor Yellow
    dotnet clean $ProjectPath -c $Configuration --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Clean failed" }
} else {
    Write-Host "[1/4] Skipping clean" -ForegroundColor DarkGray
}

# Restore
Write-Host "[2/4] Restoring packages..." -ForegroundColor Yellow
dotnet restore $ProjectPath --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Restore failed" }

# Build
Write-Host "[3/4] Building ($Configuration)..." -ForegroundColor Yellow
dotnet build $ProjectPath -c $Configuration --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "  Build succeeded." -ForegroundColor Green

# Publish
if ($Publish -or $Installer) {
    Write-Host "[4/4] Publishing..." -ForegroundColor Yellow

    if ($Publish) {
        $PublishDir = "publish"

        dotnet publish $ProjectPath `
            -c $Configuration `
            -r win-x64 `
            --self-contained false `
            -o $PublishDir `
            --nologo `
            /p:PublishSingleFile=true `
            /p:IncludeNativeLibrariesForSelfExtract=true

        if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

        $exe = Get-Item "$PublishDir\Intonaco.exe" -ErrorAction SilentlyContinue
        if ($exe) {
            $sizeMB = [math]::Round($exe.Length / 1MB, 2)
            Write-Host "  Published: $($exe.FullName) ($sizeMB MB)" -ForegroundColor Green
        }
    }

    if ($Installer) {
        $InstallerPublishDir = "publish\installer-app"

        dotnet publish $ProjectPath `
            -c $Configuration `
            -r win-x64 `
            --self-contained true `
            -o $InstallerPublishDir `
            --nologo `
            /p:PublishSingleFile=false `
            /p:PublishReadyToRun=true

        if ($LASTEXITCODE -ne 0) { throw "Installer payload publish failed" }

        Copy-Item LICENSE, THIRD-PARTY-NOTICES.md $InstallerPublishDir -Force

        [xml]$projectXml = Get-Content -LiteralPath $ProjectPath
        $version = $projectXml.Project.PropertyGroup.Version |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Select-Object -First 1
        if (-not $version) { throw "Could not read the project version." }

        $setup = & ".\installer\Build-Installer.ps1" `
            -PublishDir $InstallerPublishDir `
            -Version $version `
            -OutputDir "release"
        Write-Host "  Installer: $setup" -ForegroundColor Green
    }
} else {
    Write-Host "[4/4] Skipping publish (use -Publish and/or -Installer)" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "Build complete." -ForegroundColor Green
