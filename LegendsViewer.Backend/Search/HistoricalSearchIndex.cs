using System.Diagnostics;
using LegendsViewer.Backend.Contracts;
using LegendsViewer.Backend.Extensions;
using LegendsViewer.Backend.Legends;
using LegendsViewer.Backend.Legends.EventCollections;
using LegendsViewer.Backend.Legends.Events;
using LegendsViewer.Backend.Legends.Various;
using LegendsViewer.Backend.Legends.WorldObjects;

namespace LegendsViewer.Backend.Search;

/// <summary>
/// Immutable, generation-scoped search index derived from the resolved historical graph.
/// It never reads an export file and never invokes the XML parser.
/// </summary>
public sealed class HistoricalSearchIndex
{
    public const int MaximumQueryLength = 200;
    public const int MaximumPageSize = 100;
    public const int MaximumCandidates = 25_000;
    private const int MaximumPrefixTokens = 2_048;

    private readonly SearchDocument[] documents;
    private readonly int[] namedDocumentIndexes;
    private readonly Dictionary<string, int[]> postings;
    private readonly string[] sortedTokens;
    private readonly Dictionary<HistoricalSearchCategory, int> categoryCounts;
    private readonly object eventPreviewGate = new();

    private HistoricalSearchIndex(SearchDocument[] documents, Dictionary<string, int[]> postings, string[] sortedTokens,
        Dictionary<HistoricalSearchCategory, int> categoryCounts, int buildMilliseconds, long estimatedBytes,
        int eventPreviewFailures)
    {
        this.documents = documents;
        this.postings = postings;
        this.sortedTokens = sortedTokens;
        this.categoryCounts = categoryCounts;
        namedDocumentIndexes = documents
            .Select((document, index) => (document, index))
            .Where(item => item.document.Category != HistoricalSearchCategory.Event)
            .Select(item => item.index)
            .ToArray();
        BuildMilliseconds = buildMilliseconds;
        EstimatedBytes = estimatedBytes;
        EventPreviewFailures = eventPreviewFailures;
    }

    public int BuildMilliseconds { get; }
    public long EstimatedBytes { get; }
    public int EventPreviewFailures { get; }
    public int DocumentCount => documents.Length;
    public IReadOnlyDictionary<HistoricalSearchCategory, int> CategoryCounts => categoryCounts;

    public static HistoricalSearchIndex Build(World world)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        List<SearchDocument> documents = new(capacity: 4096);
        Dictionary<string, List<int>> postingLists = new(StringComparer.Ordinal);
        HashSet<string> perDocumentTokens = new(StringComparer.Ordinal);
        int eventPreviewFailures = 0;

        void AddDocument(SearchDocument document, string normalizedText)
        {
            int ordinal = documents.Count;
            document.Ordinal = ordinal;
            documents.Add(document);
            perDocumentTokens.Clear();
            AddTokens(postingLists, perDocumentTokens, normalizedText, ordinal);
        }

        void AddObject(WorldObject worldObject, HistoricalSearchCategory category, string type, string description,
            int? year = null, string? dateLabel = null)
        {
            if (string.IsNullOrWhiteSpace(worldObject.Name))
            {
                return;
            }
            string name = TextNormalizer.Truncate(worldObject.Name.Trim(), 300);
            string normalizedName = TextNormalizer.Normalize(name);
            if (normalizedName.Length == 0)
            {
                return;
            }
            string? url = GetDetailUrl(worldObject);
            string stableType = string.IsNullOrWhiteSpace(type) ? category.ToString() : TextNormalizer.PlainText(type, 80);
            string searchable = normalizedName + " " + TextNormalizer.Normalize(stableType);
            string cleanDescription = TextNormalizer.PlainText(description, 360);
            if (cleanDescription.Length > 0)
            {
                searchable += " " + TextNormalizer.Normalize(cleanDescription);
            }
            AddDocument(new SearchDocument
            {
                Category = category,
                Name = name,
                NormalizedName = normalizedName,
                Type = stableType,
                Description = cleanDescription,
                Year = year,
                DateLabel = dateLabel,
                RecordId = worldObject.Id,
                Key = $"{category.ToString().ToLowerInvariant()}:{worldObject.Id}",
                Url = url,
                Navigable = url != null
            }, searchable);
        }

        foreach (HistoricalFigure figure in world.HistoricalFigures)
        {
            if (string.IsNullOrWhiteSpace(figure.Name))
            {
                continue;
            }
            int? year = figure.BirthYear >= 0 ? figure.BirthYear : figure.DeathYear >= 0 ? figure.DeathYear : null;
            string? dateLabel = figure.BirthYear >= 0 && figure.DeathYear >= 0
                ? $"{figure.BirthYear}–{figure.DeathYear}"
                : figure.BirthYear >= 0 ? $"Born {figure.BirthYear}" : figure.DeathYear >= 0 ? $"Died {figure.DeathYear}" : null;
            string race = figure.Race?.NameSingular ?? string.Empty;
            string description = string.IsNullOrWhiteSpace(race)
                ? "Historical figure"
                : $"{race} historical figure";
            if (figure.CurrentState != LegendsViewer.Backend.Legends.Enums.HfState.None)
            {
                description += $" · {figure.CurrentState}";
            }
            AddObject(figure, HistoricalSearchCategory.Figure, race.Length > 0 ? race : "Historical figure", description, year, dateLabel);
        }

        foreach (Entity entity in world.Entities)
        {
            if (string.IsNullOrWhiteSpace(entity.Name))
            {
                continue;
            }
            int? year = entity.SiteHistory
                .Where(period => period.StartYear >= 0)
                .Select(period => (int?)period.StartYear)
                .Min();
            string? dateLabel = year.HasValue ? $"First recorded {year}" : null;
            string type = string.IsNullOrWhiteSpace(entity.Type) ? "Historical entity" : entity.Type;
            string race = entity.Race?.NamePlural ?? string.Empty;
            string description = type;
            if (!string.IsNullOrWhiteSpace(race))
            {
                description += $" of the {race}";
            }
            description += entity.SiteHistory.Count > 0 ? $" · {entity.SiteHistory.Count} site record(s)" : " · no recorded sites";
            AddObject(entity, HistoricalSearchCategory.Entity, type, description, year, dateLabel);
        }

        foreach (Site site in world.Sites)
        {
            if (string.IsNullOrWhiteSpace(site.Name))
            {
                continue;
            }
            int? year = site.OwnerHistory
                .Where(period => period.StartYear >= 0)
                .Select(period => (int?)period.StartYear)
                .Min();
            string? dateLabel = year.HasValue ? $"First recorded {year}" : null;
            string description = $"{site.Type} site";
            if (site.Region != null)
            {
                description += $" in {site.Region.Name}";
            }
            if (site.CurrentOwner != null)
            {
                description += $" · current owner {site.CurrentOwner.Name}";
            }
            AddObject(site, HistoricalSearchCategory.Site, site.Type, description, year, dateLabel);
        }

        foreach (WorldRegion region in world.Regions)
        {
            AddObject(region, HistoricalSearchCategory.Place, region.Type,
                $"{region.Type} region · {region.Subtype}".Trim(' ', '·'));
        }
        foreach (UndergroundRegion region in world.UndergroundRegions)
        {
            AddObject(region, HistoricalSearchCategory.Place, region.Type, $"Underground region · {region.Type}");
        }
        foreach (Landmass landmass in world.Landmasses)
        {
            AddObject(landmass, HistoricalSearchCategory.Place, landmass.Type, $"{landmass.Type} landmass");
        }
        foreach (River river in world.Rivers)
        {
            AddObject(river, HistoricalSearchCategory.Place, "River", $"River · {river.Length} tiles");
        }
        foreach (MountainPeak peak in world.MountainPeaks)
        {
            AddObject(peak, HistoricalSearchCategory.Place, peak.Type,
                $"{peak.Type} · {peak.HeightMeter}");
        }
        foreach (Structure structure in world.Structures)
        {
            string description = $"{structure.Type} structure";
            if (structure.Site != null)
            {
                description += $" in {structure.Site.Name}";
            }
            AddObject(structure, HistoricalSearchCategory.Place, structure.Type, description);
        }
        foreach (WorldConstruction construction in world.WorldConstructions)
        {
            AddObject(construction, HistoricalSearchCategory.Place, construction.Type,
                $"{construction.Type} construction");
        }

        foreach (Artifact artifact in world.Artifacts)
        {
            if (string.IsNullOrWhiteSpace(artifact.Name))
            {
                continue;
            }
            WorldEvent? creation = artifact.Events.FirstOrDefault(item =>
                string.Equals(item.Type, "artifact created", StringComparison.OrdinalIgnoreCase));
            int? year = creation is { Year: >= 0 } ? creation.Year : null;
            string? dateLabel = year.HasValue ? $"Created {year}" : null;
            string type = string.IsNullOrWhiteSpace(artifact.Type) ? "Artifact" : artifact.Type;
            string description = type;
            if (!string.IsNullOrWhiteSpace(artifact.Material))
            {
                description += $" · {artifact.Material}";
            }
            if (artifact.Creator != null)
            {
                description += $" · created by {artifact.Creator.Name}";
            }
            if (artifact.Holder != null)
            {
                description += $" · held by {artifact.Holder.Name}";
            }
            AddObject(artifact, HistoricalSearchCategory.Artifact, type, description, year, dateLabel);
        }

        foreach (EventCollection collection in world.EventCollections)
        {
            HistoricalSearchCategory category = collection switch
            {
                War => HistoricalSearchCategory.War,
                Battle => HistoricalSearchCategory.Battle,
                _ => HistoricalSearchCategory.EventCollection
            };
            string type = string.IsNullOrWhiteSpace(collection.Type)
                ? category == HistoricalSearchCategory.EventCollection ? "Chronicle" : category.ToString()
                : collection.Type;
            string subtype = TextNormalizer.PlainText(collection.Subtype, 180);
            string description = $"{type} chronicle";
            if (subtype.Length > 0)
            {
                description += $" · {subtype}";
            }
            string? dateLabel = collection.StartYear >= 0
                ? collection.EndYear >= 0 ? $"{collection.StartYear}–{collection.EndYear}" : $"{collection.StartYear}–present"
                : null;
            AddObject(collection, category, type, description,
                collection.StartYear >= 0 ? collection.StartYear : null, dateLabel);
        }

        // Event rendering and tokenization dominate very large worlds. Process event boundaries in
        // independently built chunks, then merge them in event order to preserve deterministic
        // ordinal/ranking ties. Rendering uses Print(false), which only reads historical state.
        int eventCount = world.Events.Count;
        int workerCount = eventCount < 4_000 ? 1 : Math.Min(Environment.ProcessorCount, 6);
        List<SearchDocument>[] chunkDocuments = new List<SearchDocument>[workerCount];
        Dictionary<string, List<int>>[] chunkPostings = new Dictionary<string, List<int>>[workerCount];
        int[] chunkPreviewFailures = new int[workerCount];
        if (workerCount == 1)
        {
            BuildEventChunk(0, eventCount, 0);
        }
        else
        {
            Parallel.For(0, workerCount, new ParallelOptions { MaxDegreeOfParallelism = workerCount },
                chunk => BuildEventChunk(chunk * eventCount / workerCount, (chunk + 1) * eventCount / workerCount, chunk));
        }

        void BuildEventChunk(int start, int end, int chunk)
        {
            List<SearchDocument> localDocuments = new(end - start);
            Dictionary<string, List<int>> localPostings = new(StringComparer.Ordinal);
            HashSet<string> localTokens = new(StringComparer.Ordinal);
            int failures = 0;
            for (int index = start; index < end; index++)
            {
                WorldEvent historicalEvent = world.Events[index];
                string type = historicalEvent.GetEventInfo();
                if (string.IsNullOrWhiteSpace(type))
                {
                    type = "Historical event";
                }
                string normalizedType = TextNormalizer.Normalize(type);
                if (normalizedType.Length == 0)
                {
                    normalizedType = "historical event";
                }

                string rendered = string.Empty;
                try
                {
                    rendered = historicalEvent.Print(false);
                }
                catch
                {
                    failures++;
                }

                string tokens = normalizedType + " " + TextNormalizer.Normalize(rendered);
                int localOrdinal = localDocuments.Count;
                localDocuments.Add(new SearchDocument
                {
                    Ordinal = localOrdinal,
                    Category = HistoricalSearchCategory.Event,
                    Name = type,
                    NormalizedName = normalizedType,
                    Type = type,
                    Description = string.Empty,
                    Year = historicalEvent.Year >= 0 ? historicalEvent.Year : null,
                    DateLabel = historicalEvent.Year >= 0 ? historicalEvent.Year.ToString() : "Before time",
                    RecordId = historicalEvent.Id,
                    Key = $"event:{historicalEvent.Id}",
                    Url = historicalEvent.ParentCollection != null ? GetDetailUrl(historicalEvent.ParentCollection) : null,
                    Navigable = historicalEvent.ParentCollection != null,
                    Event = historicalEvent
                });
                localTokens.Clear();
                AddTokens(localPostings, localTokens, tokens, localOrdinal);
            }
            chunkDocuments[chunk] = localDocuments;
            chunkPostings[chunk] = localPostings;
            chunkPreviewFailures[chunk] = failures;
        }

        for (int chunk = 0; chunk < workerCount; chunk++)
        {
            int offset = documents.Count;
            foreach (SearchDocument document in chunkDocuments[chunk])
            {
                document.Ordinal = documents.Count;
                documents.Add(document);
            }
            foreach ((string token, List<int> localOrdinals) in chunkPostings[chunk])
            {
                if (!postingLists.TryGetValue(token, out List<int>? target))
                {
                    target = new List<int>(localOrdinals.Count);
                    postingLists[token] = target;
                }
                foreach (int ordinal in localOrdinals)
                {
                    target.Add(ordinal + offset);
                }
            }
            eventPreviewFailures += chunkPreviewFailures[chunk];
            chunkDocuments[chunk].Clear();
            chunkPostings[chunk].Clear();
        }

        HashSet<string> creatureIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (CreatureInfo creature in world.CreatureInfos)
        {
            if (creature == CreatureInfo.Unknown || string.IsNullOrWhiteSpace(creature.NameSingular) ||
                string.IsNullOrWhiteSpace(creature.Id) || !creatureIds.Add(creature.Id))
            {
                continue;
            }
            string name = TextNormalizer.Truncate(creature.NameSingular, 300);
            string normalizedName = TextNormalizer.Normalize(name);
            if (normalizedName.Length == 0)
            {
                continue;
            }
            string description = $"Creature definition · {creature.NamePlural} · id {creature.Id}";
            AddDocument(new SearchDocument
            {
                Category = HistoricalSearchCategory.Creature,
                Name = name,
                NormalizedName = normalizedName,
                Type = "Creature",
                Description = description,
                RecordId = null,
                Key = $"creature:{creature.Id}",
                Url = null,
                Navigable = false
            }, normalizedName + " creature " + TextNormalizer.Normalize(creature.NamePlural) + " " + TextNormalizer.Normalize(creature.Id));
        }

        string[] sortedTokens = postingLists.Keys.OrderBy(token => token, StringComparer.Ordinal).ToArray();
        Dictionary<string, int[]> immutablePostings = new(postingLists.Count, StringComparer.Ordinal);
        long estimatedBytes = 0;
        foreach ((string token, List<int> list) in postingLists)
        {
            int[] values = [.. list];
            immutablePostings[token] = values;
            estimatedBytes += token.Length * sizeof(char) + 24 + values.Length * sizeof(int);
        }
        foreach (SearchDocument document in documents)
        {
            estimatedBytes += 120 + document.Name.Length * sizeof(char) + document.Description.Length * sizeof(char) +
                              document.NormalizedName.Length * sizeof(char) + document.Key.Length * sizeof(char);
        }

        stopwatch.Stop();
        Dictionary<HistoricalSearchCategory, int> counts = documents
            .GroupBy(document => document.Category)
            .ToDictionary(group => group.Key, group => group.Count());
        return new HistoricalSearchIndex([.. documents], immutablePostings, sortedTokens, counts,
            (int)Math.Min(stopwatch.Elapsed.TotalMilliseconds, int.MaxValue), estimatedBytes, eventPreviewFailures);
    }

    public HistoricalSearchResponse Search(string query, IReadOnlySet<HistoricalSearchCategory>? categories,
        int? fromYear, int? toYear, int page, int pageSize)
    {
        string normalizedQuery = TextNormalizer.Normalize(query);
        string[] queryTokens = TextNormalizer.Tokenize(normalizedQuery);
        bool hasYearFilter = fromYear.HasValue || toYear.HasValue;
        Dictionary<HistoricalSearchCategory, int> matchedCounts = [];
        HistoricalSearchResponse response = new()
        {
            Query = query.Trim(),
            Page = page,
            PageSize = pageSize,
            IndexBuildMilliseconds = BuildMilliseconds,
            IndexedRecordCount = DocumentCount,
            AvailableCategoryCounts = categoryCounts.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
            UnavailableCategories = Enum.GetValues<HistoricalSearchCategory>()
                .Where(category => !categoryCounts.TryGetValue(category, out int count) || count == 0)
                .Select(category => category.ToString())
                .ToList()
        };

        if (queryTokens.Length == 0)
        {
            return response;
        }

        HashSet<int> candidates = FindCandidates(normalizedQuery, queryTokens, categories, out _);
        List<ScoredDocument> matched = new(candidates.Count);
        foreach (int ordinal in candidates)
        {
            SearchDocument document = documents[ordinal];
            if (categories is { Count: > 0 } && !categories.Contains(document.Category))
            {
                continue;
            }
            if (hasYearFilter && (!document.Year.HasValue ||
                                  (fromYear.HasValue && document.Year.Value < fromYear.Value) ||
                                  (toYear.HasValue && document.Year.Value > toYear.Value)))
            {
                continue;
            }

            (int rank, string matchKind) = Score(document, normalizedQuery, queryTokens);
            matched.Add(new ScoredDocument(document, rank, matchKind));
            matchedCounts[document.Category] = matchedCounts.GetValueOrDefault(document.Category) + 1;
        }

        matched.Sort(CompareScoredDocuments);
        response.TotalCount = matched.Count;
        response.TotalPages = matched.Count == 0 ? 0 : (int)Math.Ceiling(matched.Count / (double)pageSize);
        // Candidate scanning may be capped by broad event-text postings while a category filter
        // still has far fewer than the cap. Only warn when the returned matches themselves reach
        // the bound, which is the case where additional matching records could be omitted.
        response.Truncated = matched.Count >= MaximumCandidates;
        if (response.Truncated)
        {
            response.Warning = "The result set is very large. Refine the search to narrow it down.";
        }
        response.Items = matched
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => ToResult(item.Document, item.Rank, item.MatchKind))
            .ToList();
        response.MatchedCategoryCounts = matchedCounts.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value);
        return response;
    }

    private HashSet<int> FindCandidates(string normalizedQuery, string[] queryTokens,
        IReadOnlySet<HistoricalSearchCategory>? categories, out bool truncated)
    {
        truncated = false;
        List<HashSet<int>> tokenCandidates = new(queryTokens.Length);
        bool tokenPostingsMissing = false;
        foreach (string token in queryTokens)
        {
            HashSet<int> tokenSet = GetTokenCandidates(token, categories, ref truncated);
            if (tokenSet.Count == 0)
            {
                // A missing token posting does not rule out an infix/phrase match in a
                // named record, so continue to the bounded name scan below.
                tokenPostingsMissing = true;
                break;
            }
            tokenCandidates.Add(tokenSet);
            if (tokenSet.Count >= MaximumCandidates)
            {
                truncated = true;
            }
        }

        HashSet<int> candidates = [];
        if (!tokenPostingsMissing)
        {
            tokenCandidates.Sort((left, right) => left.Count.CompareTo(right.Count));
            candidates = tokenCandidates[0];
            for (int index = 1; index < tokenCandidates.Count; index++)
            {
                candidates.IntersectWith(tokenCandidates[index]);
                if (candidates.Count == 0)
                {
                    break;
                }
            }
        }

        // Postings provide token-prefix matching. This bounded scan adds infix matches such as
        // "dor" in "Thodor" for named records; events are intentionally excluded to keep it cheap.
        if (normalizedQuery.Length >= 2)
        {
            foreach (int index in namedDocumentIndexes)
            {
                if (candidates.Count >= MaximumCandidates)
                {
                    truncated = true;
                    break;
                }
                SearchDocument document = documents[index];
                if ((categories is not { Count: > 0 } || categories.Contains(document.Category)) &&
                    document.NormalizedName.Contains(normalizedQuery, StringComparison.Ordinal))
                {
                    candidates.Add(index);
                }
            }
        }

        return candidates;
    }

    private static void AddTokens(Dictionary<string, List<int>> postings, HashSet<string> seen, string normalizedText,
        int ordinal)
    {
        foreach (string token in TextNormalizer.Tokenize(normalizedText))
        {
            if (!seen.Add(token))
            {
                continue;
            }
            if (!postings.TryGetValue(token, out List<int>? list))
            {
                list = new List<int>(4);
                postings[token] = list;
            }
            list.Add(ordinal);
        }
    }

    private HashSet<int> GetTokenCandidates(string token, IReadOnlySet<HistoricalSearchCategory>? categories,
        ref bool truncated)
    {
        HashSet<int> result = [];
        if (token.Length == 1)
        {
            if (postings.TryGetValue(token, out int[]? exact))
            {
                AddPostings(result, exact, categories, ref truncated);
            }
            return result;
        }

        int start = FindTokenPrefixStart(token);
        int expanded = 0;
        for (int index = start; index < sortedTokens.Length; index++)
        {
            string candidateToken = sortedTokens[index];
            if (!candidateToken.StartsWith(token, StringComparison.Ordinal))
            {
                break;
            }
            if (++expanded > MaximumPrefixTokens)
            {
                truncated = true;
                break;
            }
            AddPostings(result, postings[candidateToken], categories, ref truncated);
            if (result.Count >= MaximumCandidates)
            {
                truncated = true;
                break;
            }
        }
        return result;
    }

    private int FindTokenPrefixStart(string prefix)
    {
        int low = 0;
        int high = sortedTokens.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (string.CompareOrdinal(sortedTokens[middle], prefix) < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        return low;
    }

    private void AddPostings(HashSet<int> target, int[] values,
        IReadOnlySet<HistoricalSearchCategory>? categories, ref bool truncated)
    {
        foreach (int value in values)
        {
            if (target.Count >= MaximumCandidates)
            {
                truncated = true;
                return;
            }
            if (categories is { Count: > 0 } && !categories.Contains(documents[value].Category))
            {
                continue;
            }
            target.Add(value);
        }
    }

    private static (int Rank, string MatchKind) Score(SearchDocument document, string query, string[] tokens)
    {
        // An individual event's "name" is its canonical type label, not a unique proper name.
        // Exact proper-name matches must outrank a broad event-type query.
        if (document.Category == HistoricalSearchCategory.Event)
        {
            if (string.Equals(document.NormalizedName, query, StringComparison.Ordinal))
            {
                return (500, "event type");
            }
            if (document.NormalizedName.StartsWith(query, StringComparison.Ordinal))
            {
                return (480, "event type prefix");
            }
        }

        if (string.Equals(document.NormalizedName, query, StringComparison.Ordinal))
        {
            return (1_000, "exact name");
        }
        if (document.NormalizedName.StartsWith(query, StringComparison.Ordinal))
        {
            return (900, "name prefix");
        }

        string[] nameTokens = TextNormalizer.Tokenize(document.NormalizedName);
        bool allExact = tokens.All(queryToken => nameTokens.Contains(queryToken, StringComparer.Ordinal));
        if (allExact)
        {
            return (800, "name word");
        }
        bool allPrefix = tokens.All(queryToken =>
            nameTokens.Any(nameToken => nameToken.StartsWith(queryToken, StringComparison.Ordinal)));
        if (allPrefix)
        {
            return (700, "name word prefix");
        }
        if (document.NormalizedName.Contains(query, StringComparison.Ordinal))
        {
            return (650, "partial name");
        }
        if (document.Category == HistoricalSearchCategory.Event &&
            string.Equals(document.Type, query, StringComparison.Ordinal))
        {
            return (600, "event type");
        }
        return (300, document.Category == HistoricalSearchCategory.Event ? "event text" : "description");
    }

    private static int CompareScoredDocuments(ScoredDocument left, ScoredDocument right)
    {
        int rank = right.Rank.CompareTo(left.Rank);
        if (rank != 0) return rank;
        int length = left.Document.NormalizedName.Length.CompareTo(right.Document.NormalizedName.Length);
        if (length != 0) return length;
        int year = (left.Document.Year ?? int.MaxValue).CompareTo(right.Document.Year ?? int.MaxValue);
        if (year != 0) return year;
        int category = left.Document.Category.CompareTo(right.Document.Category);
        if (category != 0) return category;
        return string.CompareOrdinal(left.Document.Key, right.Document.Key);
    }

    private HistoricalSearchResultDto ToResult(SearchDocument document, int rank, string matchKind)
    {
        string description = document.Description;
        if (document.Event != null)
        {
            // Event previews are rendered only for the bounded result page and never stored in the index.
            lock (eventPreviewGate)
            {
                try
                {
                    description = TextNormalizer.PlainText(document.Event.Print(false), 360);
                }
                catch
                {
                    description = $"{document.Name} in {document.DateLabel ?? "an unknown year"}.";
                }
            }
            if (description.Length == 0)
            {
                description = $"{document.Name} in {document.DateLabel ?? "an unknown year"}.";
            }
        }

        return new HistoricalSearchResultDto
        {
            Key = document.Key,
            Category = document.Category,
            CategoryLabel = GetCategoryLabel(document.Category),
            Id = document.RecordId,
            Name = document.Name,
            Type = document.Type,
            Description = description,
            Year = document.Year,
            DateLabel = document.DateLabel,
            Url = document.Url,
            Navigable = document.Navigable,
            MatchKind = matchKind,
            Rank = rank
        };
    }

    public static string GetCategoryLabel(HistoricalSearchCategory category) => category switch
    {
        HistoricalSearchCategory.Figure => "Historical figures",
        HistoricalSearchCategory.Entity => "Civilizations & entities",
        HistoricalSearchCategory.Site => "Sites",
        HistoricalSearchCategory.Place => "Places",
        HistoricalSearchCategory.Artifact => "Artifacts",
        HistoricalSearchCategory.War => "Wars",
        HistoricalSearchCategory.Battle => "Battles",
        HistoricalSearchCategory.EventCollection => "Chronicles",
        HistoricalSearchCategory.Event => "Events",
        HistoricalSearchCategory.Creature => "Creatures",
        _ => category.ToString()
    };

    private static string? GetDetailUrl(WorldObject worldObject) => worldObject switch
    {
        HistoricalFigure figure => $"/hf/{figure.Id}",
        Entity entity => $"/entity/{entity.Id}",
        Site site => $"/site/{site.Id}",
        WorldRegion region => $"/region/{region.Id}",
        UndergroundRegion region => $"/uregion/{region.Id}",
        Landmass landmass => $"/landmass/{landmass.Id}",
        River river => $"/river/{river.Id}",
        MountainPeak peak => $"/mountainpeak/{peak.Id}",
        Structure structure => $"/structure/{structure.Id}",
        WorldConstruction construction => $"/construction/{construction.Id}",
        Artifact artifact => $"/artifact/{artifact.Id}",
        War war => $"/war/{war.Id}",
        Battle battle => $"/battle/{battle.Id}",
        Duel duel => $"/duel/{duel.Id}",
        Raid raid => $"/raid/{raid.Id}",
        SiteConquered conquered => $"/siteconquered/{conquered.Id}",
        Insurrection insurrection => $"/insurrection/{insurrection.Id}",
        Persecution persecution => $"/persecution/{persecution.Id}",
        Purge purge => $"/purge/{purge.Id}",
        EntityOverthrownCollection coup => $"/coup/{coup.Id}",
        BeastAttack attack => $"/beastattack/{attack.Id}",
        Abduction abduction => $"/abduction/{abduction.Id}",
        Theft theft => $"/theft/{theft.Id}",
        ProcessionCollection procession => $"/procession/{procession.Id}",
        PerformanceCollection performance => $"/performance/{performance.Id}",
        Journey journey => $"/journey/{journey.Id}",
        CompetitionCollection competition => $"/competition/{competition.Id}",
        CeremonyCollection ceremony => $"/ceremony/{ceremony.Id}",
        Occasion occasion => $"/occasion/{occasion.Id}",
        _ => null
    };

    private sealed class SearchDocument
    {
        public int Ordinal { get; set; }
        public HistoricalSearchCategory Category { get; init; }
        public string Name { get; init; } = string.Empty;
        public string NormalizedName { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public int? Year { get; init; }
        public string? DateLabel { get; init; }
        public int? RecordId { get; init; }
        public string Key { get; init; } = string.Empty;
        public string? Url { get; init; }
        public bool Navigable { get; init; }
        public WorldEvent? Event { get; init; }
    }

    private sealed record ScoredDocument(SearchDocument Document, int Rank, string MatchKind);
}
