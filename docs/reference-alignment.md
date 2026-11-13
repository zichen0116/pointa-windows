# 网页贝塞尔拖尾对齐（0.1.4）

按用户确认，参考的是 `lan-laser-pen` 网页演示中从细到粗、尾部收尖的贝塞尔拖尾，不是其 `scripts/overlay.ps1` 的直线版本。

## 来源与移植

- 本地参考：同级工作区 `.references/lan-laser-pen`，固定提交 [`1e643ed5cb9c93056ed9a9b2dce4ecd3765ad67b`](https://github.com/fuxing0910-hue/lan-laser-pen/tree/1e643ed5cb9c93056ed9a9b2dce4ecd3765ad67b)。
- 曲线：[`src/index.ts`](https://github.com/fuxing0910-hue/lan-laser-pen/blob/1e643ed5cb9c93056ed9a9b2dce4ecd3765ad67b/src/index.ts)。光点、两点线段及时间渐隐：同提交 `example/main.ts`。
- C# 移植：`Native/BezierTrail.cs`；原生接入：`Native/LaserOverlay.cs`。MIT 原文随源码与分发包保留在 `docs/third-party/lan-laser-pen-LICENSE.txt`。
- 渲染采用 SkiaSharp 4.155.0，直接绘入 Win32 DIB，替换原来叠加的直线、光晕和白色中心。仍保持透明穿透、无激活、独立于系统鼠标。

| 网页规则 | 本轮实现 |
| --- | --- |
| 历史点清理 | 500 ms 生命周期；去掉相邻重复点与 A-B-A 回折 |
| 贝塞尔控制点 | 邻点切线、张力 0.3、两端中点 |
| 弧长与细分 | 与 bezier-js 5.1.0 相同的 24 点 Gauss 积分方法；总长度/50 为目标步长，保持原 LUT 取点数量 |
| 收尖与增亮 | 按累计曲线长度/原总长增加宽度及透明度；标准宽度 0–8 DIP |
| 分段接缝 | butt 端帽与原实现的微量端点延伸；两点时使用 5 DIP 圆端线段 |
| 时间淡出 | 最新点年龄决定整体透明度，不叠加额外位置低通 |
| 光点 | 标准半径 6 DIP，同色阴影；Canvas shadowBlur 12 对应 Skia sigma 6 物理像素 |

产品适配保留手机的小/中/大直径 8/12/20 DIP、拖尾开关与 100–1200 ms 时长；标准平滑为张力 0.3，低/高为 0.15/0.45。颜色跟随手机主题。参考对比使用标准参数，改动这些设置会有意改变效果。抬手隐藏光点并消尾；断连、撤销、紧急隐藏立即清除，消尾结束停表。最多保留 180 个输入点。

## 可复现验证

Node 22.23.3 的类型擦除功能直接装载参考 TypeScript，不另写一个 JS“近似参考”。`.references` 安装 `bezier-js@5.1.0`，仅供测试生成；该包不进入 Windows 运行时。C# 使用独立多项式实现及通用 Gauss 积分常数。

从 Windows 仓库执行：

```powershell
node scripts/generate-reference-fixtures.mjs
node scripts/render-browser-reference.mjs
# 随后执行 README 中的 --smoke-test，输出目录放在 artifacts 的直接子目录。
```

生成器同时写入 Windows 几何夹具和 Android 姿态夹具。浏览器脚本使用独立临时配置的 headless Edge、仅绑定 127.0.0.1 的临时服务器；完成后退出，不操作用户浏览器。需要同级克隆和 Node 测试依赖；常规构建及几何自检直接使用已提交夹具。

2026-11-11/12：12 个曲线、直线、回折、两点及不同时间样本，共 **3,606 项几何/宽度/透明度/渐隐断言通过**（误差不超过 1e-5）。真实 Edge 157.0.4341.42 Canvas 与 Skia 的 640×360、1× 光栅结果逐像素对比，Alpha 最大差 **1/255**，单独光点一致。该结果支持标准参数视觉对齐，不宣称跨 DPI、不同显卡和任意设置都字节完全一致。

证据：`artifacts/browser-reference/`、`artifacts/v0.1.4-ui-smoke-user/trail-reference-result.txt`、`trail-raster-result.json`、`comparison-*.png`（左为原网页、右为 Pointa）。真实手机延迟、不同 DPI/投影与持续运行仍需硬件测试。

配套 Android 的方向基底、80 ms 低通与 0.1° 死区对齐说明见其 `docs/reference-alignment.md`。
