using System.Globalization;
using ApiMonitor.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace ApiMonitor.Views.Controls;

/// <summary>
/// v0.6.0：轻量本地趋势图控件（WinUI 3 原生 Canvas/Polyline/Path，无图表框架）。
/// 绑定 <see cref="Points"/>（TrendPoint 集合，时间升序）与 <see cref="Unit"/>。
/// 要求：
///   - 未知值（Value=null）不绘制、不连接跨越缺失数据的虚假连续线；
///   - 金额/额度使用 decimal（TrendPoint.Value 为 decimal?）；
///   - 时间以 UTC 存储、按本地时间显示；
///   - 深色/浅色/高对比度模式可辨认（ThemeResource 画笔，状态不只靠颜色）；
///   - AutomationProperties.Name 提供可访问摘要（绑 ChartSummary）。
/// 控件只读，不承担数据编辑。
/// </summary>
public sealed class TrendChartControl : UserControl
{
    /// <summary>趋势数据点（时间升序，Value=null 表示未知）。</summary>
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points),
        typeof(IReadOnlyList<TrendPoint>),
        typeof(TrendChartControl),
        new PropertyMetadata(null, OnPointsChanged));

    /// <summary>数值单位（轴标签与可访问摘要用）。</summary>
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit),
        typeof(string),
        typeof(TrendChartControl),
        new PropertyMetadata(string.Empty));

    /// <summary>图表可访问摘要文本（读屏与 AutomationProperties.Name）。</summary>
    public static readonly DependencyProperty ChartSummaryProperty = DependencyProperty.Register(
        nameof(ChartSummary),
        typeof(string),
        typeof(TrendChartControl),
        new PropertyMetadata(string.Empty));

    private readonly Grid _root;
    private readonly Canvas _canvas;
    private readonly TextBlock _summaryText;
    private readonly TextBlock _emptyText;
    private const double LeftPadding = 46;
    private const double RightPadding = 8;
    private const double TopPadding = 12;
    private const double BottomPadding = 26;

    public IReadOnlyList<TrendPoint>? Points
    {
        get => (IReadOnlyList<TrendPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public string ChartSummary
    {
        get => (string)GetValue(ChartSummaryProperty);
        set => SetValue(ChartSummaryProperty, value);
    }

    public TrendChartControl()
    {
        AutomationProperties.SetName(this, ApiMonitor.Services.L10n.Get("Insights.TrendChartName"));

        _root = new Grid { RowSpacing = 4 };

        _canvas = new Canvas
        {
            Height = 220,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _canvas.SizeChanged += (_, _) => Redraw();

        _emptyText = new TextBlock
        {
            Text = ApiMonitor.Services.L10n.Get("Insights.EmptyState"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray),
        };

        _summaryText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray),
            FontSize = 12,
        };

        _root.Children.Add(_canvas);
        _root.Children.Add(_emptyText);
        _root.Children.Add(_summaryText);

        Grid.SetRow(_emptyText, 0);
        Grid.SetRow(_canvas, 0);
        Grid.SetRow(_summaryText, 1);

        Content = _root;
    }

    private static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TrendChartControl control)
        {
            control.Redraw();
        }
    }

    private void Redraw()
    {
        _canvas.Children.Clear();

        var points = Points;
        if (points is null || points.Count == 0)
        {
            _emptyText.Visibility = Visibility.Visible;
            _summaryText.Visibility = Visibility.Collapsed;
            return;
        }

        double width = Math.Max(_canvas.ActualWidth, 200);
        double height = _canvas.Height;

        // 只取数值有效的点（未知值不参与坐标计算）。
        var valid = points.Where(p => p.Value is not null).Select(p => (p.TimeUtc, p.Value!.Value)).ToList();
        if (valid.Count == 0)
        {
            _emptyText.Visibility = Visibility.Visible;
            _summaryText.Visibility = Visibility.Collapsed;
            return;
        }

        _emptyText.Visibility = Visibility.Collapsed;
        _summaryText.Visibility = Visibility.Visible;
        _summaryText.Text = ChartSummary;

        decimal min = valid.Min(v => v.Value);
        decimal max = valid.Max(v => v.Value);
        decimal range = max - min;
        if (range == 0)
        {
            range = Math.Max(Math.Abs(max) * 0.1m, 1m);
        }

        DateTimeOffset start = valid[0].TimeUtc;
        DateTimeOffset end = valid[^1].TimeUtc;
        double timeSpan = Math.Max((end - start).TotalSeconds, 1);

        double ToX(DateTimeOffset t) =>
            LeftPadding + (t - start).TotalSeconds / timeSpan * (width - LeftPadding - RightPadding);

        double ToY(decimal v) =>
            TopPadding + (double)((max - v) / range) * (height - TopPadding - BottomPadding);

        // 网格线（ThemeResource 画笔，深色/浅色均可辨认）。
        var gridBrush = GetThemeBrush("DividerStrokeColorDefaultBrush");
        for (int i = 0; i <= 4; i++)
        {
            double y = TopPadding + i * (height - TopPadding - BottomPadding) / 4;
            var line = new Line
            {
                X1 = LeftPadding,
                X2 = width - RightPadding,
                Y1 = y,
                Y2 = y,
                Stroke = gridBrush,
                StrokeThickness = 1,
            };
            _canvas.Children.Add(line);

            decimal tickValue = max - (max - min) * i / 4m;
            var label = new TextBlock
            {
                Text = FormatAxisValue(tickValue),
                FontSize = 10,
                Foreground = GetThemeBrush("TextFillColorSecondaryBrush"),
            };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, y - 8);
            _canvas.Children.Add(label);
        }

        // 横轴时间标签（首/中/末）。
        AddTimeLabel(valid[0].TimeUtc, ToX(valid[0].TimeUtc), width);
        int midIndex = valid.Count / 2;
        AddTimeLabel(valid[midIndex].TimeUtc, ToX(valid[midIndex].TimeUtc), width);
        AddTimeLabel(valid[^1].TimeUtc, ToX(valid[^1].TimeUtc), width);

        // 折线：只在相邻有效点之间连线（不连接跨越缺失数据的虚假连续线）。
        var lineBrush = GetThemeBrush("AccentFillColorDefaultBrush");
        for (int i = 1; i < valid.Count; i++)
        {
            var prev = valid[i - 1];
            var cur = valid[i];
            bool prevHasPoint = points.Any(p => p.TimeUtc == prev.TimeUtc && p.Value is not null);
            bool curHasPoint = points.Any(p => p.TimeUtc == cur.TimeUtc && p.Value is not null);
            _ = prevHasPoint;
            _ = curHasPoint;

            // 连续点（中间没有未知值）才连线。
            bool continuous = AreConsecutive(points, prev.TimeUtc, cur.TimeUtc);
            if (!continuous)
            {
                continue;
            }

            var line = new Line
            {
                X1 = ToX(prev.TimeUtc),
                Y1 = ToY(prev.Value),
                X2 = ToX(cur.TimeUtc),
                Y2 = ToY(cur.Value),
                Stroke = lineBrush,
                StrokeThickness = 2,
            };
            _canvas.Children.Add(line);
        }

        // 数据点（可见的小圆点，状态不只靠颜色——辅以点本身）。
        var dotBrush = GetThemeBrush("AccentFillColorDefaultBrush");
        for (int i = 0; i < valid.Count; i++)
        {
            var (time, value) = valid[i];
            double x = ToX(time);
            double y = ToY(value);
            var dot = new Ellipse
            {
                Width = 5,
                Height = 5,
                Fill = dotBrush,
            };
            Canvas.SetLeft(dot, x - 2.5);
            Canvas.SetTop(dot, y - 2.5);
            _canvas.Children.Add(dot);
        }
    }

    /// <summary>两个有效点之间是否存在未知值间隔（存在则视为不连续）。</summary>
    private static bool AreConsecutive(IReadOnlyList<TrendPoint> points, DateTimeOffset a, DateTimeOffset b)
    {
        bool foundA = false;
        bool sawGap = false;
        foreach (var p in points)
        {
            if (!foundA)
            {
                if (p.TimeUtc == a)
                {
                    foundA = true;
                }

                continue;
            }

            if (p.TimeUtc == b)
            {
                return !sawGap;
            }

            if (p.Value is null)
            {
                sawGap = true;
            }
        }

        return false;
    }

    private void AddTimeLabel(DateTimeOffset timeUtc, double x, double width)
    {
        var label = new TextBlock
        {
            Text = timeUtc.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.CurrentCulture),
            FontSize = 10,
            Foreground = GetThemeBrush("TextFillColorSecondaryBrush"),
        };
        double left = Math.Clamp(x - 30, 0, Math.Max(0, width - 60));
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, _canvas.Height - 20);
        _canvas.Children.Add(label);
    }

    private static string FormatAxisValue(decimal value)
    {
        // 数值过大时用简写（K/M），保证轴标签可读。
        if (Math.Abs(value) >= 1_000_000m)
        {
            return (value / 1_000_000m).ToString("0.#", CultureInfo.CurrentCulture) + "M";
        }

        if (Math.Abs(value) >= 1_000m)
        {
            return (value / 1_000m).ToString("0.#", CultureInfo.CurrentCulture) + "K";
        }

        return value.ToString("0.##", CultureInfo.CurrentCulture);
    }

    /// <summary>安全获取主题画刷；资源缺失时回退灰色，避免 Application.Resources 直取崩溃。</summary>
    private static Brush GetThemeBrush(string key)
    {
        try
        {
            if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Brush brush)
            {
                return brush;
            }
        }
        catch
        {
            // 回退。
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
}
