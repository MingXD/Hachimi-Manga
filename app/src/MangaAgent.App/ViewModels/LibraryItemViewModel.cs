using MangaAgent.Core.Library;

namespace MangaAgent.App.ViewModels;

public sealed class LibraryItemViewModel
{
    public string Path { get; }
    public string Name { get; }
    public List<BookInfo> Books { get; }

    public LibraryItemViewModel(string path, List<BookInfo> books)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path;
        Books = books;
    }
}
