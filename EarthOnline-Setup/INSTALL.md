# EarthOnline Windows 安装器使用说明

## 📦 安装包内容

EarthOnline-Setup-v1.0.5.zip 包含以下文件：
- EarthOnline-Setup.exe - 主安装程序
- 依赖文件 - .NET 8.0 运行时库
- 安装说明文档

## 🎯 安装步骤

### 图形界面安装
1. 解压 `EarthOnline-Setup-v1.0.5.zip` 到任意目录
2. 双击 `EarthOnline-Setup.exe` 启动安装程序
3. 按照向导提示完成安装
4. 选择安装选项：
   - 安装路径（默认：%LocalAppData%\EarthOnline）
   - 桌面快捷方式（默认勾选）
   - 开始菜单快捷方式（默认勾选）
   - 任务栏固定（默认不勾选，Windows 10/11 兼容性提示）
5. 点击"安装"按钮开始安装
6. 完成后可选择运行应用

### 命令行安装
```bash
# 静默安装到默认位置，创建所有快捷方式
EarthOnline-Setup.exe /silent /desktop /startmenu

# 自定义安装路径，不创建快捷方式
EarthOnline-Setup.exe /silent /dir="C:\Program Files\EarthOnline" 

# 创建桌面快捷方式但不创建开始菜单快捷方式
EarthOnline-Setup.exe /silent /desktop
```

## ⚙️ 命令行参数

| 参数 | 描述 | 默认值 |
|------|------|--------|
| `/silent` | 静默安装模式 | 否 |
| `/dir="path"` | 自定义安装路径 | %LocalAppData%\EarthOnline |
| `/desktop` | 创建桌面快捷方式 | 否 |
| `/startmenu` | 创建开始菜单快捷方式 | 否 |
| `/taskbar` | 固定到任务栏（Windows 10/11） | 否 |

## 📁 安装位置

- **默认位置**：`%LocalAppData%\EarthOnline`
- **自定义位置**：可通过 `/dir` 参数指定

## 📝 注意事项

1. **管理员权限**：安装程序需要管理员权限才能创建开始菜单快捷方式
2. **任务栏固定**：Windows 10/11 需要额外权限，安装程序会提示
3. **文件大小**：安装包约 134MB（包含 .NET 8.0 运行时）
4. **卸载**：可通过控制面板的程序和功能卸载 EarthOnline

## 🔧 故障排除

### 常见问题
- **安装失败**：检查是否以管理员身份运行
- **快捷方式未创建**：确保有足够的权限
- **任务栏固定失败**：Windows 10/11 需要手动固定

### 联系支持
如遇问题，请联系开发团队获取帮助。