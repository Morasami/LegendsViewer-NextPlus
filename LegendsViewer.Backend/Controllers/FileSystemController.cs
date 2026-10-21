using System.Web;
using LegendsViewer.Backend.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace LegendsViewer.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FileSystemController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<FilesAndSubdirectoriesDto>(StatusCodes.Status200OK)]
    public ActionResult<FilesAndSubdirectoriesDto> Get()
    {
        return Ok(GetHomeInformation());
    }

    [HttpGet("{encodedPath}")]
    [ProducesResponseType<FilesAndSubdirectoriesDto>(StatusCodes.Status200OK)]
    public ActionResult<FilesAndSubdirectoriesDto> Get([FromRoute] string encodedPath)
    {
        var path = HttpUtility.UrlDecode(encodedPath);

        if (path == "mounts")
        {
            return Ok(GetMountsInformation());
        }

        if (!Path.Exists(path))
        {
            return Ok(GetHomeInformation());
        }

        string directoryName;
        if (Directory.Exists(path))
        {
            directoryName = path;
        }
        else
        {
            directoryName = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
        }

        return Ok(BrowseDirectory(directoryName));
    }

    internal static FilesAndSubdirectoriesDto BrowseDirectory(string directoryName, bool foldersOnly = false)
    {
        return new FilesAndSubdirectoriesDto
        {
            CurrentDirectory = directoryName,
            ParentDirectory = Directory.GetParent(directoryName)?.FullName ?? "mounts",
            Subdirectories = Directory.GetDirectories(directoryName)
                .Where(path => IsNotUnixHiddenPath(Path.GetFileName(path)))
                .Select(path => foldersOnly ? path : Path.GetRelativePath(directoryName, path))
                .Order().ToArray(),
            Files = foldersOnly ? [] : Directory.GetFiles(directoryName, $"*{BookmarkController.FileIdentifierLegendsXml}")
                .Select(f => Path.GetFileName(f) ?? "")
                .Where(IsNotUnixHiddenPath).Order().ToArray()
        };
    }

    [HttpGet("{encodedCurrentPath}/{encodedSubFolder}")]
    [ProducesResponseType<string>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<FilesAndSubdirectoriesDto> Get([FromRoute] string encodedCurrentPath, [FromRoute] string encodedSubFolder)
    {
        var currentPath = HttpUtility.UrlDecode(encodedCurrentPath);
        var subFolder = HttpUtility.UrlDecode(encodedSubFolder);
        var fullPath = Path.Combine(currentPath, subFolder);
        if (!Path.Exists(fullPath))
        {
            return BadRequest("File does not exist!");
        }
        return Get(fullPath);
    }

    private static FilesAndSubdirectoriesDto GetMountsInformation()
    {
        // GetLogicalDrives will also get mount points on Unix/MacOS: see
        // https://learn.microsoft.com/en-us/dotnet/api/system.io.directory.getlogicaldrives#remarks
        var logicalDrives = Directory.GetLogicalDrives();
        return new FilesAndSubdirectoriesDto
        {
            CurrentDirectory = Path.DirectorySeparatorChar.ToString(),
            ParentDirectory = null,
            Subdirectories = logicalDrives.Where(IsNotUnixHiddenPath)
                .Order() // sort alphabetically
                .ToArray(),
            Files = Array.Empty<string>()
        };
    }

    private static FilesAndSubdirectoriesDto GetHomeInformation()
    {
        var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var homeDir = new DirectoryInfo(homePath);

        return new FilesAndSubdirectoriesDto
        {
            CurrentDirectory = homePath,
            ParentDirectory = Directory.GetParent(homePath)?.FullName,
            Subdirectories = homeDir.GetDirectories()
                .Select(d => d.Name)
                .Where(IsNotUnixHiddenPath)
                .Order() // sort alphabetically
                .ToArray(),
            Files = homeDir.GetFiles()
                .Select(f => f.Name)
                .Where(IsNotUnixHiddenPath)
                .Order()
                .ToArray()
        };
    }

    private static bool IsNotUnixHiddenPath(string path)
    {
        // Hide all '.' paths, except steam path
        return !(path.StartsWith('.') && path != ".steam");
    }
}
