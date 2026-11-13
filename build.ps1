# Basalt 配置编辑器 —— 编译脚本
#
# 目标：.NET Framework 4.8 WinForms，用 Roslyn csc 或系统自带的老版 csc 编译。
# 不需要 NuGet、不需要 .csproj、不需要装 .NET SDK；目标机器上也不用装任何运行时。
#
# 产物：release\BasaltConfigTool.exe

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# ---------------------------------------------------------------- 找编译器

$candidates = @(
    'C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe',
    'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\Roslyn\csc.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\Roslyn\csc.exe',
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
    'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
)

$csc = $null
foreach ($c in $candidates) {
    if (Test-Path -LiteralPath $c) { $csc = $c; break }
}

if (-not $csc) {
    Write-Host '[错误] 没有找到 C# 编译器。' -ForegroundColor Red
    Write-Host '       请安装 Visual Studio 2022 生成工具（勾选「.NET 桌面开发」），'
    Write-Host '       或者确认系统里有 C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe。'
    exit 1
}

# ---------------------------------------------------------------- 准备输出目录

$outDir = Join-Path $root 'release'
if (-not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}
$out = Join-Path $outDir 'BasaltConfigTool.exe'

# ---------------------------------------------------------------- 源文件

$sources = @(
    'Json.cs',
    'Catalog.cs',
    'Assoc.cs',
    'MainForm.cs',
    'Program.cs'
) | ForEach-Object { Join-Path (Join-Path $root 'src') $_ }

foreach ($s in $sources) {
    if (-not (Test-Path -LiteralPath $s)) {
        Write-Host "[错误] 缺少源文件：$s" -ForegroundColor Red
        exit 1
    }
}

# ---------------------------------------------------------------- 编译

Write-Host "编译器：$csc"
Write-Host "输出  ：$out"
Write-Host ''

$args = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/warn:4',
    "/out:$out",
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll'
) + $sources

& $csc $args
if ($LASTEXITCODE -ne 0) {
    Write-Host ''
    Write-Host '[失败] 上面有编译错误。' -ForegroundColor Red
    exit 1
}

$size = (Get-Item -LiteralPath $out).Length
Write-Host ''
Write-Host "[成功] 已生成 $out（$size 字节）" -ForegroundColor Green
exit 0
