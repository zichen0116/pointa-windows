# 构建与开发

用户介绍见 [README](../README.md)。本页保留构建、自测与开发分发方法；产物版本以项目配置为准。

## 构建与自测

0.1.4 对齐参考网页的贝塞尔曲线、沿路径变宽/增亮、停留渐隐和光点阴影；标准参数为光点直径 12 DIP、拖尾 500 ms、曲线张力 0.3。激光颜色由 Android 0.1.2 的主题决定，无单独颜色选项。原项目函数生成的几何基准与真实浏览器逐像素对比方法见 [参考算法对齐](reference-alignment.md)。

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

