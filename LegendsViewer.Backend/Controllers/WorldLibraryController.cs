using LegendsViewer.Backend.WorldLibrary;
using Microsoft.AspNetCore.Mvc;
using LegendsViewer.Backend.WorldCache;

namespace LegendsViewer.Backend.Controllers;

public record ExportDirectoriesRequest(string[] Paths);

[ApiController]
[Route("api/world-library")]
public class WorldLibraryController(WorldLibraryService library, WorldCacheService? cache = null) : ControllerBase
{
    [HttpDelete("{snapshotId}/cache")]
    [ProducesResponseType<LibraryState>(200)]
    public async Task<ActionResult<LibraryState>> DeleteCache(string snapshotId)
    {
        if (Request.Headers["X-NextPlus-Settings"] != "1") return StatusCode(403);
        if (cache == null) return StatusCode(503);
        var snapshot = library.Get().Snapshots.SingleOrDefault(s => s.Id == snapshotId);
        if (snapshot == null) return NotFound();
        if (!await WorldLoadService.LoadGate.WaitAsync(0)) return Conflict("Another world is loading. Wait for it to finish.");
        try { cache.Delete(Path.Combine(snapshot.Directory, snapshot.Prefix + "-legends.xml")); return Ok(library.Get()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Problem(ex.Message); }
        finally { WorldLoadService.LoadGate.Release(); }
    }
    [HttpGet]
    public ActionResult<LibraryState> Get() => Ok(library.Get());

    [HttpPost("refresh")]
    [ProducesResponseType<LibraryState>(200)]
    [ProducesResponseType<ProblemDetails>(500)]
    public ActionResult<LibraryState> Refresh(CancellationToken cancellation)
    {
        if (Request.Headers["X-NextPlus-Settings"] != "1") return StatusCode(403);
        try { return Ok(library.Scan(cancellation)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return Problem("Could not save the library index. Check AppData access: " + ex.Message, statusCode: 500); }
    }

    [HttpPut("directories")]
    [ProducesResponseType<LibraryState>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(500)]
    public ActionResult<LibraryState> Directories(ExportDirectoriesRequest request)
    {
        if (Request.Headers["X-NextPlus-Settings"] != "1") return StatusCode(403);
        try { return Ok(library.SetDirectories(request.Paths)); }
        catch (ArgumentException ex) { return Problem(ex.Message, statusCode: 400); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return Problem("Could not save export folders: " + ex.Message, statusCode: 500); }
    }
}
