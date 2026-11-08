# 下载 WebView2 SDK（NuGet 包）并取出编译所需的三个 DLL
# 依赖：PowerShell 5.1+、能联网
param(
    [string]$Version = '1.0.4314.35'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$need = @('Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll')
$missing = $need | Where-Object { -not (Test-Path (Join-Path $root $_)) }
if (-not $missing) {
    Write-Host '三个 DLL 都在，跳过下载。' -ForegroundColor Green
    return
}

Write-Host "缺少：$($missing -join ', ')"
Write-Host "下载 Microsoft.Web.WebView2 $Version ..."

$pkg = Join-Path $env:TEMP "microsoft.web.webview2.$Version.nupkg"
$url = "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$Version/microsoft.web.webview2.$Version.nupkg"
Write-Host "  $url"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri $url -OutFile $pkg -UseBasicParsing

$zip = [IO.Path]::ChangeExtension($pkg, '.zip')
Copy-Item $pkg $zip -Force
$out = Join-Path $env:TEMP ("wv2sdk_" + [Guid]::NewGuid().ToString('N').Substring(0, 6))
Expand-Archive -Path $zip -DestinationPath $out -Force

$pairs = @(
    @{ from = 'lib\net462\Microsoft.Web.WebView2.Core.dll';        to = 'Microsoft.Web.WebView2.Core.dll' },
    @{ from = 'lib\net462\Microsoft.Web.WebView2.WinForms.dll';    to = 'Microsoft.Web.WebView2.WinForms.dll' },
    @{ from = 'runtimes\win-x64\native\WebView2Loader.dll';        to = 'WebView2Loader.dll' }
)
foreach ($p in $pairs) {
    $src = Join-Path $out $p.from
    if (-not (Test-Path $src)) { throw "包里找不到 $($p.from)" }
    Copy-Item $src (Join-Path $root $p.to) -Force
    Write-Host "  + $($p.to)" -ForegroundColor Green
}

Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $pkg, $zip -Force -ErrorAction SilentlyContinue
Write-Host '完成，现在可以运行 build.ps1 了。' -ForegroundColor Green
