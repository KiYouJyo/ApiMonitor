using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace ApiMonitor.Services;

/// <summary>
/// v0.6.0 布局回归修复：x:Uid 在本环境（Windows App SDK 2.3.1 + resw 键被
/// convertDotsToSlashes 转斜杠）不可用，改用附加属性 Loc.Key 实现声明式本地化。
///
/// 用法：
///   &lt;Button Loc.Key="Home.Refresh" ... /&gt;
///   &lt;TextBlock Loc.Key="Home.AccountOverview" ... /&gt;
///   &lt;TextBlock Loc.Key="Home.Subtitle" Loc.Args="v0.6.0" /&gt;
///
/// 附加属性在元素 Loaded 时按当前语言从 ResourceLoader（斜杠键）取文本，
/// 写入 TextBlock.Text / Button.Content 等通用属性；找不到资源时写入
/// "[Missing: 键名]" 占位，绝不静默生成空白控件。
/// </summary>
public static class Loc
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key",
        typeof(string),
        typeof(Loc),
        new PropertyMetadata(null, OnKeyChanged));

    public static readonly DependencyProperty ArgsProperty = DependencyProperty.RegisterAttached(
        "Args",
        typeof(object),
        typeof(Loc),
        new PropertyMetadata(null, OnArgsChanged));

    public static string GetKey(DependencyObject obj) => (string)obj.GetValue(KeyProperty);

    public static void SetKey(DependencyObject obj, string value) => obj.SetValue(KeyProperty, value);

    public static object GetArgs(DependencyObject obj) => obj.GetValue(ArgsProperty);

    public static void SetArgs(DependencyObject obj, object value) => obj.SetValue(ArgsProperty, value);


    private static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // 立即应用（不依赖 Loaded）：页面初始为 Collapsed 时 Loaded 不触发，
        // 但附加属性变更必然发生，保证所有页面文本就绪。
        Apply(d);
    }

    private static void OnArgsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        Apply(d);
    }

    private static void Apply(DependencyObject d)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        string? key = GetKey(element);
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        string resolved = Resolve(key);
        if (string.IsNullOrEmpty(resolved))
        {
            // 绝不静默生成空白：给出明显占位便于排查。
            resolved = $"[Missing: {key}]";
        }

        object? args = GetArgs(element);
        if (args is string[] argArray && argArray.Length > 0)
        {
            try
            {
                resolved = string.Format(resolved, argArray);
            }
            catch
            {
                // 保持原文本。
            }
        }

        SetContent(element, resolved);

        // ToggleSwitch 的 OnContent/OffContent：解析 <Key>OnContent / <Key>OffContent 键。
        if (element is ToggleSwitch toggleSwitch)
        {
            string? toggleKey = GetKey(element);
            if (!string.IsNullOrEmpty(toggleKey))
            {
                string? on = TryResolveValue(toggleKey + "OnContent");
                if (!string.IsNullOrEmpty(on))
                {
                    toggleSwitch.OnContent = on;
                }

                string? off = TryResolveValue(toggleKey + "OffContent");
                if (!string.IsNullOrEmpty(off))
                {
                    toggleSwitch.OffContent = off;
                }
            }
        }
    }

    private static string? TryResolveValue(string key)
    {
        string resolved = Resolve(key);
        return resolved.StartsWith("[Missing:", StringComparison.Ordinal) ? null : resolved;
    }

    /// <summary>
    /// 委托 L10n 解析（共享 ResourceContext 语言限定 + 多属性后缀 + 斜杠规范化）。
    /// </summary>
    private static string Resolve(string key)
    {
        // L10n 找不到时返回 "[Missing: key]"，调用方据此判断。
        return L10n.Get(key);
    }

    private static void SetContent(FrameworkElement element, string text)
    {
        try
        {
            switch (element)
            {
                case TextBlock tb:
                    tb.Text = text;
                    break;
                case Button button:
                    // 已有显式 Content（如图标+文字的复合内容）不覆盖。
                    if (button.Content is null)
                    {
                        button.Content = text;
                    }

                    break;
                case ToggleButton toggle:
                    if (toggle.Content is null)
                    {
                        toggle.Content = text;
                    }

                    break;
                case ToggleSwitch toggleSwitch:
                    toggleSwitch.Header = text;
                    break;
                case ComboBox comboBox:
                    comboBox.Header = text;
                    break;
                case RadioButtons radioButtons:
                    radioButtons.Header = text;
                    break;
                case NavigationViewItem navItem:
                    if (navItem.Content is null)
                    {
                        navItem.Content = text;
                    }

                    break;
                case HyperlinkButton hyperlink:
                    if (hyperlink.Content is null)
                    {
                        hyperlink.Content = text;
                    }

                    break;
                case ContentControl contentControl when contentControl is not Button:
                    if (contentControl.Content is null)
                    {
                        contentControl.Content = text;
                    }

                    break;
                case InfoBar infoBar:
                    infoBar.Title = text;
                    break;
            }
        }
        catch
        {
            // 本地化失败不影响布局。
        }
    }
}
