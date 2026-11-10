# Windows 0.1.3：统一 Fluent 控件

日期：2026-11-11。依据用户提供的 Clash 托盘菜单及 Pointa 旧控件截图进行修正。

问题并非 C# 不能制作现代界面。0.1.2 仍使用 WinForms 默认 ContextMenuStrip、Flat ComboBox/CheckBox，并且窗口颜色固定为浅色，导致菜单、下拉展开、勾选和主窗风格分离。

## 实现

- 配对、设置和配对批准改为 WPF Window，移除原有 Form UI 与手绘按钮；保留 0.1.2 已确认的信息结构、刷新按钮位置和设置内容。
- 使用 .NET 10 自带的 `PresentationFramework.Fluent` 资源字典。默认 `Fluent.xaml` 跟随系统明暗与强调色；未自行复制或重写 ComboBox、CheckBox、Button、MenuItem 模板，也没有引入第三方 UI 包。
- 托盘图标仍由 NotifyIcon 提供，右键弹出 WPF Fluent ContextMenu；弹出窗口取得前台焦点，使用标准鼠标/键盘菜单行为。派生菜单显式引用 ContextMenu 的官方样式，防止隐式样式查找回退为旧菜单。
- 设置语言下拉列表、启动复选框、按钮、悬停/按下/禁用/焦点状态共享同一套 Fluent 模板。颜色使用动态资源；二维码保留白色扫码区，离线占位使用当前主题背景。
- WPF Application/Dispatcher 承担消息循环，官方 WinForms 互操作支持保留的托盘与覆盖层。单实例、后台启动、关窗收进托盘、网络事件、原生翻页和授权逻辑继续复用。
- 设置窗口只允许一个实例；取消不保存，关闭配对批准窗等同拒绝。已有设置、DPAPI 身份、设备授权及 Android 协议没有迁移格式。
- 发布仍为自包含程序，打包检查增加 Fluent 与 WPF 核心程序集，用户无需另装 UI 运行时。

## 依据与边界

[Microsoft WPF 主题文档](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/styles-templates-overview) 与 [.NET 10 WPF 更新说明](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100) 描述了该官方 Windows 11 风格主题；WPF Fluent 不等同于 WinUI 3。实现使用资源字典入口，不调用仍带实验诊断的 ThemeMode API。

自动化覆盖显式深浅色、中英文窗口、下拉展开、官方模板、菜单圆角、勾选状态、刷新/保存回调和关闭批准拒绝。没有为验证而修改用户的 Windows 主题、启动项、防火墙或设备授权。Windows 10、混合 DPI 和完整屏幕阅读器验收仍需各自实测。
