using LegendsViewer.Backend.Contracts;
using LegendsViewer.Backend.Search;
using LegendsViewer.Backend.WorldCache;
using Microsoft.AspNetCore.Mvc;

namespace LegendsViewer.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class SearchController(WorldSessionLease lease) : ControllerBase
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 25;
    private const int MaximumPage = 100_000;
    private const int MaximumCategoriesLength = 500;
    private const int MinimumYear = -100_000;
    private const int MaximumYear = 100_000;

    [HttpGet]
    [ProducesResponseType<HistoricalSearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<HistoricalSearchResponse> Search(
        [FromQuery] string? q = null,
        [FromQuery] string? categories = null,
        [FromQuery] int? fromYear = null,
        [FromQuery] int? toYear = null,
        [FromQuery] int page = DefaultPage,
        [FromQuery] int pageSize = DefaultPageSize)
    {
        Dictionary<string, string[]> errors = [];
        if (page < 1 || page > MaximumPage)
            errors["page"] = [$"Page must be between 1 and {MaximumPage}."];
        if (pageSize < 1 || pageSize > HistoricalSearchIndex.MaximumPageSize)
            errors["pageSize"] = [$"Page size must be between 1 and {HistoricalSearchIndex.MaximumPageSize}."];
        if (fromYear is < MinimumYear or > MaximumYear)
            errors["fromYear"] = [$"From year must be between {MinimumYear} and {MaximumYear}."];
        if (toYear is < MinimumYear or > MaximumYear)
            errors["toYear"] = [$"To year must be between {MinimumYear} and {MaximumYear}."];
        if (fromYear.HasValue && toYear.HasValue && fromYear.Value > toYear.Value)
            errors["yearRange"] = ["From year cannot be after to year."];

        string query = q?.Trim() ?? string.Empty;
        if (query.Length > HistoricalSearchIndex.MaximumQueryLength)
            errors["q"] = [$"Search text must be at most {HistoricalSearchIndex.MaximumQueryLength} characters."];

        HashSet<HistoricalSearchCategory>? selectedCategories = null;
        if (!string.IsNullOrWhiteSpace(categories))
        {
            if (categories.Length > MaximumCategoriesLength)
            {
                errors["categories"] = ["The category filter is too long."];
            }
            else
            {
                selectedCategories = [];
                foreach (string rawCategory in categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string? canonicalName = Enum.GetNames<HistoricalSearchCategory>()
                        .FirstOrDefault(name => string.Equals(name, rawCategory, StringComparison.OrdinalIgnoreCase));
                    if (canonicalName == null)
                    {
                        errors["categories"] = [$"Unknown category '{rawCategory}'. Valid categories: {string.Join(", ", Enum.GetNames<HistoricalSearchCategory>())}."];
                        break;
                    }
                    selectedCategories.Add(Enum.Parse<HistoricalSearchCategory>(canonicalName));
                }
            }
        }

        if (errors.Count > 0)
        {
            return ValidationProblem(new ValidationProblemDetails(errors));
        }

        if (!string.IsNullOrWhiteSpace(query) && !TextNormalizer.HasSearchableCharacter(query))
        {
            return InvalidSearch("Search text must contain at least one letter or digit.");
        }

        HistoricalSearchResponse response = lease.SearchIndex.Search(query, selectedCategories, fromYear, toYear, page, pageSize);
        FillWorld(response);
        if (!response.WorldLoaded)
        {
            response.Message = "No world is loaded. Open a world from Explore Worlds first.";
        }
        else if (string.IsNullOrWhiteSpace(query))
        {
            response.Message = "Enter a name, place, artifact, war, battle, or event phrase to search this world.";
        }
        else if (response.TotalCount == 0)
        {
            response.Message = "No historical records match this search in the loaded world.";
        }

        return Ok(response);
    }

    [HttpGet("metadata")]
    [ProducesResponseType<HistoricalSearchMetadataDto>(StatusCodes.Status200OK)]
    public ActionResult<HistoricalSearchMetadataDto> Metadata()
    {
        HistoricalSearchIndex index = lease.SearchIndex;
        return Ok(new HistoricalSearchMetadataDto
        {
            WorldLoaded = IsWorldLoaded,
            Generation = lease.GenerationId,
            WorldName = IsWorldLoaded ? lease.World.Name : null,
            WorldAlternativeName = IsWorldLoaded ? lease.World.AlternativeName : null,
            CurrentYear = IsWorldLoaded ? lease.World.CurrentYear : null,
            IndexBuildMilliseconds = index.BuildMilliseconds,
            IndexedRecordCount = index.DocumentCount,
            EstimatedIndexBytes = index.EstimatedBytes,
            CategoryCounts = index.CategoryCounts.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
            UnavailableCategories = Enum.GetValues<HistoricalSearchCategory>()
                .Where(category => !index.CategoryCounts.TryGetValue(category, out int count) || count == 0)
                .Select(category => category.ToString())
                .ToList()
        });
    }

    private bool IsWorldLoaded => !string.IsNullOrWhiteSpace(lease.World.Name) || lease.World.Events.Count > 0 ||
                                  lease.World.HistoricalFigures.Count > 0 || lease.World.Entities.Count > 0;

    private void FillWorld(HistoricalSearchResponse response)
    {
        response.WorldLoaded = IsWorldLoaded;
        response.Generation = lease.GenerationId;
        response.WorldName = IsWorldLoaded ? lease.World.Name : null;
        response.WorldAlternativeName = IsWorldLoaded ? lease.World.AlternativeName : null;
        response.CurrentYear = IsWorldLoaded ? lease.World.CurrentYear : null;
    }

    private ActionResult<HistoricalSearchResponse> InvalidSearch(string message)
    {
        return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            ["q"] = [message]
        }));
    }
}
