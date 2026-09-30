# EarthOnline Windows 安装器

EarthOnline Windows 安装器是一个基于 WPF 的可视化安装程序，用于简化 EarthOnline 应用的安装过程。

## 📦 功能特性

- **可视化安装向导**：治愈系 UI 设计，友好的安装体验
- **自定义安装选项**：
  - 安装路径选择（默认 %LocalAppData%，可选 Program Files）
  - 桌面快捷方式（默认勾选）
  - 开始菜单快捷方式（默认勾选）
  - 任务栏固定（默认不勾选，带 Windows 兼容性提示）
- **静默安装支持**：通过命令行参数实现无人值守安装
- **自包含应用**：包含 .NET 8.0 运行时，无需额外安装

## 🛠️ 技术实现

- **框架**：WPF (.NET 8.0)
- **打包**：自包含单文件应用
- **资源传输**：主程序作为嵌入资源
- **快捷方式创建**：PowerShell 脚本生成 .lnk 文件
- **安装流程管理**：状态机控制安装流程

## 📁 项目结构

```
EarthOnline-Setup/
├── bin/                          # 构建输出目录
│   └── Release/
│       └── net8.0-windows/
│           └── win-x64/          # 自包含安装程序
│               ├── EarthOnline-Setup.exe
│               └── ... (依赖文件)
├── EarthOnline-Setup.csproj       # 项目文件
├── App.xaml                      # 应用资源
├── App.xaml.cs                   # 应用启动逻辑
├── SetupManager.cs               # 安装核心逻辑
├── MainWindow.xaml               # 安装向导 UI
├── MainWindow.xaml.cs            # 向导状态管理
├── EarthOnline-Setup-v1.0.5.zip   # 完整安装包
├── INSTALL.md                    # 安装说明
├── README.md                     # 项目说明
└── test_install.bat              # 测试脚本
```

## 🚀 使用方法

### 图形界面安装
1. 双击 `EarthOnline-Setup.exe`
2. 按照向导提示完成安装
3. 选择安装选项
4. 点击"安装"按钮
5. 完成安装后可选择运行应用

### 命令行安装
```bash
# 静默安装到默认位置，创建所有快捷方式
EarthOnline-Setup.exe /silent /desktop /startmenu

# 自定义安装路径，不创建快捷方式
EarthOnline-Setup.exe /silent /dir="C:\Program Files\EarthOnline" 
```

## 📋 命令行参数

| 参数 | 描述 | 默认值 |
|------|------|--------|
| `/silent` | 静默安装模式 | 否 |
| `/dir="path"` | 自定义安装路径 | %LocalAppData%\EarthOnline |
| `/desktop` | 创建桌面快捷方式 | 否 |
| `/startmenu` | 创建开始菜单快捷方式 | 否 |
| `/taskbar` | 固定到任务栏（Windows 10/11） | 否 |

## 🔧 开发说明

### 构建项目
```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

### 测试安装
运行 `test_install.bat` 进行基本安装测试。

## 📝 版本历史

- **v1.0.5** (2026-09-30): 初始版本，支持可视化安装和静默安装

## 📞 联系支持

如遇问题，请联系开发团队获取帮助。