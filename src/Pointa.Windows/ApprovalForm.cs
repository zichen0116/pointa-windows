using Pointa.Core;

namespace Pointa.Windows;

internal sealed class ApprovalForm : Form
{
    private readonly TaskCompletionSource<bool> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<bool> Decision => decision.Task;

    public ApprovalForm(AppSettings settings, PairingRequest request)
    {
        string T(string zh, string en) => settings.T(zh, en);
        Ui.Configure(this, T("映点 · 新手机请求配对", "Pointa · Pair a new phone"), new Size(390, 225));
        MinimizeBox = false;
        var title = Ui.Label(request.DeviceName, 14, true);
        title.SetBounds(22, 20, 346, 40);
        var body = Ui.Label(T("允许这台手机控制激光和演示翻页？\n仅在你正在连接自己的手机时允许。", "Allow this phone to point and change slides?\nOnly allow a phone you are pairing now."), 9);
        body.SetBounds(22, 66, 346, 54);
        var method = Ui.Label($"{request.Method} · {request.RemoteAddress}", 8);
        method.ForeColor = Ui.Muted;
        method.SetBounds(22, 127, 346, 25);
        var reject = Ui.Button(T("拒绝", "Deny"), (_, _) => { decision.TrySetResult(false); Close(); });
        reject.SetBounds(22, 172, 164, 34);
        var allow = Ui.Button(T("允许配对", "Allow pairing"), (_, _) => { decision.TrySetResult(true); Close(); }, primary: true);
        allow.SetBounds(204, 172, 164, 34);
        Controls.AddRange([title, body, method, reject, allow]);
        CancelButton = reject;
        FormClosed += (_, _) => { decision.TrySetResult(false); Dispose(); };
        Ui.Finish(this);
    }
    protected override bool ShowWithoutActivation => true;
}
