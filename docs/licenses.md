# 映点 Pointa：来源、依赖与许可记录

更新：2026-11-12。状态：开发期来源台账，正式发布前需按实际锁定依赖和分发文件复核。此文件不是项目正式开源许可证，也不替代第三方许可原文。

## 1. 源码来源原则

Pointa 根据本项目 PRD、UI 设计 V5 和后续用户反馈实现 Windows 应用。0.1.4 移植 `lan-laser-pen` 的网页拖尾算法；没有复制 RemotePointer、gInk 或 ppInk 的源码。

| 来源 | 使用方式 | 许可处理 |
|---|---|---|
| 本项目 PRD/UI/HTML | 用户提供的需求、视觉和交互依据，归档 `docs/product/` | 保留来源文件；不把用户文档擅自标成第三方授权素材 |
| [lan-laser-pen](https://github.com/fuxing0910-hue/lan-laser-pen/tree/1e643ed5cb9c93056ed9a9b2dce4ecd3765ad67b) | `src/index.ts` 与 `example/main.ts` 的网页曲线/光点效果移植至 C#/Skia；测试夹具调用原函数；见 [修改及对齐说明](reference-alignment.md) | 已核实该提交 MIT；保留 `Copyright (c) 2021 SilentTiger<cia.fbi.007@hotmail.com>` 及 [完整许可](third-party/lan-laser-pen-LICENSE.txt) |
| [RemotePointer Android](https://github.com/schorschii/RemotePointer-Android) / [Server](https://github.com/schorschii/RemotePointer-Server) | 仅了解相关产品交互，不复制源码 | PRD 标记 GPL-3.0；不能在未经明确许可决策时混入代码 |
| [gInk](https://github.com/geovens/gInk) / [ppInk](https://github.com/pubpub-zz/ppInk) | 仅作为 PRD 参考资料，不作为当前依赖 | 如未来引入，先逐仓库、逐依赖核验许可 |
| Microsoft Win32/.NET 文档 | 使用平台公开 API 的技术依据 | 平台 API 的使用与复制示例源码分开记录 |

如后续引入第三方代码片段，记录来源 URL、提交、路径、改动和许可，并把必要声明随源码及二进制一起分发；不能仅在 README 写一句致谢替代许可义务。

## 2. 依赖台账

版本以工程 PackageReference、还原资产及最终发布产物为准。下表只记录本阶段已确定的技术依赖；未确定的包不预先声称已使用或已审查。

| 组件 | 用途 | 版本依据 | 许可与发布动作 |
|---|---|---|---|
| .NET 10 / Windows Desktop / WinForms | 运行时及桌面 UI | 工程 TargetFramework、发布 RID、SDK/runtime 清单 | 自包含发布时保留运行时自带第三方声明，记录实际随包 runtime；参考 [.NET 源码许可](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) |
| ASP.NET Core / Kestrel | HTTPS/WSS 局域网服务 | `Microsoft.AspNetCore.App` framework reference 与实际 runtime | 按发布模式检查分发内容，保留对应声明；参考 [ASP.NET Core 许可](https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt) |
| Bouncy Castle C# | SRP-6a 密码学原语 | BouncyCastle.Cryptography 2.7.0 | MIT；已保存包内 [完整许可](third-party/BouncyCastle-LICENSE.md) |
| QRCoder | 生成真实配对 QR | 1.7.0；包元数据对应提交 `2dc55ee03cb9abf66ffed7d683cbcce0a141b124` | MIT；已保存该提交的 [完整许可](third-party/QRCoder-LICENSE.txt) |
| SkiaSharp / NativeAssets.Win32 | 与浏览器 Canvas 同类的 Skia 曲线、抗锯齿和阴影渲染 | 4.155.0 | 包内 [MIT 许可](third-party/SkiaSharp-LICENSE.txt) 与 [Skia 第三方声明](third-party/SkiaSharp-THIRD-PARTY-NOTICES.txt) 随包分发 |
| bezier-js | 原项目基准生成依赖；C# 独立实现三次多项式和 24 点 Gauss 积分以匹配其长度/LUT 方法 | 5.1.0；仅 `.references` 下的测试工具使用 | 包声明 MIT；JS 不进入生产二进制，原函数和版本记录在 [对齐说明](reference-alignment.md) |
| Inno Setup | Setup.exe 安装/卸载 | 本机编译器 6.7.4，官方安装程序签名有效 | 构建工具，不随产品分发；商业使用条款按 [官方说明](https://jrsoftware.org/isinfo.php) 另行核验 |

Bouncy Castle 的算法库来源与 Pointa 自行集成的配对协议分别评估。MIT 许可不代表本应用的密码学集成已经安全审计或通过任何认证。[Bouncy Castle 官方许可说明](https://www.bouncycastle.org/about/license/)

## 3. 发布前清单

- [ ] 从实际还原结果列出直接及传递依赖，记录包名、精确版本、源、许可证和版权声明。
- [ ] 对自包含 .NET/ASP.NET Core/Windows Desktop 文件核对随包许可，不在裁剪时意外删去必需声明。
- [ ] 确认二维码、图标、字体和安装器的实际来源；系统字体只引用，不擅自再分发字体文件。
- [ ] 将必须分发的许可原文加入安装包及可浏览的第三方声明入口。
- [ ] 按锁定版本检查已知安全通告并记录检查日期；不能只按最新网页版本推断当前依赖安全。
- [ ] 正式项目 LICENSE 由仓库所有者决定；未决定前不擅自宣布 MIT/GPL 或其他授权。
- [ ] Android 工程建立自己的依赖台账，覆盖 Compose、CameraX、扫码、网络、密码学及其传递依赖。

2026-11-05 执行 NuGet `--vulnerable --include-transitive`，当次源未报告已知漏洞。此结果不涵盖后续新增依赖，也不代替代码及协议审计。发布脚本包含本文、`docs/third-party` 中全部许可和 SDK 自带的 .NET LICENSE/ThirdPartyNotices；只排除 Skia 原生调试符号，不删除其运行库或声明。

未勾选的发布清单仍需最终分发审查。后续实际复用、增加依赖或改变分发模式时更新本文件，不覆盖已有来源记录。
