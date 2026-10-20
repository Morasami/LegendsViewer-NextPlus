using LegendsViewer.Backend.Legends.Bookmarks;
using System.Collections.Concurrent;
using System.Text.Json;

namespace LegendsViewer.Backend.Tests;

[TestClass]
public class BookmarkServiceTests : IDisposable
{
    private readonly string _testFolder;

    public BookmarkServiceTests()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), $"BookmarkTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testFolder);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testFolder))
            {
                Directory.Delete(_testFolder, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private string BookmarkFilePath => Path.Combine(_testFolder, "bookmarks.json");

    private IBookmarkService CreateService() => new BookmarkService(BookmarkFilePath);

    [TestMethod]
    public void AddBookmark_SingleBookmark_ShouldPersist()
    {
        // Arrange
        var service = CreateService();
        var bookmark = CreateTestBookmark(
            "/path/to/world-00001-01-01-legends.xml",
            "TestWorld",
            "00001-01-01"
        );

        // Act
        var result = service.AddBookmark(bookmark);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("TestWorld", result.WorldName);
        var all = service.GetAll();
        Assert.AreEqual(1, all.Count);
    }

    [TestMethod]
    public void AddBookmark_SameWorldTwice_ShouldNotCreateDuplicates()
    {
        // Arrange - simulates loading the same world multiple times (Issue: duplicate detection)
        var service = CreateService();
        var bookmark1 = CreateTestBookmark(
            "/path/to/world-00001-01-01-legends.xml",
            "TestWorld",
            "00001-01-01"
        );
        var bookmark2 = CreateTestBookmark(
            "/path/to/world-00001-01-01-legends.xml",
            "TestWorld",
            "00001-01-01",
            additionalTimestamps: new[] { "2-2-2" }
        );

        // Act
        service.AddBookmark(bookmark1);
        service.AddBookmark(bookmark2);

        // Assert - should have exactly one bookmark with both timestamps merged
        var all = service.GetAll();
        Assert.AreEqual(1, all.Count, "Should not create duplicate bookmarks for the same world");
        Assert.AreEqual(2, all[0].WorldTimestamps.Count, "Should merge timestamps from both loads");
        Assert.IsTrue(all[0].WorldTimestamps.Contains("00001-01-01"));
        Assert.IsTrue(all[0].WorldTimestamps.Contains("2-2-2"));
    }

    [TestMethod]
    public void AddBookmark_DifferentWorlds_ShouldCreateSeparateBookmarks()
    {
        // Arrange
        var service = CreateService();
        var bookmark1 = CreateTestBookmark("/path/to/world1-00001-01-01-legends.xml", "World1", "00001-01-01");
        var bookmark2 = CreateTestBookmark("/path/to/world2-00002-02-02-legends.xml", "World2", "2-2-2");

        // Act
        service.AddBookmark(bookmark1);
        service.AddBookmark(bookmark2);

        // Assert
        var all = service.GetAll();
        Assert.AreEqual(2, all.Count);
    }

    [TestMethod]
    public void GetBookmark_WithValidPath_ShouldRetrieveBookmark()
    {
        // Arrange
        var service = CreateService();
        var bookmark = CreateTestBookmark(
            "/path/to/world-00001-01-01-legends.xml",
            "TestWorld",
            "00001-01-01"
        );
        service.AddBookmark(bookmark);

        // Act
        var result = service.GetBookmark("/path/to/world-00001-01-01-legends.xml");

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("TestWorld", result.WorldName);
    }

    [TestMethod]
    public void GetBookmark_WithNonExistentPath_ShouldReturnNull()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.GetBookmark("/nonexistent/path-legends.xml");

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void DeleteBookmarkTimestamp_WithValidTimestamp_ShouldRemoveTimestamp()
    {
        // Arrange - create actual temp file
        var tempFile = Path.Combine(_testFolder, "world-00001-01-01-legends.xml");
        File.WriteAllText(tempFile, "<test/>");
        
        var service = CreateService();
        var bookmark = CreateTestBookmark(
            tempFile,
            "TestWorld",
            "00001-01-01",
            additionalTimestamps: new[] { "00002-02-02", "00003-03-03" }
        );
        service.AddBookmark(bookmark);

        // Act - delete one timestamp
        var result = service.DeleteBookmarkTimestamp(tempFile);

        // Assert
        Assert.IsTrue(result.Success, "Delete should return true for valid timestamp");
        Assert.IsFalse(result.FileMissing, "FileMissing should be false when file exists");
        var all = service.GetAll();
        Assert.AreEqual(1, all.Count);
        Assert.IsFalse(all[0].WorldTimestamps.Contains("00001-01-01"), "Deleted timestamp should be removed");
        Assert.IsTrue(all[0].WorldTimestamps.Contains("00002-02-02"));
        Assert.IsTrue(all[0].WorldTimestamps.Contains("00003-03-03"));
    }

    [TestMethod]
    public void DeleteBookmarkTimestamp_WhenLastTimestamp_ShouldRemoveEntireBookmark()
    {
        // Arrange - create actual temp file
        var tempFile = Path.Combine(_testFolder, "world-00001-01-01-legends.xml");
        File.WriteAllText(tempFile, "<test/>");
        
        var service = CreateService();
        var bookmark = CreateTestBookmark(
            tempFile,
            "TestWorld",
            "00001-01-01"
        );
        service.AddBookmark(bookmark);

        // Act
        var result = service.DeleteBookmarkTimestamp(tempFile);

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsFalse(result.FileMissing, "FileMissing should be false when file exists");
        var all = service.GetAll();
        Assert.AreEqual(0, all.Count, "Bookmark should be completely removed when last timestamp is deleted");
    }

    [TestMethod]
    public void DeleteBookmarkTimestamp_WithMissingFile_ShouldReturnFalse()
    {
        // Arrange - Issue #25 edge case: file no longer exists on disk
        var service = CreateService();

        // Act - try to delete a non-existent bookmark
        var result = service.DeleteBookmarkTimestamp("/nonexistent/world-00001-01-01-legends.xml");

        // Assert
        Assert.IsFalse(result.Success, "Delete should return false for non-existent bookmark");
        Assert.IsTrue(result.FileMissing, "FileMissing should be true when file doesn't exist");
    }

    [TestMethod]
    public void DeleteBookmarkTimestamp_WithMissingFile_ButTimestampExistsInOtherBookmark_ShouldSucceed()
    {
        // Arrange - file is missing but the timestamp exists in another bookmark
        var service = CreateService();
        var bookmark1 = CreateTestBookmark(
            "/path/to/world1-00001-01-01-legends.xml",
            "TestWorld1",
            "00001-01-01"
        );
        var bookmark2 = CreateTestBookmark(
            "/path/to/world2-00002-02-02-legends.xml",
            "TestWorld2",
            "00002-02-02"
        );
        service.AddBookmark(bookmark1);
        service.AddBookmark(bookmark2);

        // Act - delete with a non-existent file path that has a timestamp present in bookmark1
        // The file "/nonexistent/world1-00001-01-01-legends.xml" doesn't exist, 
        // but timestamp "00001-01-01" exists in bookmark1
        var result = service.DeleteBookmarkTimestamp("/nonexistent/world1-00001-01-01-legends.xml");

        // Assert - should succeed because timestamp was found
        Assert.IsTrue(result.Success, "Delete should succeed when timestamp exists in a bookmark");
        Assert.IsTrue(result.FileMissing, "FileMissing should be true when file doesn't exist");
        var all = service.GetAll();
        Assert.AreEqual(1, all.Count, "Only bookmark2 should remain");
        Assert.IsTrue(all[0].WorldTimestamps.Contains("00002-02-02"));
    }

    [TestMethod]
    public void DeleteBookmarkTimestamp_WithMultipleTimestamps_OneFileMissing_ShouldOnlyRemoveDeletedTimestamp()
    {
        // Arrange - bookmark with multiple timestamps, one file is missing
        var service = CreateService();
        var bookmark = CreateTestBookmark(
            "/path/to/world-00001-01-01-legends.xml",
            "TestWorld",
            "00001-01-01",
            additionalTimestamps: new[] { "00002-02-02", "00003-03-03" }
        );
        service.AddBookmark(bookmark);

        // Act - delete when file for first timestamp doesn't exist
        var result = service.DeleteBookmarkTimestamp("/nonexistent/world-00001-01-01-legends.xml");

        // Assert
        Assert.IsTrue(result.Success, "Delete should succeed");
        Assert.IsTrue(result.FileMissing, "FileMissing should be true");
        var all = service.GetAll();
        Assert.AreEqual(1, all.Count, "Bookmark should still exist with remaining timestamps");
        Assert.IsFalse(all[0].WorldTimestamps.Contains("00001-01-01"), "Deleted timestamp should be removed");
        Assert.IsTrue(all[0].WorldTimestamps.Contains("00002-02-02"));
        Assert.IsTrue(all[0].WorldTimestamps.Contains("00003-03-03"));
    }

    [TestMethod]
    public void GetRegionNameAndTimestampByRegionId_WithValidId_ShouldParseCorrectly()
    {
        // Arrange
        var regionId = "TheWorld-00001-01-01";

        // Act
        var (regionName, timestamp) = BookmarkService.GetRegionNameAndTimestampByRegionId(regionId);

        // Assert
        Assert.AreEqual("TheWorld", regionName);
        Assert.AreEqual("00001-01-01", timestamp);
    }

    [TestMethod]
    public void GetRegionNameAndTimestampByRegionId_WithComplexWorldName_ShouldParseCorrectly()
    {
        // Arrange - path encoding test: world names with special characters
        var regionId = "World-With-Special-Name-12345-06-15";

        // Act
        var (regionName, timestamp) = BookmarkService.GetRegionNameAndTimestampByRegionId(regionId);

        // Assert
        Assert.AreEqual("World-With-Special-Name", regionName);
        Assert.AreEqual("12345-06-15", timestamp);
    }

    [TestMethod]
    public void ExtractTimestampFromFilePath_WithStandardFormat_ShouldExtractCorrectly()
    {
        // Arrange
        var testCases = new (string filePath, string expectedTimestamp)[]
        {
            ("/path/to/world-00001-01-01-legends.xml", "00001-01-01"),
            ("/path/to/world-01253-12-31-legends_plus.xml", "01253-12-31"),
            ("/path/to/region-12345-06-15-legends.xml", "12345-06-15"),
        };

        foreach (var (filePath, expectedTimestamp) in testCases)
        {
            // Act
            var result = BookmarkService.ExtractTimestampFromFilePath(filePath);

            // Assert
            Assert.AreEqual(expectedTimestamp, result, $"Failed for path: {filePath}");
        }
    }

    [TestMethod]
    public void GetRegionNameAndTimestampByRegionId_WithInvalidId_ShouldReturnEmpty()
    {
        // Arrange
        var regionId = "invalid";

        // Act
        var (regionName, timestamp) = BookmarkService.GetRegionNameAndTimestampByRegionId(regionId);

        // Assert
        Assert.AreEqual("", regionName);
        Assert.AreEqual("", timestamp);
    }

    [TestMethod]
    public void ReplaceLastOccurrence_WithMultipleOccurrences_ShouldReplaceOnlyLast()
    {
        // Arrange
        var source = "/path/world-00001-01-01/world-00001-01-01-legends.xml";
        var find = "00001-01-01";
        var replace = BookmarkService.TimestampPlaceholder;

        // Act
        var result = BookmarkService.ReplaceLastOccurrence(source, find, replace);

        // Assert
        Assert.AreEqual("/path/world-00001-01-01/world-{TIMESTAMP}-legends.xml", result);
    }

    [TestMethod]
    public void ReplaceLastOccurrence_WithNoOccurrence_ShouldReturnOriginal()
    {
        // Arrange
        var source = "/path/world-legends.xml";
        var find = "nonexistent";
        var replace = BookmarkService.TimestampPlaceholder;

        // Act
        var result = BookmarkService.ReplaceLastOccurrence(source, find, replace);

        // Assert
        Assert.AreEqual(source, result);
    }

    [TestMethod]
    public void Concurrent_AddBookmark_FromMultipleThreads_ShouldNotCorruptState()
    {
        // Arrange - thread-safety test: verify concurrent operations don't corrupt data
        var service = CreateService();
        var exceptions = new ConcurrentBag<Exception>();
        var barrier = new Barrier(10); // Sync 10 threads
        var successfulAdds = 0;

        // Act
        Parallel.For(0, 10, i =>
        {
            try
            {
                barrier.SignalAndWait(); // Wait for all threads to be ready
                var bookmark = CreateTestBookmark(
                    $"/path/world{i}-00001-01-01-legends.xml",
                    $"World{i}",
                    "00001-01-01"
                );
                service.AddBookmark(bookmark);
                Interlocked.Increment(ref successfulAdds);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        // Assert - no exceptions should occur
        Assert.AreEqual(0, exceptions.Count, $"Thread-safety issues detected: {string.Join(", ", exceptions.Select(e => e.Message))}");
        Assert.AreEqual(10, successfulAdds, "All 10 bookmark additions should succeed");
    }

    [TestMethod]
    public void Concurrent_AddAndGet_FromMultipleThreads_ShouldHandleGracefully()
    {
        // Arrange - thread-safety test: mixed read/write operations
        var service = CreateService();
        var exceptions = new ConcurrentBag<Exception>();
        var successfulOps = 0;
        
        // Pre-populate some bookmarks
        for (int i = 0; i < 5; i++)
        {
            var bookmark = CreateTestBookmark(
                $"/path/world{i}-00001-01-01-legends.xml",
                $"World{i}",
                "00001-01-01"
            );
            service.AddBookmark(bookmark);
        }

        var barrier = new Barrier(20);

        // Act - mixed operations
        Parallel.For(0, 20, i =>
        {
            try
            {
                barrier.SignalAndWait();
                if (i % 2 == 0)
                {
                    // Add operations
                    var bookmark = CreateTestBookmark(
                        $"/path/newWorld{i}-00001-01-01-legends.xml",
                        $"NewWorld{i}",
                        "00001-01-01"
                    );
                    service.AddBookmark(bookmark);
                    Interlocked.Increment(ref successfulOps);
                }
                else
                {
                    // Read operations
                    var all = service.GetAll();
                    if (all.Count > 0)
                    {
                        var first = all.FirstOrDefault();
                        if (first != null)
                        {
                            service.GetBookmark(first.FilePath);
                        }
                    }
                    Interlocked.Increment(ref successfulOps);
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        // Assert
        Assert.AreEqual(0, exceptions.Count, $"Thread-safety issues: {string.Join("; ", exceptions.Select(e => e.Message))}");
    }

    [TestMethod]
    public void AddBookmark_WithSpecialCharactersInPath_ShouldPersistCorrectly()
    {
        // Arrange - path encoding test: special characters in file paths
        // Bookmarks with same WorldName+WorldRegionName merge, so use different regions
        var service = CreateService();
        
        var testCases = new (string path, string worldRegionName, string timestamp)[]
        {
            ("/path/with spaces/regionA-00001-01-01-legends.xml", "regionA", "00001-01-01"),
            ("/path/with+plus/regionB-00002-02-02-legends.xml", "regionB", "00002-02-02"),
            ("/path/with#hash/regionC-00003-03-03-legends.xml", "regionC", "00003-03-03"),
            ("/path/with%percent/regionD-00004-04-04-legends.xml", "regionD", "00004-04-04"),
            ("/path/with&amp;ampersand/regionE-00005-05-05-legends.xml", "regionE", "00005-05-05"),
            ("/path/with$dollar/regionF-00006-06-06-legends.xml", "regionF", "00006-06-06"),
        };

        // Act
        foreach (var (path, worldRegionName, timestamp) in testCases)
        {
            var bookmark = CreateTestBookmark(path, "SpecialWorld", timestamp);
            bookmark.WorldRegionName = worldRegionName; // Use different region names
            var result = service.AddBookmark(bookmark);
            Assert.IsNotNull(result, $"Should add bookmark for path: {path}");
        }

        // Assert - each different region should create a separate bookmark
        Assert.AreEqual(testCases.Length, service.GetAll().Count);
    }

    [TestMethod]
    public void ExtractTimestampFromFilePath_WithTimestampPlaceholder_ShouldReturnPlaceholder()
    {
        // Arrange - {TIMESTAMP} placeholder should not be resolved by ExtractTimestampFromFilePath
        // The placeholder is resolved elsewhere (e.g., when loading bookmarks from file)
        var filePath = "region3-{TIMESTAMP}-legends.xml";

        // Act
        var result = BookmarkService.ExtractTimestampFromFilePath(filePath);

        // Assert - {TIMESTAMP} doesn't match YYYYY-MM-DD pattern, so returns empty
        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void LoadBookmarksFromFile_WithTimestampPlaceholder_ShouldMigrateCorrectly()
    {
        // Arrange - create a bookmarks.json file with {TIMESTAMP} placeholder (old format)
        var testBookmarkFile = Path.Combine(_testFolder, "legacy_bookmarks.json");
        // WorldMapImage is base64 encoded - use empty bytes
        var emptyBase64 = Convert.ToBase64String(Array.Empty<byte>());
        var legacyJson = @"{
    ""region3-{TIMESTAMP}-legends.xml"": {
        ""FilePath"": ""region3-{TIMESTAMP}-legends.xml"",
        ""WorldName"": ""TestWorld"",
        ""WorldAlternativeName"": ""Alt World"",
        ""WorldRegionName"": ""region3"",
        ""WorldTimestamps"": [""00100-01-01""],
        ""WorldWidth"": 128,
        ""WorldHeight"": 128,
        ""WorldMapImage"": """ + emptyBase64 + @""",
        ""State"": 0,
        ""LoadedTimestamp"": null,
        ""LatestTimestamp"": ""00100-01-01""
    }
}";
        File.WriteAllText(testBookmarkFile, legacyJson);

        // Act - create a new service that loads the legacy file
        var service = new BookmarkService(testBookmarkFile);
        var all = service.GetAll();

        // Assert - should have loaded the bookmark with correct Id
        Assert.AreEqual(1, all.Count, "Should load the legacy bookmark");
        Assert.AreEqual("TestWorld_region3", all[0].Id, "Id should be WorldName_RegionName");
        Assert.AreEqual("TestWorld", all[0].WorldName);
        Assert.IsTrue(all[0].WorldTimestamps.Contains("00100-01-01"));
    }

    [TestMethod]
    public void AddBookmark_AfterResetAll_ShouldResetOtherBookmarksState()
    {
        // Arrange
        var service = CreateService();
        var bookmark1 = CreateTestBookmark("/path/world1-00001-01-01-legends.xml", "World1", "00001-01-01");
        var bookmark2 = CreateTestBookmark("/path/world2-00002-02-02-legends.xml", "World2", "2-2-2");
        
        service.AddBookmark(bookmark1);
        bookmark1.State = BookmarkState.Loaded;
        service.AddBookmark(bookmark1); // This resets all, then sets bookmark1 back to Loaded
        
        // Act - add another bookmark, which triggers ResetAllBookmarks first
        service.AddBookmark(bookmark2);

        // Assert
        var all = service.GetAll();
        var world1 = all.FirstOrDefault(b => b.WorldName == "World1");
        var world2 = all.FirstOrDefault(b => b.WorldName == "World2");
        
        Assert.IsNotNull(world1);
        Assert.IsNotNull(world2);
        // World1 is reset to Default when adding world2 (due to ResetAllBookmarks)
        Assert.AreEqual(BookmarkState.Default, world1.State, "World1 state should be reset when adding new bookmark");
        // World2 was just added, so it's set to Loaded
        Assert.AreEqual(BookmarkState.Loaded, world2.State, "World2 should be in Loaded state");
    }

    [TestMethod]
    public void DefaultStoragePath_ShouldUseNextPlusApplicationData()
    {
        // Resolve without constructing the default service or touching real application data.
        var method = typeof(BookmarkService).GetMethod("GetDefaultBookmarkFilePath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(method);
        var path = (string?)method.Invoke(null, null);
        Assert.AreEqual(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LegendsViewer-NextPlus", "bookmarks.json"), path);
    }

    [TestMethod]
    public void SeparateStorage_ShouldNotLoadOrChangeOriginalBookmarks()
    {
        // Both directories are isolated fixtures, never the user's real AppData.
        var originalPath = Path.Combine(_testFolder, "LegendsViewer", "bookmarks.json");
        new BookmarkService(originalPath).AddBookmark(
            CreateTestBookmark("original-00001-01-01-legends.xml", "Original", "00001-01-01"));
        var originalBytes = File.ReadAllBytes(originalPath);
        var nextPlusPath = Path.Combine(_testFolder, "LegendsViewer-NextPlus", "bookmarks.json");
        var service = new BookmarkService(nextPlusPath);
        Assert.AreEqual(0, service.GetAll().Count);
        service.AddBookmark(CreateTestBookmark("next-00001-01-01-legends.xml", "NextPlus", "00001-01-01"));
        Assert.AreEqual("NextPlus", new BookmarkService(nextPlusPath).GetAll().Single().WorldName);
        CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(originalPath));
    }

    [TestMethod]
    public void Persistence_ShouldReloadMergedTimestampsAndDeletions()
    {
        var service = CreateService();
        var firstPath = Path.Combine(_testFolder, "world-00001-01-01-legends.xml");
        var secondPath = Path.Combine(_testFolder, "world-00002-01-01-legends.xml");
        service.AddBookmark(CreateTestBookmark(firstPath, "World", "00001-01-01"));
        service.AddBookmark(CreateTestBookmark(secondPath, "World", "00002-01-01"));
        var reloaded = new BookmarkService(BookmarkFilePath);
        var bookmark = reloaded.GetBookmark(secondPath);
        Assert.IsNotNull(bookmark);
        Assert.AreEqual("World_TestRegion", bookmark.Id);
        CollectionAssert.AreEquivalent(new[] { "00001-01-01", "00002-01-01" }, bookmark.WorldTimestamps);
        Assert.AreEqual(BookmarkState.Default, bookmark.State);
        Assert.IsNull(bookmark.LoadedTimestamp);
        Assert.AreEqual("00002-01-01", bookmark.LatestTimestamp);
        Assert.IsTrue(reloaded.DeleteBookmarkTimestamp(firstPath).Success);
        var afterDelete = new BookmarkService(BookmarkFilePath);
        CollectionAssert.AreEqual(new[] { "00002-01-01" }, afterDelete.GetAll().Single().WorldTimestamps);
        Assert.IsTrue(afterDelete.DeleteBookmarkTimestamp(secondPath).Success);
        Assert.AreEqual(0, new BookmarkService(BookmarkFilePath).GetAll().Count);
        Assert.AreEqual(0, Directory.GetFiles(_testFolder, "*.tmp").Length);
    }

    [TestMethod]
    public void BookmarkInputsAndResults_ShouldBeDetachedSnapshots()
    {
        var service = CreateService();
        var input = CreateTestBookmark("world-00001-01-01-legends.xml", "World", "00001-01-01",
            ["00002-01-01"]);
        input.WorldMapImage = [1, 2, 3];
        var added = service.AddBookmark(input);
        var all = service.GetAll();
        var found = service.GetBookmark(input.FilePath)!;
        input.WorldTimestamps.Clear();
        input.WorldMapImage[0] = 9;
        added.WorldTimestamps.Clear();
        added.WorldMapImage![0] = 8;
        all[0].WorldTimestamps.Clear();
        all[0].WorldMapImage![0] = 7;
        found.WorldTimestamps.Clear();
        found.WorldMapImage![0] = 6;
        var deleted = service.DeleteBookmarkTimestamp(input.FilePath);
        Assert.IsTrue(deleted.Success);
        Assert.IsNotNull(deleted.Bookmark);
        deleted.Bookmark.WorldTimestamps.Clear();
        var stored = service.GetAll().Single();
        CollectionAssert.AreEqual(new[] { "00002-01-01" }, stored.WorldTimestamps);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, stored.WorldMapImage);
        Assert.AreEqual(BookmarkState.Loaded, added.State);
        Assert.AreEqual("00001-01-01", added.LoadedTimestamp);
    }

    [TestMethod]
    public void Concurrent_SameWorldAdds_ShouldMergeEveryTimestampAndPersist()
    {
        var service = CreateService();
        Parallel.For(1, 21, i =>
        {
            var timestamp = $"{i:D5}-01-01";
            service.AddBookmark(CreateTestBookmark($"world-{timestamp}-legends.xml", "World", timestamp));
        });
        var bookmark = service.GetAll().Single();
        Assert.AreEqual("World_TestRegion", bookmark.Id);
        var expected = Enumerable.Range(1, 20).Select(i => $"{i:D5}-01-01").ToArray();
        CollectionAssert.AreEquivalent(expected, bookmark.WorldTimestamps);
        CollectionAssert.AreEquivalent(expected, new BookmarkService(BookmarkFilePath).GetAll().Single().WorldTimestamps);
        Assert.AreEqual(BookmarkState.Loaded, bookmark.State);
    }

    [TestMethod]
    public void Concurrent_AddDeleteAndRead_ShouldKeepConsistentSnapshotsAndPersist()
    {
        var service = CreateService();
        var timestamps = Enumerable.Range(1, 10).Select(i => $"{i:D5}-01-01").ToArray();
        service.AddBookmark(CreateTestBookmark("world-00001-01-01-legends.xml", "World", timestamps[0], timestamps[1..]));
        Parallel.For(0, 30, i =>
        {
            if (i < 10)
            {
                Assert.IsTrue(service.DeleteBookmarkTimestamp($"world-{timestamps[i]}-legends.xml").Success);
            }
            else if (i < 20)
            {
                var timestamp = $"{i + 1:D5}-01-01";
                service.AddBookmark(CreateTestBookmark($"world-{timestamp}-legends.xml", "World", timestamp));
            }
            else
            {
                // Enumerating/serializing a result outside the service lock must be safe.
                foreach (var bookmark in service.GetAll())
                {
                    Assert.AreEqual(bookmark.WorldTimestamps.Count, bookmark.WorldTimestamps.Distinct().Count());
                    JsonSerializer.Serialize(bookmark);
                }
                service.GetBookmark("world-00001-01-01-legends.xml");
            }
        });
        var expected = Enumerable.Range(11, 10).Select(i => $"{i:D5}-01-01").ToArray();
        CollectionAssert.AreEquivalent(expected, service.GetAll().Single().WorldTimestamps);
        CollectionAssert.AreEquivalent(expected, new BookmarkService(BookmarkFilePath).GetAll().Single().WorldTimestamps);
    }

    [TestMethod]
    public void Save_WithOpenReader_ShouldPreserveCompleteOldAndNewJson()
    {
        var service = CreateService();
        service.AddBookmark(CreateTestBookmark("world-00001-01-01-legends.xml", "World", "00001-01-01"));
        // Keep the previous file version open while a save replaces it. An in-place
        // truncate/write would change this reader's contents as well.
        using var previousVersion = new FileStream(BookmarkFilePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        service.AddBookmark(CreateTestBookmark("world-00002-01-01-legends.xml", "World", "00002-01-01"));
        var previous = JsonSerializer.Deserialize<Dictionary<string, Bookmark>>(previousVersion);
        Assert.IsNotNull(previous);
        CollectionAssert.AreEqual(new[] { "00001-01-01" }, previous["World_TestRegion"].WorldTimestamps);
        var current = new BookmarkService(BookmarkFilePath).GetAll().Single();
        CollectionAssert.AreEquivalent(new[] { "00001-01-01", "00002-01-01" }, current.WorldTimestamps);
    }

    [TestMethod]
    public void SaveFailure_ShouldPropagateAndCleanTemporaryFile()
    {
        var service = CreateService();
        // A directory at the destination deterministically prevents replacing it on all platforms.
        Directory.CreateDirectory(BookmarkFilePath);
        try
        {
            service.AddBookmark(CreateTestBookmark("world-00001-01-01-legends.xml", "World", "00001-01-01"));
            Assert.Fail("Saving to a directory must report a file error.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Platforms differ in how they report a directory blocking the destination.
        }
        Assert.AreEqual(0, Directory.GetFiles(_testFolder, "*.tmp").Length);
        Assert.IsTrue(Directory.Exists(BookmarkFilePath));
    }

    [TestMethod]
    public void LoadInvalidJson_ShouldPropagateError()
    {
        File.WriteAllText(BookmarkFilePath, "{invalid JSON");
        Assert.ThrowsException<JsonException>(() => new BookmarkService(BookmarkFilePath));
    }

    private static Bookmark CreateTestBookmark(
        string filePath,
        string worldName,
        string timestamp,
        string[]? additionalTimestamps = null)
    {
        var timestamps = new List<string> { timestamp };
        if (additionalTimestamps != null)
        {
            timestamps.AddRange(additionalTimestamps);
        }

        return new Bookmark
        {
            // Use the actual file path directly. The real BookmarkService extracts
            // Id from FilePath, so we pass the real path.
            FilePath = filePath,
            WorldName = worldName,
            WorldAlternativeName = $"Alt {worldName}",
            WorldRegionName = "TestRegion",
            WorldTimestamps = timestamps,
            WorldWidth = 256,
            WorldHeight = 256,
            State = BookmarkState.Default,
            LoadedTimestamp = timestamp,
            LatestTimestamp = timestamp
        };
    }
}
