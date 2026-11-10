# Windows 0.1.2 视觉与设置精简

日期：2026-11-10。按本次用户反馈调整，不修改原始产品文档。

- 使用浅灰底、白色圆角卡片、深色主按钮、清晰字号与间距，保留原生窗口行为和键盘焦点提示。
- 主窗移除底栏及“局域网 · 加密连接”文案；“刷新配对码”进入覆盖层状态卡片右侧，下方仅保留设置与断开连接。
- 设置包含语言、登录自动启动并收进托盘、防火墙以及已授权设备。设备逐项移除／清空授权仍需确认，默认选“否”，保留现有撤销协议。
- 删除独立设备管理窗口和托盘重复入口。删除显示器选择、桌面激光外观、高级信息和复制诊断控件；手机端继续设置激光参数。
- 保存通用偏好从最新配置复制，避免覆盖设置窗口打开期间由手机修改的激光参数。已有授权、身份及配置文件保持兼容。

## 图标资产

使用 Codex 内置 `image_gen` 生成透明 PNG，没有调用 CLI/API 备用路径。最终原图位于 `src/Pointa.Windows/Assets/Pointa.png`，多尺寸 ICO 位于 `src/Pointa.Windows/Assets/Pointa.ico`。程序资源、窗口、任务栏、托盘使用同一图标；主窗也显示相同标记。

`scripts/update-icon.ps1` 只进行缩放与 ICO 封装，保留透明通道，包含 16、20、24、32、40、48、64、128、256 px；验证标准尺寸解码和 256 px PNG 帧。可运行脚本从 PNG 重新生成 ICO。

生成时的完整提示词：

```text
Use case: logo-brand. Create a single polished Windows desktop application icon for Pointa (映点), a phone-controlled presentation laser pointer. This is the final app icon asset, not a mockup, not an icon sheet. Square 1024x1024 composition, centered mark. A deep midnight navy rounded-square/squircle tile with a confident simple sculptural coral-red ribbon forming an abstract lowercase p / directed light loop, finishing in a bright coral focus dot. Use Pointa's existing brand coral #F87581 and ink #172236, with restrained warm-white highlights. The silhouette must be bold and unmistakable at 16x16, 32x32 and 48x48: one thick geometric loop, one clear dot, generous negative space. Contemporary premium Windows 11 / Fluent-inspired material finish, very subtle depth and light, precise geometry, calm and professional. No lettering, no words, no tiny lines, no crosshairs, no concentric target rings, no sparkle stars, no lens flare, no glossy plastic, no large cast shadow, no surrounding scene. Rounded navy tile occupies about 88 percent of canvas; truly transparent alpha outside the tile. Keep its front face straight-on and square to the viewer, no perspective. Output only the icon.
```

工具实际返回 1280×1280 PNG，保留原始输出分辨率；窗口图标尺寸由 ICO 封装决定。
