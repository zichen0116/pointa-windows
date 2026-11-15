# 映点 Pointa · Windows

手机演示翻页笔的 Windows 接收端：在独立透明覆盖层显示激光和拖尾，并向前台演示应用发送上一页/下一页。激光不移动系统鼠标，不抢前台焦点。产品受 [lan-laser-pen](https://github.com/fuxing0910-hue/lan-laser-pen) 启发；0.1.4 移植其网页贝塞尔拖尾，来源与许可见 [对齐说明](docs/reference-alignment.md)。

**当前是 0.1.6 开发验证版。** 配套 [pointa-android](https://github.com/zichen0116/pointa-android) 0.1.4 已实现连接、翻页、触控和空中指针。构建、自测及 UI smoke 只证明其覆盖范围；真实手机扫码、双端操作、投影/多屏硬件、延迟、安装升级和安全外审均需各自验收，不能据此宣称产品已成熟。

## 功能与技术

- WPF 官方 Fluent 控件与系统明暗主题：配对窗、设置、确认窗及圆角托盘菜单；统一图标与设备授权/撤销。
- 独立原生 layered window，Skia 渲染收尖贝塞尔拖尾及光点柔光，鼠标穿透；前台应用保护及离散翻页去重。
- Kestrel TLS WebSocket；QR 证书指纹绑定和短效令牌；六位码 SRP-6a、证书绑定及 PC 批准。
- 当前用户 DPAPI 保护本地信任数据；单活跃控制端、断线清理、UDP 发现。
- C# / .NET 10；Windows 11 x64 优先，Windows 10 x64 兼容性待实测。无云账号、无公网中转、无默认遥测。

本应用层 SRP 集成尚未外部安全审计。UDP 发现只是定位线索；它不能替代证书绑定和设备认证。mDNS 尚未实现。未支持系统鼠标点击/拖动、文件传输或远程画面流。

0.1.2 按桌面端使用反馈精简：主窗移除底栏，刷新配对码位于覆盖层状态卡片右侧；底部只有“设置”和“断开连接”。设备管理合并到设置，仅保留语言、登录启动并收进托盘、防火墙和已授权设备。激光颜色、大小、拖尾、平滑由手机端调整，Windows 保存偏好时不覆盖这些参数。新图标的生成提示词和资源路径见 [桌面视觉更新](docs/windows-ui-0.1.2.md)。

0.1.3 将可见 UI 迁移为 .NET 10 自带的 WPF Fluent 主题，移除 WinForms 设置控件和自绘按钮，托盘也使用相同体系的 ContextMenu。展开语言列表、鼠标悬停、勾选与键盘焦点采用框架标准模板；系统深色模式下不再显示白色旧菜单。实现与验证范围见 [Fluent 迁移说明](docs/windows-ui-0.1.3.md)。

0.1.5 将设备名/IP 移到品牌右侧，删除覆盖层状态卡，刷新按钮与设备码放在一起；最小化保留任务栏入口，支持最大化，配对码有效期统一为 3 分钟。详见 [窗口改进及验证](docs/windows-ui-0.1.5.md)。

0.1.6 将主窗宽度从 520 缩为 416 DIP，二维码保持 176 DIP；托盘只留打开配对、设置、退出。配对后浏览器、PDF 和演示编辑窗口默认可翻页，无需逐个授权；可在设置中收紧为只允许 PowerPoint/WPS 放映窗口。系统网络通知未改变实际监听地址时，保留连接与设备码。

## 构建与自测

0.1.4 对齐参考网页的贝塞尔曲线、沿路径变宽/增亮、停留渐隐和光点阴影；标准参数为光点直径 12 DIP、拖尾 500 ms、曲线张力 0.3。激光颜色由 Android 0.1.2 的主题决定，无单独颜色选项。原项目函数生成的几何基准与真实浏览器逐像素对比方法见 [参考算法对齐](docs/reference-alignment.md)。

在 Windows 安装 .NET 10 SDK，用 PowerShell 从仓库根目录执行：

```powershell
./scripts/build.ps1
./scripts/test.ps1
```

脚本默认使用 PATH 中的 `dotnet`。也可传入独立 SDK 路径：

```powershell
./scripts/build.ps1 -DotNet '../.tools/dotnet/dotnet.exe'
./scripts/test.ps1 -DotNet '../.tools/dotnet/dotnet.exe'
```

`test.ps1` 执行 `tests/Pointa.SelfTest` 的断言测试，包括真实回环 WSS 配对/控制场景；它不会启动桌面 UI。测试结果以当次退出码和输出为准，验收矩阵保留尚未验证的项目。

开发启动使用 `dotnet run --project src/Pointa.Windows/Pointa.Windows.csproj -c Release`。需要人工 UI smoke 时，可在没有其他 smoke 操作时执行：

```powershell
$env:DOTNET_ROOT = Split-Path (Get-Command dotnet).Source -Parent
& './src/Pointa.Windows/bin/Release/net10.0-windows/Pointa.exe' --smoke-test './artifacts/ui-smoke'
```

独立 SDK 环境将 `DOTNET_ROOT` 指向其目录。smoke 输出截图和结果文件，使用自己的数据目录；它不替代 Android、投影、真实前台演示应用或长期性能测试。

## 自包含分发与安装器

仅生成 Windows x64 自包含程序目录及 ZIP：

```powershell
./scripts/publish.ps1
```

从 [Inno Setup 官方网站](https://jrsoftware.org/isdl.php) 安装 Inno Setup 6 后，可同时编译安装器：

```powershell
./scripts/publish.ps1 -Iscc 'C:/Program Files (x86)/Inno Setup 6/ISCC.exe'
```

脚本也接受 `-DotNet`。版本来自 `Directory.Build.props`。产物为 `artifacts/win-x64/`、`artifacts/Pointa-<版本>-win-x64.zip` 和提供 `-Iscc` 时的 `artifacts/Pointa-<版本>-Setup.exe`。重复发布只清理该生成目录，不读取或打包用户配置。ZIP 解压后运行 `Pointa.exe`，不需要手动安装 .NET。

安装器默认安装到当前用户的 `%LOCALAPPDATA%\Programs\Pointa`，不请求管理员权限，建立开始菜单入口。升级使用 Windows Restart Manager 检查并关闭使用中的应用；卸载先请求 Pointa 优雅退出，并删除本程序开机启动项。用户配置保存在 `%LOCALAPPDATA%\Pointa`；卸载默认保留，交互卸载时可以明确选择删除，静默卸载始终保留。安装包当前未配置代码签名，安装/升级/卸载仍需干净环境验证；安装向导暂使用英文，应用内提供中英文。

GitHub Actions 在 Windows runner 上执行构建、自测与打包，上传 ZIP/Setup 作为开发构建产物，不自动发布正式 Release。编译打包不会自动安装程序、添加开机自启或修改防火墙。

## 连接帮助

1. 手机和电脑需处于互相可达的局域网；相同 Wi-Fi 不保证访客/校园/公司网络允许互访。
2. 从 0.1.1 开始自动选择在线 Wi-Fi／有默认网关的有线网卡，仍只监听私有 IPv4 和回环地址。Windows 的 Public 分类不再导致二维码隐藏或只监听回环；无网关的 VMware/Hyper-V 虚拟网卡不优先作为配对地址。配对窗直接显示 IP，点击即可复制。
3. 默认端口为 TCP `32145` 和 UDP `32146`。如果防火墙阻止入站，可在设置中点击“设置防火墙”，由 Windows 请求管理员确认：只为当前可执行文件开放这两个端口、RemoteAddresses=LocalSubnet，适用于公用/专用/域配置；不关闭系统防火墙。安装器不会自动创建这些规则。
4. UDP 发现不可用时，在 Android 连接弹层手动输入电脑 IP 和六位设备码；扫码/手动 IP 都无法绕过访客网络隔离。可换可互访网络或受信任热点。
5. 先把 PowerPoint、WPS、浏览器演示或 PDF 阅读器置于前台再翻页；配对窗口处于前台时不应翻页。浏览器进程允许不等于能判断任意网页正在演示。
6. 单独共享某个应用窗口可能看不到激光；整屏共享、HDR、独占全屏和提升权限的演示应用需分别验证。请勿把以管理员身份运行作为默认解决办法。

## 文档与交付边界

| 文档 | 内容 |
|---|---|
| [架构与实施](docs/architecture.md) | 工程边界、安全设计、V5 取舍及 Windows→Android 阶段门槛 |
| [网络协议](docs/protocol.md) | 服务端实际字段、握手和控制协议，Android 实现依据 |
| [验收矩阵](docs/acceptance.md) | 功能、安全、性能、真机和发布测试计划 |
| [本次验证结果](docs/validation.md) | 构建、自测、UI/原生检查证据及尚未验证的边界 |
| [Android 交接](docs/android-handoff.md) | 跨语言向量、原生 UI、状态及输入约束 |
| [来源与许可](docs/licenses.md) | 依赖许可、来源和正式发布待办 |
| [产品原始文档](docs/product/) | PRD、UI 设计 V5 与 HTML 原型 |

Windows 与 Android 真机联调、至少两类手机/电脑、安装升级、性能目标、屏幕共享兼容、代码签名及安全独立审查完成前，只分发开发验证版。正式项目许可证尚待仓库所有者决定；第三方许可不授予本项目额外授权。
