namespace ApiBalanceMonitor.ViewModels;

/// <summary>与 WinUI 控件无关的状态级别，由视图层转换为 InfoBarSeverity。</summary>
public enum StatusSeverity
{
    Informational,
    Success,
    Warning,
    Error,
}
