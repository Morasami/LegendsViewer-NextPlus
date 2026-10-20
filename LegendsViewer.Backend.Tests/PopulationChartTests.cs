using LegendsViewer.Backend.Legends;
using LegendsViewer.Backend.Legends.Various;
using LegendsViewer.Backend.Legends.WorldObjects;

namespace LegendsViewer.Backend.Tests;

[TestClass]
public class PopulationChartTests
{
    [TestMethod]
    public void SiteChartPreservesCountsAndOrdersRacesByPopulation()
    {
        using var world = new World();
        var site = new Site([], world) { Populations = [
            new Population(world, new CreatureInfo("ELF"), 3),
            new Population(world, new CreatureInfo("DWARF"), 12)] };
        var chart = site.PopulationsByRace;
        CollectionAssert.AreEqual(new[] { 12, 3 }, chart.Datasets.Single().Data.ToArray());
        Assert.AreEqual(2, chart.Labels.Count);
        Assert.AreEqual(2, chart.Datasets.Single().BackgroundColor.Count);
    }

    [TestMethod]
    public void EntityChartPreservesCountsAndHandlesEmptyPopulation()
    {
        using var world = new World();
        var entity = new Entity([], world);
        Assert.AreEqual(0, entity.PopulationsByRace.Datasets.Single().Data.Count);
        entity.Populations.Add(new Population(world, new CreatureInfo("DWARF"), 12));
        Assert.AreEqual(12, entity.PopulationsByRace.Datasets.Single().Data.Single());
    }
}
