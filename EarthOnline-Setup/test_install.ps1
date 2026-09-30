# EarthOnline 安装器测试脚本

Write-Host "=== EarthOnline 安装器测试 ==="
Write-Host

$installerPath = "$PSScriptRoot\bin\Release\net8.0-windows\win-x64\EarthOnline-Setup.exe"
$testDir = "$PSScriptRoot\test_install"
$desktopShortcut = "$env:USERPROFILE\Desktop\EarthOnline.lnk"
$startMenuShortcut = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\EarthOnline\EarthOnline.lnk"

# 检查安装器文件
if (-not (Test-Path $installerPath)) {
    Write-Error "错误：安装器文件不存在！"
    exit 1
}
Write-Host "✓ 安装器文件存在"

# 创建测试目录
if (Test-Path $testDir) {
    Remove-Item -Recurse -Force $testDir
}
New-Item -ItemType Directory -Path $testDir | Out-Null
Write-Host "✓ 测试目录已创建"

# 执行静默安装测试
Write-Host "执行静默安装测试..."
Write-Host "安装参数：/silent /dir=""$testDir"" /desktop /startmenu"
Start-Process -FilePath $installerPath -ArgumentList "/silent /dir=`"$testDir`" /desktop /startmenu" -Wait

# 检查安装结果
if (-not (Test-Path "$testDir\EarthOnline.exe")) {
    Write-Error "错误：主程序文件未找到！"
    exit 1
}
Write-Host "✓ 主程序文件已安装"

if (-not (Test-Path $desktopShortcut)) {
    Write-Warning "警告：桌面快捷方式未创建"
} else {
    Write-Host "✓ 桌面快捷方式已创建"
}

if (-not (Test-Path $startMenuShortcut)) {
    Write-Warning "警告：开始菜单快捷方式未创建"
} else {
    Write-Host "✓ 开始菜单快捷方式已创建"
}

Write-Host
Write-Host "=== 测试完成 ==="
Write-Host
Write-Host "安装位置：$testDir"
Write-Host "主程序：$testDir\EarthOnline.exe"
Write-Host
Write-Host "请手动检查快捷方式是否正常工作"
Write-Host

# 询问是否保留测试文件
$keep = Read-Host "是否保留测试安装文件？(y/n)"
if ($keep -eq 'y') {
    Write-Host "测试文件已保留在：$testDir"
} else {
    Remove-Item -Recurse -Force $testDir
    Remove-Item $desktopShortcut -ErrorAction SilentlyContinue
    Remove-Item $startMenuShortcut -ErrorAction SilentlyContinue
    Write-Host "测试文件已清理"
}