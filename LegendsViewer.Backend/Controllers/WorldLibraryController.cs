using LegendsViewer.Backend.WorldLibrary;
using Microsoft.AspNetCore.Mvc;

namespace LegendsViewer.Backend.Controllers;

public record ExportDirectoriesRequest(string[] Paths);

[ApiController]
[Route("api/world-library")]
public class WorldLibraryController(WorldLibraryService library) : ControllerBase
{
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
