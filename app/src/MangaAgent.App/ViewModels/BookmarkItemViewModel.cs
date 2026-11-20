using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MangaAgent.App.ViewModels;

/// <summary>v4.21：书签面板的一行（页码 + 备注，点击跳转、可删除）。</summary>
public partial class BookmarkItemViewModel : ObservableObject
{
    private readonly ReaderViewModel _owner;

    public int Page { get; }
    /// <summary>物理页序号（0 基）→ 显示用页码（1 基）。</summary>
    public string Label => $"第 {Page + 1} 页";
    public string? Note { get; }
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);
    public string NoteText => Note ?? "";

    public BookmarkItemViewModel(ReaderViewModel owner, int page, string? note)
    {
        _owner = owner;
        Page = page;
        Note = note;
    }

    [RelayCommand]
    private void Jump() => _owner.GoToBookmarkPage(Page);

    [RelayCommand]
    private void Delete() => _owner.RemoveBookmarkAt(Page);
}
