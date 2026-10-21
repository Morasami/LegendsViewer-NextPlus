using LegendsViewer.Backend.Settings;
using LegendsViewer.Backend.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace LegendsViewer.Backend.Controllers;

public record GamePathRequest(string Path);

[ApiController]
[Route("api/game-settings")]
public class GameSettingsController(GameInstallationService installations) : ControllerBase
{
    [HttpGet]
    public ActionResult<GameInstallationState> Get() => Ok(installations.Discover());

    [HttpPost("validate")]
    public ActionResult<GameInstallation> Validate(GamePathRequest request) => Ok(installations.Validate(request.Path));

    [HttpPut]
    [ProducesResponseType<GameInstallationState>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<GameInstallationState> Save(GamePathRequest request)
    {
        if (Request.Headers["X-NextPlus-Settings"] != "1") return StatusCode(403);
        try { return Ok(installations.Save(request.Path)); }
        catch (ArgumentException ex) { return Problem(ex.Message, statusCode: 400); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return Problem("Could not save settings: " + ex.Message, statusCode: 500); }
    }

    // Uses the existing server-side browser, with query parameters to preserve literal Unicode/percent paths.
    [HttpGet("directories")]
    [ProducesResponseType<FilesAndSubdirectoriesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public ActionResult<FilesAndSubdirectoriesDto> Directories([FromQuery] string? path = null)
    {
        try
        {
            if (path == "mounts") return Ok(new FilesAndSubdirectoriesDto {
                CurrentDirectory = "mounts", Subdirectories = Directory.GetLogicalDrives(), Files = [] });
            path = path == null ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : GameInstallationService.Normalize(path);
            if (!Directory.Exists(path)) return Problem("Folder is unavailable.", statusCode: 400);
            return Ok(FileSystemController.BrowseDirectory(path, foldersOnly: true));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        { return Problem("Cannot browse this folder: " + ex.Message, statusCode: 400); }
    }
}
