using Pointa.Core;
using Pointa.Server;
using System.Windows.Controls.Primitives;
using W = System.Windows;
using C = System.Windows.Controls;

namespace Pointa.Windows;

internal static class UiSelfCheck
{
    public static async Task RunAsync(HostService host, TrayMenu menu, string directory)
    {
        try
        {
            foreach (var (mode, label) in new[] { ("Dark", "dark"), ("Light", "light") })
            {
                Ui.UseTheme(mode);
                foreach (var language in new[] { "zh", "en" })
                {
                    var settings = new AppSettings { Language = language };
                    using var window = new PairingWindow(settings);
                    window.Show();
                    var pairing = host.Pairing;
                    window.UpdatePairing(pairing.QrPayload, pairing.Code, pairing.ExpiresAt, true, null, pairing.Host);
                    await Task.Delay(200);
                    Check(window.Width == 416 && window.MinWidth == 416, "Main window is twenty percent narrower");
                    var ip = window.AddressButton.TranslatePoint(new W.Point(), window);
                    var qr = window.QrImage.TranslatePoint(new W.Point(), window);
                    Check(ip.X >= window.ActualWidth * .45 && window.AddressButton.ActualWidth >= 176 &&
                        window.AddressButton.ActualHeight >= 44 && ip.Y < qr.Y, "Large IP action fills the upper-right header");
                    var refresh = window.RefreshButton.TranslatePoint(new W.Point(), window);
                    var buttons = window.SettingsButton.TranslatePoint(new W.Point(), window);
                    Check(refresh.X + window.RefreshButton.ActualWidth / 2 > window.ActualWidth / 2 &&
                        refresh.Y + window.RefreshButton.ActualHeight < buttons.Y, "Refresh placement in the code column");
                    var refreshCount = 0;
                    window.RefreshRequested += (_, _) => refreshCount++;
                    window.RefreshButton.RaiseEvent(new W.RoutedEventArgs(C.Button.ClickEvent));
                    Check(refreshCount == 1, "Refresh action");
                    Check(window.ResizeMode == W.ResizeMode.CanResize && window.ShowInTaskbar, "Standard taskbar and resize behavior");
                    window.WindowState = W.WindowState.Minimized;
                    await Task.Delay(100);
                    Check(window.IsVisible && window.WindowState == W.WindowState.Minimized, "Minimize keeps the window in the taskbar");
                    window.WindowState = W.WindowState.Maximized;
                    await Task.Delay(100);
                    Check(window.WindowState == W.WindowState.Maximized, "Maximize works");
                    window.WindowState = W.WindowState.Normal;
                    window.SavePreview(Path.Combine(directory, $"pairing-{label}-{language}.png"));
                    window.UpdatePairing(pairing.QrPayload, pairing.Code, pairing.ExpiresAt, false, null);
                    window.SavePreview(Path.Combine(directory, $"offline-{label}-{language}.png"));
                    window.UpdatePairing(pairing.QrPayload, pairing.Code, pairing.ExpiresAt, true, null, pairing.Host);

                    string? saved = null; bool? startup = null; bool? allowApps = null;
                    using var options = new SettingsWindow(settings, () => host, (value, start, apps) => { saved = value; startup = start; allowApps = apps; });
                    options.Show();
                    options.RenderDevices([new PairedDevice("preview", "演示手机 · Presentation phone", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)], "preview");
                    options.Startup.IsChecked = true;
                    Check(options.AllowPresentationApps.IsChecked == true, "Presentation apps allowed by default");
                    options.AllowPresentationApps.IsChecked = false;
                    await Task.Delay(200);
                    Check(options.LanguageSelector.Template is not null && options.Startup.Template is not null &&
                        options.TryFindResource("DefaultComboBoxStyle") is W.Style && options.TryFindResource("DefaultCheckBoxStyle") is W.Style,
                        "Official Fluent control templates loaded");
                    Ui.SavePreview((W.FrameworkElement)options.Content, Path.Combine(directory, $"settings-{label}-{language}.png"));
                    options.LanguageSelector.IsDropDownOpen = true;
                    await Task.Delay(200);
                    var popup = options.LanguageSelector.Template!.FindName("PART_Popup", options.LanguageSelector) as Popup;
                    Check(popup?.IsOpen == true && popup.Child is W.FrameworkElement, "Language popup opens");
                    Ui.SavePreview((W.FrameworkElement)popup!.Child, Path.Combine(directory, $"language-{label}-{language}.png"));
                    options.LanguageSelector.IsDropDownOpen = false;
                    options.LanguageSelector.SelectedIndex = language == "zh" ? 1 : 0;
                    options.SaveButton.RaiseEvent(new W.RoutedEventArgs(C.Button.ClickEvent));
                    Check(options.Saved && saved == (language == "zh" ? "en" : "zh") && startup == true && allowApps == false, "Language/startup/control save callback");

                    using (var approval = new ApprovalWindow(settings, new PairingRequest("演示手机 · Presentation phone", "127.0.0.1", "QR")))
                    {
                        approval.Show();
                        await Task.Delay(100);
                        Check(!approval.ShowActivated, "Pairing request does not steal focus");
                        Ui.SavePreview((W.FrameworkElement)approval.Content, Path.Combine(directory, $"approval-{label}-{language}.png"));
                        approval.Close();
                        Check(!await approval.Decision, "Closing approval denies pairing");
                    }

                    menu.PlacementTarget = (W.UIElement)window.Content;
                    menu.Placement = PlacementMode.Center;
                    menu.IsOpen = true;
                    await Task.Delay(250);
                    Check(menu.Template is not null && menu.TryFindResource("DefaultContextMenuStyle") is W.Style, "Fluent tray popup loaded");
                    Check(menu.Template!.FindName("Border", menu) is C.Border border && border.CornerRadius.TopLeft == 8, "Fluent rounded menu template applied");
                    Ui.SavePreview(menu, Path.Combine(directory, $"tray-{label}-{language}.png"));
                    menu.IsOpen = false;
                }
            }
        }
        finally { menu.IsOpen = false; menu.PlacementTarget = null; menu.Placement = PlacementMode.MousePoint; Ui.UseTheme("System"); }
    }
    private static void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException("UI self-check failed: " + message); }
}
