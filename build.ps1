param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceDirectory = Join-Path $root "src"
$outputDirectory = $root
$outputPath = Join-Path $outputDirectory "TokeIME.exe"

$cscCandidates = @(
    (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
    (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
)
$csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) {
    throw "找不到 .NET Framework C# 编译器 csc.exe。请安装 .NET Framework 4.8 Developer Pack。"
}

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$frameworkDirectory = Split-Path -Parent $csc
$referenceNames = @(
    "System.dll",
    "System.Core.dll",
    "System.Drawing.dll",
    "System.Windows.Forms.dll",
    "System.Web.Extensions.dll"
)
$references = foreach ($referenceName in $referenceNames) {
    $candidate = Join-Path $frameworkDirectory $referenceName
    if (Test-Path -LiteralPath $candidate) { $candidate } else { $referenceName }
}

$sources = Get-ChildItem -Path $sourceDirectory -Filter *.cs | Sort-Object Name | ForEach-Object { $_.FullName }
$arguments = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/out:$outputPath",
    "/win32manifest:$(Join-Path $sourceDirectory 'app.manifest')",
    "/win32icon:$(Join-Path $sourceDirectory 'TrayIcon.ico')",
    "/resource:$(Join-Path $sourceDirectory 'TrayIcon.ico'),TokeIME.TrayIcon.ico",
    "/resource:$(Join-Path $sourceDirectory 'TrayIconActive.ico'),TokeIME.TrayIconActive.ico"
)

if ($Configuration -eq "Release") {
    $arguments += "/optimize+"
    $arguments += "/debug-"
} else {
    $arguments += "/optimize-"
    $arguments += "/debug+"
    $arguments += "/debug:full"
}

foreach ($reference in $references) {
    $arguments += "/reference:$reference"
}
$arguments += $sources

& $csc $arguments
if ($LASTEXITCODE -ne 0) {
    throw "编译失败，csc.exe 退出码：$LASTEXITCODE"
}

$file = Get-Item -LiteralPath $outputPath
Write-Host ("构建成功：{0} ({1:N0} bytes)" -f $file.FullName, $file.Length)
