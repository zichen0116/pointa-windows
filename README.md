# 映点 Pointa · Windows

手机演示翻页笔的 Windows 接收端：在独立透明覆盖层显示激光和拖尾，并向前台演示应用发送上一页/下一页。激光不移动系统鼠标，不抢前台焦点。产品受 [lan-laser-pen](https://github.com/fuxing0910-hue/lan-laser-pen) 启发，按本项目 PRD 与 V5 原型独立实现。

**当前是开发验证版。** 先完成 Windows，再在 [pointa-android](https://github.com/zichen0116/pointa-android) 实施 Android。构建、自测及 UI smoke 只证明其覆盖范围；真实手机扫码、双端操作、投影/多屏硬件、延迟、安装升级和安全外审均需各自验收，不能据此宣称产品已成熟。

## 功能与技术

- WinForms 配对小窗、系统托盘、中英文设置、设备授权/撤销、目标屏选择与紧急隐藏。
- 独立原生 layered window 激光、柔光和拖尾，鼠标穿透；前台应用保护及离散翻页去重。
- Kestrel TLS WebSocket；QR 证书指纹绑定和短效令牌；六位码 SRP-6a、证书绑定及 PC 批准。
- 当前用户 DPAPI 保护本地信任数据；单活跃控制端、断线清理、UDP 发现。
- C# / .NET 10；Windows 11 x64 优先，Windows 10 x64 兼容性待实测。无云账号、无公网中转、无默认遥测。

本应用层 SRP 集成尚未外部安全审计。UDP 发现只是定位线索；它不能替代证书绑定和设备认证。mDNS 尚未实现。未支持系统鼠标点击/拖动、文件传输或远程画面流。

## 构建与自测

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
2. Windows 默认不在 Public 网络接口提供控制服务。设置里的公共网络选项需要用户明确启用；应用不会替用户改变系统网络类别。仅在信任当前网络时开启，使用后可关闭。
3. 默认控制端口为 TCP `32145`，发现端口为 UDP `32146`。如果 Windows 防火墙阻止入站，用户可在系统提示中仅允许信任的私有网络，或请管理员建立限定程序、端口和本地子网的规则。安装器不会默认开放公共网络或创建规则。
4. UDP 发现不可用时，Android 后续支持手动 IP；扫码/手动 IP 都无法绕过访客网络隔离。可换可互访网络或受信任热点。
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
