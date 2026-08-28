using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace ApiMonitor.Services;

/// <summary>
/// 声明式本地化附加属性。除主要文本外，同时覆盖 Placeholder、ToolTip、
/// AutomationProperties.Name 与 ToggleSwitch 的 On/Off 文本。
/// </summary>
public static class Loc
{
    public static readonly DependencyProperty KeyProperty = Register("Key", OnAnyKeyChanged);
    public static readonly DependencyProperty ArgsProperty = DependencyProperty.RegisterAttached(
        "Args", typeof(object), typeof(Loc), new PropertyMetadata(null, OnAnyKeyChanged));
    public static readonly DependencyProperty PlaceholderKeyProperty = Register("PlaceholderKey", OnAnyKeyChanged);
    public static readonly DependencyProperty ToolTipKeyProperty = Register("ToolTipKey", OnAnyKeyChanged);
    public static readonly DependencyProperty AutomationNameKeyProperty = Register("AutomationNameKey", OnAnyKeyChanged);
    public static readonly DependencyProperty OnContentKeyProperty = Register("OnContentKey", OnAnyKeyChanged);
    public static readonly DependencyProperty OffContentKeyProperty = Register("OffContentKey", OnAnyKeyChanged);

    private static DependencyProperty Register(string name, PropertyChangedCallback callback) =>
        DependencyProperty.RegisterAttached(name, typeof(string), typeof(Loc), new PropertyMetadata(null, callback));

    public static string GetKey(DependencyObject obj) => obj.GetValue(KeyProperty) as string ?? string.Empty;
    public static void SetKey(DependencyObject obj, string value) => obj.SetValue(KeyProperty, value);
    public static object? GetArgs(DependencyObject obj) => obj.GetValue(ArgsProperty);
    public static void SetArgs(DependencyObject obj, object value) => obj.SetValue(ArgsProperty, value);
    public static string GetPlaceholderKey(DependencyObject obj) => obj.GetValue(PlaceholderKeyProperty) as string ?? string.Empty;
    public static void SetPlaceholderKey(DependencyObject obj, string value) => obj.SetValue(PlaceholderKeyProperty, value);
    public static string GetToolTipKey(DependencyObject obj) => obj.GetValue(ToolTipKeyProperty) as string ?? string.Empty;
    public static void SetToolTipKey(DependencyObject obj, string value) => obj.SetValue(ToolTipKeyProperty, value);
    public static string GetAutomationNameKey(DependencyObject obj) => obj.GetValue(AutomationNameKeyProperty) as string ?? string.Empty;
    public static void SetAutomationNameKey(DependencyObject obj, string value) => obj.SetValue(AutomationNameKeyProperty, value);
    public static string GetOnContentKey(DependencyObject obj) => obj.GetValue(OnContentKeyProperty) as string ?? string.Empty;
    public static void SetOnContentKey(DependencyObject obj, string value) => obj.SetValue(OnContentKeyProperty, value);
    public static string GetOffContentKey(DependencyObject obj) => obj.GetValue(OffContentKeyProperty) as string ?? string.Empty;
    public static void SetOffContentKey(DependencyObject obj, string value) => obj.SetValue(OffContentKeyProperty, value);

    private static void OnAnyKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => Apply(d);

    private static void Apply(DependencyObject d)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        string key = GetKey(element);
        if (!string.IsNullOrWhiteSpace(key))
        {
            string text = FormatIfNeeded(L10n.Get(key), GetArgs(element));
            SetPrimaryContent(element, text);
        }

        ApplyPlaceholder(element, GetPlaceholderKey(element));
        ApplyToolTip(element, GetToolTipKey(element));
        ApplyAutomationName(element, GetAutomationNameKey(element));

        if (element is ToggleSwitch toggle)
        {
            string onKey = GetOnContentKey(element);
            string offKey = GetOffContentKey(element);
            if (string.IsNullOrWhiteSpace(onKey) && !string.IsNullOrWhiteSpace(key))
            {
                onKey = key + ".OnContent";
            }
            if (string.IsNullOrWhiteSpace(offKey) && !string.IsNullOrWhiteSpace(key))
            {
                offKey = key + ".OffContent";
            }

            SetIfResolved(onKey, value => toggle.OnContent = value);
            SetIfResolved(offKey, value => toggle.OffContent = value);
        }
    }

    private static string FormatIfNeeded(string value, object? args)
    {
        if (args is not string[] array || array.Length == 0)
        {
            return value;
        }

        try
        {
            return string.Format(value, array.Cast<object>().ToArray());
        }
        catch (FormatException)
        {
            return value;
        }
    }

    private static void ApplyPlaceholder(FrameworkElement element, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        SetIfResolved(key, value =>
        {
            switch (element)
            {
                case TextBox textBox: textBox.PlaceholderText = value; break;
                case PasswordBox passwordBox: passwordBox.PlaceholderText = value; break;
                case AutoSuggestBox suggestBox: suggestBox.PlaceholderText = value; break;
            }
        });
    }

    private static void ApplyToolTip(FrameworkElement element, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        SetIfResolved(key, value => ToolTipService.SetToolTip(element, value));
    }

    private static void ApplyAutomationName(FrameworkElement element, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        SetIfResolved(key, value => AutomationProperties.SetName(element, value));
    }

    private static void SetIfResolved(string key, Action<string> setter)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        string value = L10n.Get(key);
        if (!value.StartsWith("[Missing:", StringComparison.Ordinal))
        {
            setter(value);
        }
    }

    private static void SetPrimaryContent(FrameworkElement element, string text)
    {
        try
        {
            switch (element)
            {
                case TextBlock tb: tb.Text = text; break;
                case TextBox textBox: textBox.Header = text; break;
                case PasswordBox passwordBox: passwordBox.Header = text; break;
                case NumberBox numberBox: numberBox.Header = text; break;
                case Button button when button.Content is null: button.Content = text; break;
                case ToggleButton toggle when toggle.Content is null: toggle.Content = text; break;
                case ToggleSwitch toggleSwitch: toggleSwitch.Header = text; break;
                case ComboBox comboBox: comboBox.Header = text; break;
                case RadioButtons radioButtons: radioButtons.Header = text; break;
                case NavigationViewItem navItem when navItem.Content is null: navItem.Content = text; break;
                case HyperlinkButton hyperlink when hyperlink.Content is null: hyperlink.Content = text; break;
                case InfoBar infoBar: infoBar.Title = text; break;
                case ContentDialog dialog: dialog.Title = text; break;
                case ContentControl contentControl when contentControl.Content is null: contentControl.Content = text; break;
            }
        }
        catch
        {
            // 文案设置失败不影响应用生命周期；完整性测试负责提前发现资源问题。
        }
    }
}
