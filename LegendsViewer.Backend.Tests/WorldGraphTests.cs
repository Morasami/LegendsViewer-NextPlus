using LegendsViewer.Backend.Legends;
using LegendsViewer.Backend.Legends.Events;
using LegendsViewer.Backend.Legends.EventCollections;
using LegendsViewer.Backend.Legends.Parser;
using LegendsViewer.Backend.Legends.Various;
using LegendsViewer.Backend.WorldCache;
using WorldCacheProbe;

namespace LegendsViewer.Backend.Tests;

[TestClass]
public class WorldGraphTests
{
    [DataTestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(WorldGraphCodec.MaxNodes + 1)]
    public void RejectsInvalidNodeCountsBeforeAllocation(int count)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(count); stream.Position = 0;
        Assert.ThrowsException<InvalidDataException>(() => new WorldGraphCodec().Read(stream));
    }

    [TestMethod]
    public void RejectsUnknownTypeCodes()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(1); writer.Write(int.MaxValue); stream.Position = 0;
        Assert.ThrowsException<InvalidDataException>(() => new WorldGraphCodec().Read(stream));
    }

    internal static async Task<World> Fixture()
    {
        var world = new World();
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "Xah_Atho-00005-01-01");
        await world.ParseAsync(root + "-legends.xml", root + "-legends_plus.xml", null, null, null);
        return world;
    }

    [TestMethod]
    public async Task FixturePreservesAllModelFieldsIdentityAndBehavior()
    {
        var world = await Fixture();
        RoundTrip(world);
    }

    [TestMethod]
    public async Task SyntheticWarfarePrivateEventStatePopulationsAndSentinelSurvive()
    {
        var world = await Fixture();
        List<Property> Props(params (string, string)[] pairs) => pairs.Select(p => new Property { Name = p.Item1, Value = p.Item2 }).ToList();
        var hf = world.HistoricalFigures[0]; var slayer = world.HistoricalFigures[1];
        var death = new HfDied(Props(("id", "900000"), ("year", "5"), ("type", "hf died"), ("cause", "synthetic unknown cause"),
            ("hfid", hf.Id.ToString()), ("slayer_hfid", slayer.Id.ToString())), world);
        world.Events.Add(death);
        var battle = new Battle(Props(("id", "900000"), ("name", "proof battle"), ("event", death.Id.ToString())), world);
        var war = new War(Props(("id", "900001"), ("name", "proof war")), world);
        war.EventCollections.Add(battle); battle.ParentCollection = war;
        world.EventCollections.Add(battle); world.EventCollections.Add(war);
        battle.NotableAttackers.Add(slayer); battle.NotableDefenders.Add(hf);
        world.CivilizedPopulations.Add(new Population(world, CreatureInfo.Unknown, 42));
        RoundTrip(world);
    }

    private static void RoundTrip(World world)
    {
        using var stream = new MemoryStream(); var codec = new WorldGraphCodec();
        codec.Write(stream, world); stream.Position = 0;
        var restored = codec.Read(stream);
        Assert.IsTrue(WorldEvidence.CompareGraph(world, restored) > 100);
        Assert.AreEqual(WorldEvidence.Behavior(world), WorldEvidence.Behavior(restored));
    }
}
