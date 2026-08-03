using ApiMonitor.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ApiMonitor.Views;

/// <summary>
/// 完整“关于”页：产品信息、当前能力、隐私摘要、项目链接、
/// 本地文档（离线查看）、手动检查更新、复制诊断信息、本地数据入口。
/// 版本与 Provider 列表来自统一服务，不在 XAML 硬编码。
/// </summary>
public sealed partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
    }

    public MainViewModel? ViewModel
    {
        get => (MainViewModel?)DataContext;
        set
        {
            DataContext = value;
            if (value?.About is { } about)
            {
                DataContext = about;
            }
        }
    }

    /// <summary>本地文档查看（RichTextBlock + ScrollViewer 的简单对话框，不引入 WebView2/Markdown 框架）。</summary>
    private async void OnLocalDocClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tag)
        {
            return;
        }

        string title;
        string? filePath = null;
        switch (tag)
        {
            case "privacy":
                title = "隐私政策";
                filePath = ResolveDocPath("PRIVACY.md");
                break;
            case "license":
                title = "MIT License";
                filePath = ResolveDocPath("LICENSE");
                break;
            default:
                title = "第三方声明";
                filePath = ResolveDocPath("THIRD-PARTY-NOTICES.md");
                break;
        }

        string content = string.Empty;
        if (filePath is not null && System.IO.File.Exists(filePath))
        {
            try
            {
                content = System.IO.File.ReadAllText(filePath);
            }
            catch
            {
                content = "无法读取本地文档。";
            }
        }
        else
        {
            content = "本地文档未找到。";
        }

        var textBlock = new TextBlock
        {
            Text = content,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        var scrollViewer = new ScrollViewer
        {
            Content = textBlock,
            MaxHeight = 400,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = scrollViewer,
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
        };

        if (XamlRoot is not null)
        {
            dialog.XamlRoot = XamlRoot;
            try
            {
                await dialog.ShowAsync();
            }
            catch
            {
                // 对话框显示失败不影响应用。
            }
        }
    }

    private static string? ResolveDocPath(string fileName)
    {
        // 打包运行时文档随包分发（AppContext.BaseDirectory 下的内容）；
        // 未打包调试时回退到工作目录。
        string baseDir = AppContext.BaseDirectory;
        string packaged = System.IO.Path.Combine(baseDir, fileName);
        if (System.IO.File.Exists(packaged))
        {
            return packaged;
        }

        string working = System.IO.Path.Combine(Environment.CurrentDirectory, fileName);
        if (System.IO.File.Exists(working))
        {
            return working;
        }

        // 仓库根（开发时直接运行源码目录）。
        string repo = System.IO.Path.Combine(
            Environment.CurrentDirectory,
            "..",
            fileName);
        string repoFull = System.IO.Path.GetFullPath(repo);
        return System.IO.File.Exists(repoFull) ? repoFull : null;
    }
}
