using Avalonia;
using Avalonia.Controls;

namespace MangaAgent.App.Views;

/// <summary>调试窗口（F12 开关）：独立小窗显示实时状态 + 事件流，不占阅读器/主窗口空间。</summary>
public class DebugWindow : Window
{
    private Avalonia.Threading.DispatcherTimer? _timer;
    private int _lastEventCount;
    public TextBlock StatusText { get; }
    public ListBox EventsList { get; }

    public DebugWindow()
    {
        Title = "Hachimi Manga 调试面板";
        Width = 460;
        Height = 380;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        var layout = new StackPanel { Spacing = 4, Margin = new Thickness(8) };
        StatusText = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 11, Foreground = Avalonia.Media.Brushes.LimeGreen };
        EventsList = new ListBox { Height = 240, FontSize = 10, Background = Avalonia.Media.Brushes.Transparent };
        layout.Children.Add(new TextBlock { Text = "🔍 调试面板 (F12 开关)", FontWeight = Avalonia.Media.FontWeight.Bold, FontSize = 13 });
        layout.Children.Add(StatusText);
        layout.Children.Add(new TextBlock { Text = "最近事件（点击/滚轮/缩放/翻页/异常）：", FontSize = 10, Foreground = Avalonia.Media.Brushes.Gray });
        layout.Children.Add(EventsList);
        Content = layout;
    }

    public void Start(System.Func<string>? statusProvider)
    {
        StatusProvider = statusProvider;
        _timer ??= new Avalonia.Threading.DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
    }

    public System.Func<string>? StatusProvider { get; set; }

    private void Refresh()
    {
        if (StatusProvider is not null) StatusText.Text = StatusProvider();
        var events = global::MangaAgent.App.App.RecentEvents.ToList();
        if (events.Count != _lastEventCount)
        {
            EventsList.ItemsSource = events.TakeLast(25).Select(e => $"{e.Time:HH:mm:ss.fff} {e.Msg}").ToList();
            _lastEventCount = events.Count;
            if (EventsList.ItemCount > 0) EventsList.ScrollIntoView(EventsList.ItemsSource.Cast<object>().Last());
        }
    }
}
