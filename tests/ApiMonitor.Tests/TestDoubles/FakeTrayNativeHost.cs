using ApiMonitor.Services;

namespace ApiMonitor.Tests.TestDoubles;

/// <summary>记录调用并模拟托盘回调的假原生宿主。</summary>
public sealed class FakeTrayNativeHost : ITrayNativeHost
{
    public bool InitializeResult { get; set; } = true;

    public bool AddIconResult { get; set; } = true;

    public bool UpdateTipResult { get; set; } = true;

    public bool DeleteIconResult { get; set; } = true;

    public int InitializeCalls { get; private set; }

    public int AddIconCalls { get; private set; }

    public int UpdateTipCalls { get; private set; }

    public int DeleteIconCalls { get; private set; }

    public List<string> Tooltips { get; } = new();

    public List<IReadOnlyList<TrayMenuItem>> MenuItems { get; } = new();

    public uint? ShowContextMenuResult { get; set; }

    public bool Disposed { get; private set; }

    public bool IsMessageWindowAlive => true;

    public event Action? LeftClick;

    public event Action? LeftDoubleClick;

    public event Action<TrayScreenPoint>? ContextMenuRequested;

    public event Action? TaskbarCreated;

    public void RaiseLeftClick() => LeftClick?.Invoke();

    public void RaiseLeftDoubleClick() => LeftDoubleClick?.Invoke();

    public void RaiseContextMenu(TrayScreenPoint point) => ContextMenuRequested?.Invoke(point);

    public void RaiseTaskbarCreated() => TaskbarCreated?.Invoke();

    public bool Initialize()
    {
        InitializeCalls++;
        return InitializeResult;
    }

    public bool AddIcon(string tooltipText)
    {
        AddIconCalls++;
        Tooltips.Add(tooltipText);
        return AddIconResult;
    }

    public bool UpdateTip(string tooltipText)
    {
        UpdateTipCalls++;
        Tooltips.Add(tooltipText);
        return UpdateTipResult;
    }

    public bool DeleteIcon()
    {
        DeleteIconCalls++;
        return DeleteIconResult;
    }

    public uint? ShowContextMenu(IReadOnlyList<TrayMenuItem> items, TrayScreenPoint position)
    {
        MenuItems.Add(items);
        return ShowContextMenuResult;
    }

    public void Dispose() => Disposed = true;
}
