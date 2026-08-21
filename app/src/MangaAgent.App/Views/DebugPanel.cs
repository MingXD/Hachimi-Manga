using Avalonia.Controls;
using Avalonia.Threading;
using System.Collections.Generic;
using System.Linq;

namespace MangaAgent.App.Views;

/// <summary>调试信息面板（F12 开关）：实时显示阅读器状态 + 最近事件流，帮助排查"点击无反应"等交互问题。</summary>
public partial class DebugPanel : UserControl
{
    private DispatcherTimer? _timer;
    private int _lastEventCount;

    public DebugPanel()
    {
        var layout = new StackPanel { Spacing = 4 };
        StatusText = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 11, Foreground = Avalonia.Media.Brushes.LimeGreen };
        EventsList = new ListBox { Height = 180, FontSize = 10, Background = Avalonia.Media.Brushes.Transparent };
        layout.Children.Add(new TextBlock { Text = "🔍 调试面板 (F12 关闭)", FontWeight = Avalonia.Media.FontWeight.Bold, FontSize = 12, Foreground = Avalonia.Media.Brushes.White });
        layout.Children.Add(StatusText);
        layout.Children.Add(EventsList);
        Content = layout;
        Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#D0000000"));
        IsVisible = false;
    }

    public TextBlock StatusText { get; }
    public ListBox EventsList { get; }

    public void Start()
    {
        _timer ??= new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
    }

    /// <summary>由宿主注入状态读取委托（避免 DebugPanel 依赖具体窗口）。</summary>
    public System.Func<string>? StatusProvider { get; set; }

    private void Refresh()
    {
        if (!IsVisible) return;
        if (StatusProvider is not null) StatusText.Text = StatusProvider();
        var events = global::MangaAgent.App.App.RecentEvents.ToList();
        if (events.Count != _lastEventCount)
        {
            EventsList.ItemsSource = events.TakeLast(20).Select(e => $"{e.Time:HH:mm:ss.fff} {e.Msg}").ToList();
            _lastEventCount = events.Count;
            EventsList.ScrollIntoView(EventsList.ItemsSource.Cast<object>().LastOrDefault());
        }
    }
}
