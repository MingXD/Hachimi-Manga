using CommunityToolkit.Mvvm.ComponentModel;
using MangaAgent.Core.Library;

namespace MangaAgent.App.ViewModels;

/// <summary>资料库条目（下拉框展示 + 路径失效状态）。v4.20：路径失效的库不再被静默跳过，
/// 而是保留在列表里并标记 <see cref="IsValid"/>=false，供"关联"按钮重新指到新位置（库改名/移动后无需清空重建）。</summary>
public partial class LibraryItemViewModel : ViewModelBase
{
    [ObservableProperty] private string _path;
    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _isValid;

    public List<BookInfo> Books { get; }

    public LibraryItemViewModel(string path, List<BookInfo> books)
    {
        _path = path;
        _name = NameFromPath(path);
        Books = books;
        _isValid = Directory.Exists(path);
    }

    /// <summary>下拉框显示名（路径失效时带后缀，一眼看出哪个库需要重新关联）。</summary>
    public string DisplayName => IsValid ? Name : $"{Name}（路径失效）";

    partial void OnIsValidChanged(bool value) => OnPropertyChanged(nameof(DisplayName));
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    /// <summary>重新关联到新路径：名称随新路径更新，合法性重新判定（不扫描书籍，扫描由调用方负责）。</summary>
    public void UpdatePath(string path)
    {
        Path = path;
        Name = NameFromPath(path);
        IsValid = Directory.Exists(path);
    }

    private static string NameFromPath(string path)
        => System.IO.Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : path;
}
