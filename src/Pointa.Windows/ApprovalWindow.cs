using Pointa.Core;
using W = System.Windows;

namespace Pointa.Windows;

internal sealed class ApprovalWindow : DesktopWindow
{
    private readonly TaskCompletionSource<bool> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<bool> Decision => decision.Task;

    public ApprovalWindow(AppSettings settings, PairingRequest request)
        : base(settings.T("映点 · 新手机请求配对", "Pointa · Pair a new phone"), 440)
    {
        string T(string zh, string en) => settings.T(zh, en);
        ResizeMode = W.ResizeMode.NoResize; ShowActivated = false;
        var body = Ui.Text(T("允许这台手机控制激光和演示翻页？\n仅在你正在连接自己的手机时允许。", "Allow this phone to point and change slides?\nOnly allow a phone you are pairing now."));
        body.TextWrapping = W.TextWrapping.Wrap; body.Margin = new W.Thickness(0, 16, 0, 14);
        var method = Ui.Text($"{request.Method} · {request.RemoteAddress}", 12, muted: true);
        method.Margin = new W.Thickness(0, 0, 0, 20);
        var reject = Ui.Button(T("拒绝", "Deny"), () => { decision.TrySetResult(false); Close(); });
        reject.IsCancel = true;
        var allow = Ui.Button(T("允许配对", "Allow pairing"), () => { decision.TrySetResult(true); Close(); }, true);
        Content = Ui.Body(Ui.Stack(Ui.Text(request.DeviceName, 21, true), body, method, Ui.Row(reject, allow, 184)));
        Closed += (_, _) => decision.TrySetResult(false);
    }
}
