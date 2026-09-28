# Assets 目录说明

## 入库资源（公开，无密钥）

| 文件 | 用途 | 是否入库 |
|---|---|---|
| `app_logo.ico` / `app_logo.png` | 应用图标（exe / 窗口 / 托盘 / 标题栏共用），由安卓 `mipmap-xxxhdpi/ic_launcher.png` 生成 | ✅ 入库 |
| `map.html` | 地图页（WebView2 加载）。**不含任何 Key**，Key 由程序运行时注入 | ✅ 入库 |

## 本地可选文件（不入库）

### `amap_default.json` —— 高德 Key 内置回退

- **已被 `.gitignore` 第 24 行忽略，永不入库**；`EarthOnline-Desktop.csproj` 用
  `Condition="Exists(...)"` 条件包含，文件不存在时不影响构建。
- 克隆本仓库后该文件**不存在**，此时若也没在设置页自填 Key，地图页会直接降级为列表视图
  （足迹增删改不受影响）——这是预期行为，不是缺陷。
- 想内置自己的 Key，按下面格式新建一份即可（仍不会入库）：

```json
{
  "key": "你的高德 Web 端 JS API Key",
  "securityJsCode": "你的高德安全密钥"
}
```

## Key 读取优先级

1. **设置页自填**（DPAPI 加密存 `%LOCALAPPDATA%\EarthOnline\settings.json` 的 `amapKeyEnc` / `amapSecEnc`）
2. **`Assets/amap_default.json`**（本目录，本地回退）
3. **都没有** → 地图降级为列表视图，不发任何外部请求

实现见 `Services/AmapConfig.cs`。

## 红线

- 源码、`.cs` / `.xaml` / `map.html` / 任何入库文件里**不得出现明文 Key**。
- 旧 Key 仍存在于 Git 历史中（历史提交），按用户红线**不做 filter-repo / 强推重写**；
  建议在高德控制台吊销换新，并在设置页填入新 Key 覆盖。
