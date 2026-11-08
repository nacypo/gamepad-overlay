# 重新编译 GamepadOverlay.exe
# 需要：.NET Framework 4.0 的 csc.exe（Win10/11 自带）+ 同目录下的三个 WebView2 DLL
# 三个 DLL 缺失时会自动调用 get-deps.ps1 去 NuGet 下载
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$need = @('Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll')
$missing = $need | Where-Object { -not (Test-Path (Join-Path $root $_)) }
if ($missing) {
    Write-Host "缺少 DLL：$($missing -join ', ')，先下载依赖……" -ForegroundColor Yellow
    & (Join-Path $root 'get-deps.ps1')
}

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "找不到 csc.exe：$csc" }

Get-Process GamepadOverlay -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

& $csc /nologo /target:winexe /platform:x64 /optimize+ /langversion:5 /out:GamepadOverlay.exe `
    /r:Microsoft.Web.WebView2.Core.dll `
    /r:Microsoft.Web.WebView2.WinForms.dll `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    src\Overlay.cs

if ($LASTEXITCODE -ne 0) { throw "编译失败（exit $LASTEXITCODE）" }
Write-Host "OK -> $root\GamepadOverlay.exe" -ForegroundColor Green
