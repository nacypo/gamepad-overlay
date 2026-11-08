# Gamepad Overlay

**手柄按什么键，屏幕上就亮什么。** 一个常驻桌面、可鼠标穿透的 HTML 手柄浮层，用来录攻略 / 直播 / 复盘操作。

*A lightweight Windows overlay that shows your controller's inputs on screen in real time — buttons, triggers, sticks, plus live FPS and battery. Transparent, always-on-top, click-through, single exe, no installer.*

![效果](_shots/overlay-default.png)

[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%2F%2011-0078D6?logo=windows&logoColor=white)](#环境要求)
[![.NET](https://img.shields.io/badge/.NET%20Framework-4.0-512BD4?logo=dotnet&logoColor=white)](#从源码构建)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

---

## 下载

到 **[Releases](../../releases)** 页下载最新的 `手柄浮层 vX.X.zip`：

1. 解压到任意位置（绿色免安装，不写注册表）
2. 双击 `一键启动.bat`（或只启动浮层：`启动浮层.bat`）
3. 开游戏

> 从浏览器下载的 zip，解压前记得**右键 → 属性 → 解除锁定**，否则 exe 和 bat 可能被系统拦住。

## 功能

- **按键实时点亮** —— ABXY、肩键、扳机、十字键、摇杆，按下即亮
- **模拟量显示** —— 扳机行程、摇杆方向都能看出来，不只是开关
- **置顶 + 鼠标穿透** —— 盖在游戏上，点击直接穿到游戏里；`Ctrl+Alt+O` 随时切换
- **实时帧率 + 手柄电量** —— 右上角常驻，电量 ≤20% 变红呼吸
- **45% ~ 140% 七档缩放** —— 缩到 55% 只有巴掌大，字号反向补偿，依然看得清
- **自动识别手柄** —— Xbox 显示 ABXY，DualSense 自动变 ✕ ○ □ △ 和 PS 配色
- **摆到哪记到哪** —— 位置、大小、穿透状态存在 `settings.ini`，下次启动原样恢复
- **给 OBS 用** —— 透明 / 深色 / 绿幕三种底色，窗口采集或色度键都行

> 按住手柄任意键时，卡片左下角还会弹出一枚按键胶囊（淡蓝色的 `LB`、绿色的 `A`），
> 录制时特别好用：[按键点亮效果](_shots/overlay-keys.png)。
>
> 缩到 **55%** 就只剩这么大一块：[小尺寸](_shots/overlay-small.png)；
> 换成 PS 手柄会自动切键色：[PS 主题](_shots/overlay-ps.png)；
> 电量掉到 20% 以下整体转红并呼吸：[低电量](_shots/overlay-lowbat.png)。

### 设计

- 白色 Xbox 机身，轮廓和按键位置是**照着一台真机照片逐个量出来的比例**（宽高比 1.416），
  左右两半严格镜像对称；描边用亚像素采样 + 高斯平滑，没有锯齿
- 肩键 LB / RB 是悬在肩上方的两条紫色光条，LT / RT 竖在两侧 —— 四个肩键围成一圈
- D-pad 是"暗件 + 发光缝"，摇杆是"白色凹槽 + 深色帽"
- 所有反馈都是同一套语言：**暗件 + 亮细节 + 发光只在按下时出现**

## 使用

启动后浮层默认在**屏幕右上角**，900×700。退出：`Ctrl + Alt + X`，或托盘图标 → 退出。

### 全局快捷键

| 快捷键 | 作用 |
| --- | --- |
| `Ctrl + Alt + O` | 切换鼠标穿透 |
| `Ctrl + Alt + T` | 切换窗口置顶 |
| `Ctrl + Alt + W / A / S / D` | 上 / 左 / 下 / 右 挪 12px（穿透开着也能挪） |
| `Ctrl + Alt + [` / `]` | 缩小 / 放大一档 |
| `Ctrl + Alt + X` | 退出 |

### 托盘菜单

| 菜单项 | 作用 |
| --- | --- |
| 鼠标穿透 / 窗口置顶 | 同上两个开关 |
| 吸附位置 | 四角 / 屏幕正中 / **移到鼠标位置** |
| 位置微调 | ↑↓←→ 每次 12px |
| 摇杆 Y 轴反向 | 摇杆上下反了就勾这个（少数手柄驱动如此） |
| 大小 | 45% / 55% / 70% / 85% / 100% / 120% / 140% |
| 深色底色 / 绿幕底色 / 透明底色 | 给 OBS 用，默认透明 |
| 退出 | 关掉浮层 |

> 想摆到任意位置：`Ctrl+Alt+O` 关掉穿透 → 拖动标题栏那行 → 再按一次恢复穿透。
> 吸附到四角是**贴着卡片边缘**算的（不是那个 900×700 的透明窗口），所以缩小后照样能贴到屏幕最角落。

---

## 帧率和电量

| 显示 | 含义 |
| --- | --- |
| `118 FPS` | **前台游戏的真实帧率**，从 RTSS 共享内存读出来，和 Afterburner 悬浮窗是同一个数 |
| `235 Hz` | 拿不到时的兜底：浮层自己的刷新率（≈ 显示器刷新率） |
| `100%` | 手柄电量，≤20% 变红并呼吸，21–45% 暖黄，46% 以上绿色，读不到显示 `--` |

**帧率**读的是 **RivaTuner Statistics Server（RTSS）** 的共享内存 `RTSSSharedMemoryV2`，
按前台窗口 PID 查 `帧率 = frames / (time1 - time0)`，每 0.4 秒刷一次。
不注入游戏、不加反作弊风险、不需要管理员权限。**所以想要真实帧率，需要装并运行 RTSS**
（MSI Afterburner 里自带，官网也有独立包）。没装就显示浮层自己的 `Hz`，其它功能不受影响。

**电量**走的是**蓝牙设备属性**而不是 XInput：`XInputGetBatteryInformation` 对 BLE 的 Xbox 手柄
只会返回 `BATTERY_TYPE_DISCONNECTED`。真正做法是调 `cfgmgr32.dll`，从 HID 实例 ID 抠出蓝牙地址，
匹配 `BTHLE\Dev_xxxxxxxxxxxx` 节点，读 `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2`（`DEVPKEY_Bluetooth_Battery`）。
**所以只有蓝牙连接时电量才准**，USB 直连或用 Xbox 无线适配器会显示 `--`。

## 命令行参数

```
GamepadOverlay.exe [--anchor=tl|tr|bl|br|c] [--x=.. --y=..] [--margin=12]
                   [--w=900] [--h=700] [--scale=1.0]
                   [--opaque] [--no-click-through] [--no-top] [--query="theme=ps&demo=1"]
```

| 参数 | 说明 |
| --- | --- |
| `--anchor=` | 初始吸附位置（默认 `tr`） |
| `--x= --y=` | 直接指定左上角坐标 |
| `--margin=` | 吸附时距离屏幕边缘的像素（默认 12，`0` 就是贴死） |
| `--w= --h=` | 窗口尺寸（默认 900×700） |
| `--scale=` | 内容缩放 0.45–1.4 |
| `--opaque` | 实心底色（等价托盘里的「深色底色」），给 OBS 窗口采集 |
| `--no-click-through` | 启动时不开启鼠标穿透 |
| `--no-top` | 启动时不置顶 |
| `--query=` | 透传给页面的参数，调试用（`demo=1` 假手柄、`theme=ps`、`bat=8` 等） |

## 给 OBS / 直播用

- **透明叠加**：`来源 → 窗口采集` 选 `Gamepad Overlay`，勾选「允许透明度」，方法选 **Windows 10 (1903 及以上)**
- **绿幕**：托盘 → 绿幕底色，采集后加「色度键」滤镜，键色 `#00FF00`
- **深色底**：托盘 → 深色底色，适合不透明的小窗口叠加

## 从源码构建

需要 Windows 10/11 + .NET Framework 4.0（系统自带），不需要装 SDK。

```powershell
git clone <改成你自己的仓库地址>
cd gamepad-overlay
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

`build.ps1` 会发现缺 WebView2 DLL，自动调用 `get-deps.ps1` 去 NuGet 拉 SDK 并取出三个文件：

| 文件 | 来源 |
| --- | --- |
| `Microsoft.Web.WebView2.Core.dll` | `lib\net462\` |
| `Microsoft.Web.WebView2.WinForms.dll` | `lib\net462\` |
| `WebView2Loader.dll` | `runtimes\win-x64\native\` |

> `csc.exe` 用的是 .NET Framework 4.0 那个，**只支持 C# 5** —— 改 `src/Overlay.cs` 时不能写
> `$"..."` 插值、`?.`、表达式体成员。

## 原理

```
XInput (xinput1_4.dll)          ┌──────────────┐   JSON via postMessage   ┌──────────────┐
  ──► 后台线程每 16ms 轮询 ──►  │ GamepadOverlay│ ──────────────────────► │  WebView2    │
实时帧率 (RTSS 共享内存)        │   .exe (C#)   │                         │ index.html   │
蓝牙电量 (cfgmgr32)             └──────────────┘ ◄──── ready / rect ───── └──────────────┘
```

- 页面是**纯 HTML + SVG + JS**，单文件零依赖，可以直接用浏览器打开预览
- 透明靠 `TransparencyKey`（品红抠像）+ `DefaultBackgroundColor = Transparent` 实现**逐像素透明**
- 窗口用 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT`，并每 1.2 秒重新压回置顶层
  （有些无边框全屏游戏会把自己也设成置顶）
- 页面会把卡片的实际矩形上报给宿主，宿主据此做像素级贴边

## 已知限制

- **独占全屏（Exclusive Fullscreen）盖不住** —— 那是系统层面的限制，任何窗口都不行。
  把游戏改成「窗口化 / 无边框窗口」即可（BeamNG：`Options → Graphics → Display Mode → Window`）。
- **只支持 XInput 手柄**（Xbox 系列及绝大多数第三方）。纯 DirectInput 的老手柄读不到。
- **电量只在蓝牙连接时可用**（见上文）。
- 手柄需要**先在系统里连上**，再启动浮层。

## License

[MIT](LICENSE) —— 随便用、随便改，出问题别赖我 :)

## 致谢

- 帧率数据来自 [RTSS](https://www.guruguru3d.com/files-details/rtss-rivatuner-statistics-server-download.html) 的共享内存
- 界面基于 Microsoft Edge WebView2
