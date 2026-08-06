namespace ApiMonitor.ViewModels;

/// <summary>与 WinUI 控件无关的状态级别，由视图层转换为 InfoBarSeverity。</summary>
public enum StatusSeverity
{
    Informational,
    Success,
    Warning,
    Error,
}

/// <summary>账户卡片的状态分类（主界面状态筛选与通知定位共用）。</summary>
public enum AccountStatusKind
{
    Normal,
    Low,
    Unknown,
    Failed,
}

/// <summary>主界面账户状态筛选。</summary>
public enum AccountStatusFilter
{
    All,
    Normal,
    Low,
    Unknown,
    Failed,
}

/// <summary>主窗口导航页面（导航外壳共享同一账户状态，不创建第二份账户状态）。</summary>
public enum AppPageKind
{
    Home,
    Insights,
    Settings,
    About,

    /// <summary>v1.0.0：首次启动引导（完成/跳过后不再自动出现）。</summary>
    Onboarding,
}
