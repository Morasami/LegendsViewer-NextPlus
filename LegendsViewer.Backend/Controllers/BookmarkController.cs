using System.Web;
using LegendsViewer.Backend.WorldLibrary;
using LegendsViewer.Backend.Legends.Bookmarks;
using LegendsViewer.Backend.Legends.Interfaces;
using LegendsViewer.Backend.Legends.Maps;
using Microsoft.AspNetCore.Mvc;

namespace LegendsViewer.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BookmarkController(
    ILogger<BookmarkController> logger,
    IWorld worldDataService,
    IWorldMapImageGenerator worldMapImageGenerator,
    IBookmarkService bookmarkService,
    WorldLibraryService? library = null) : ControllerBase
{
    private static readonly SemaphoreSlim LoadGate = new(1, 1);

    public const string FileIdentifierLegendsXml = "-legends.xml";

    private const string FileIdentifierWorldHistoryTxt = "-world_history.txt";
    private const string FileIdentifierWorldMapBmp = "-world_map.bmp";
    private const string FileIdentifierWorldSitesAndPops = "-world_sites_and_pops.txt";
    private const string FileIdentifierLegendsPlusXml = "-legends_plus.xml";
    private readonly IWorld _worldDataService = worldDataService;
    private readonly IWorldMapImageGenerator _worldMapImageGenerator = worldMapImageGenerator;
    private readonly IBookmarkService _bookmarkService = bookmarkService;

    [HttpGet]
    [ProducesResponseType<List<Bookmark>>(StatusCodes.Status200OK)]
    public ActionResult<List<Bookmark>> Get()
    {
        var bookmarks = _bookmarkService.GetAll();
        return Ok(bookmarks);
    }

    [HttpGet("{*encodedFilePath}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<Bookmark> Get([FromRoute] string encodedFilePath)
    {
        var filePath = HttpUtility.UrlDecode(encodedFilePath);
        var item = _bookmarkService.GetBookmark(filePath);
        if (item == null)
        {
            return NotFound();
        }
        return Ok(item);
    }

    [HttpDelete("{*encodedFilePath}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<Bookmark> Delete([FromRoute] string encodedFilePath)
    {
        var filePath = HttpUtility.UrlDecode(encodedFilePath);
        var result = _bookmarkService.DeleteBookmarkTimestamp(filePath);
        
        if (!result.Success)
        {
            return NotFound();
        }
        
        if (result.FileMissing)
        {
            Response.Headers.Append("X-File-Missing", "true");
        }
        
        if (result.Bookmark == null)
        {
            return NoContent();
        }
        
        return Ok(result.Bookmark);
    }

    [HttpPost("loadByFullPath")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Bookmark>> ParseWorldXml([FromBody] string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
        {
            return BadRequest($"Invalid file path.\n{filePath}");
        }
        FileInfo fileInfo = new(filePath);
        if (string.IsNullOrWhiteSpace(fileInfo.DirectoryName))
        {
            return BadRequest("Invalid directory.");
        }
        string directoryName = fileInfo.DirectoryName;
        var export = ExportName.Parse(fileInfo.Name);
        if (export == null || export.Kind is not ("main" or "extended"))
            return BadRequest($"Invalid file name.\n{fileInfo.Name}");
        var regionId = export.Prefix;
        var RegionName = export.Region;
        var Timestamp = export.Date ?? "";
        string? Companion(string suffix)
        {
            var path = Path.Combine(directoryName, regionId + suffix);
            return System.IO.File.Exists(path) ? path : null;
        }
        var xmlFileName = Companion(FileIdentifierLegendsXml);
        if (xmlFileName == null) return BadRequest("Main legends XML is missing.");
        var xmlPlusFileName = Companion(FileIdentifierLegendsPlusXml);
        var historyFileName = Companion(FileIdentifierWorldHistoryTxt);
        var sitesAndPopsFileName = Companion(FileIdentifierWorldSitesAndPops);
        var mapFileName = Companion(FileIdentifierWorldMapBmp);
        // The parser and map generator share singleton state. Reject overlapping manual/library loads.
        if (!await LoadGate.WaitAsync(0)) return Conflict("Another world is loading. Wait for it to finish.");

        try
        {
            _worldDataService.Clear();
            _worldMapImageGenerator.Clear();
            _bookmarkService.MarkUnloaded();

            logger.LogInformation($"Start loading world '{regionId}' from '{directoryName}'");

            await Task.Run(async () =>
            {
                await _worldMapImageGenerator.LoadExportedWorldMapAsync(xmlFileName);
                await _worldDataService.ParseAsync(xmlFileName, xmlPlusFileName, historyFileName, sitesAndPopsFileName, mapFileName);
            });

            logger.LogInformation(_worldDataService.Log.ToString());

            var bookmark = AddBookmark(xmlFileName, RegionName, Timestamp);
            try { library?.RecordLoad(xmlFileName, bookmark); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { logger.LogWarning(ex, "World loaded, but library metadata could not be saved"); }

            return Ok(bookmark);
        }
        catch (Exception ex)
        {
            _worldDataService.Clear();
            _worldMapImageGenerator.Clear();
            try { library?.RecordLoad(xmlFileName, null, ex.Message); }
            catch (Exception metadataError) when (metadataError is IOException or UnauthorizedAccessException)
            { logger.LogWarning(metadataError, "Could not save failed load metadata"); }
            logger.LogError(ex, "Failed to load world {RegionId}", regionId);
            return StatusCode(500, $"Error parsing the XML file: {ex.Message}");
        }
        finally { LoadGate.Release(); }
    }

    [HttpPost("loadByFolderAndFile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Bookmark>> ParseWorldXml([FromBody] string folderPath, string fileName)
    {
        var fullPath = Path.Combine(folderPath, fileName);
        if (!Path.Exists(fullPath))
        {
            return BadRequest("File does not exist!");
        }
        return await ParseWorldXml(fullPath);
    }

    private Bookmark AddBookmark(string filePath, string regionName, string timestamp)
    {
        var imageData = _worldMapImageGenerator.GenerateMapByteArray(WorldMapImageGenerator.DefaultTileSizeMin);
        var bookmark = new Bookmark
        {
            Id = $"{_worldDataService.Name}_{regionName}",
            FilePath = string.IsNullOrEmpty(timestamp) ? filePath : BookmarkService.ReplaceLastOccurrence(filePath, timestamp, BookmarkService.TimestampPlaceholder),
            WorldName = _worldDataService.Name,
            WorldAlternativeName = _worldDataService.AlternativeName,
            WorldRegionName = regionName,
            WorldTimestamps = [timestamp],
            WorldWidth = _worldDataService.Width,
            WorldHeight = _worldDataService.Height,
            WorldMapImage = imageData,
            State = BookmarkState.Loaded,
            LoadedTimestamp = timestamp,
            LatestTimestamp = timestamp
        };

        return _bookmarkService.AddBookmark(bookmark);
    }
}
