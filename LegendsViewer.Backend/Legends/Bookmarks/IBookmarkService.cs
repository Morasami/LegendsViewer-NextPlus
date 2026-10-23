namespace LegendsViewer.Backend.Legends.Bookmarks;

public class DeleteResult
{
    public bool Success { get; set; }
    public bool FileMissing { get; set; }
    public Bookmark? Bookmark { get; set; }
}

public interface IBookmarkService
{
    Bookmark AddBookmark(Bookmark bookmark);
    DeleteResult DeleteBookmarkTimestamp(string filePath);
    List<Bookmark> GetAll();
    Bookmark? GetBookmark(string filePath);
    void MarkUnloaded();
}
