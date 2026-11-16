# 参与 Pointa

先在 Issue 中说明要解决的问题。小修复可以直接发 PR；较大的交互或协议变更，请先讨论，避免双方重复投入。

## 报告问题

请提供双端版本、手机型号/Android 版本、Windows 版本、演示软件、连接方式、复现步骤和错误原文。截图请遮住私人内容。不要提交配对码、二维码、信任文件或密钥。

## 提交修改

- 保持一次 PR 解决一个问题，说明之前/之后的行为和实际运行的检查。
- 提交说明采用 Conventional Commits，例如 `fix(android): preserve layout when closing the editor`。
- 修改协议时同步检查配套仓库；改变连接或存储逻辑时补回归用例。
- UI 改动请附中英文截图，检查小屏和深浅色模式。

构建步骤见 README 和 docs/development.md。提交代码意味着你有权按本项目 MIT 许可证贡献这些内容；不要引入来源不明的图标或代码。
