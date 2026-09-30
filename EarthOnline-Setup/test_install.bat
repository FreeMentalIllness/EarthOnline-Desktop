@echo off
setlocal

echo === EarthOnline 安装器测试 ===
echo.

set INSTALLER_PATH=%~dp0\bin\Release\net8.0-windows\win-x64\EarthOnline-Setup.exe
set TEST_DIR=%~dp0\test_install

echo 1. 检查安装器文件...
if not exist "%INSTALLER_PATH%" (
    echo 错误：安装器文件不存在！
    exit /b 1
)
echo ✓ 安装器文件存在

echo 2. 创建测试目录...
if exist "%TEST_DIR%" rmdir /s /q "%TEST_DIR%"
mkdir "%TEST_DIR%"
echo ✓ 测试目录已创建

echo 3. 执行静默安装测试...
echo 安装参数：/silent /dir="%TEST_DIR%"
"%INSTALLER_PATH%" /silent /dir="%TEST_DIR%"

echo 4. 检查安装结果...
if not exist "%TEST_DIR%\EarthOnline.exe" (
    echo 错误：主程序文件未找到！
    exit /b 1
)
echo ✓ 主程序文件已安装

echo.
echo === 测试完成 ===
echo.
echo 安装位置："%TEST_DIR%"
echo 主程序："%TEST_DIR%\EarthOnline.exe"
echo.
echo 请手动检查快捷方式是否正常工作
echo.
echo 按任意键继续...
pause >nul