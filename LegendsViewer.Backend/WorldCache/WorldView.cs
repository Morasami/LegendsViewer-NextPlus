using LegendsViewer.Backend.Legends.EventCollections;
using LegendsViewer.Backend.Legends.Events;
using LegendsViewer.Backend.Legends.Parser;
using LegendsViewer.Backend.Legends.Various;
using LegendsViewer.Backend.Legends.WorldObjects;
using System.Drawing;
using System.Text;

using LegendsViewer.Backend.Legends.Interfaces;
using LegendsViewer.Backend.Legends;

namespace LegendsViewer.Backend.WorldCache;

// Non-disposable request view: DI must not dispose the shared concrete World at scope end.
public sealed class WorldView(LegendsViewer.Backend.Legends.World value) : IWorld
{
    public LegendsViewer.Backend.Legends.World Value { get; } = value;
    public string Name { get => Value.Name; set => Value.Name = value; }
    public string AlternativeName { get => Value.AlternativeName; set => Value.AlternativeName = value; }
    public int Width { get => Value.Width; set => Value.Width = value; }
    public int Height { get => Value.Height; set => Value.Height = value; }
    public int CurrentYear { get => Value.CurrentYear; set => Value.CurrentYear = value; }
    public int CurrentMonth { get => Value.CurrentMonth; set => Value.CurrentMonth = value; }
    public int CurrentDay { get => Value.CurrentDay; set => Value.CurrentDay = value; }
    public List<WorldRegion> Regions => Value.Regions;
    public List<UndergroundRegion> UndergroundRegions => Value.UndergroundRegions;
    public List<Landmass> Landmasses => Value.Landmasses;
    public List<MountainPeak> MountainPeaks => Value.MountainPeaks;
    public List<Identity> Identities => Value.Identities;
    public List<River> Rivers => Value.Rivers;
    public List<Site> Sites => Value.Sites;
    public List<HistoricalFigure> HistoricalFigures => Value.HistoricalFigures;
    public List<Entity> Entities => Value.Entities;
    public List<War> Wars => Value.Wars;
    public List<Battle> Battles => Value.Battles;
    public List<BeastAttack> BeastAttacks => Value.BeastAttacks;
    public List<Era> Eras => Value.Eras;
    public List<Artifact> Artifacts => Value.Artifacts;
    public List<WorldConstruction> WorldConstructions => Value.WorldConstructions;
    public List<PoeticForm> PoeticForms => Value.PoeticForms;
    public List<MusicalForm> MusicalForms => Value.MusicalForms;
    public List<DanceForm> DanceForms => Value.DanceForms;
    public List<WrittenContent> WrittenContents => Value.WrittenContents;
    public List<Structure> Structures => Value.Structures;
    public List<WorldEvent> Events => Value.Events;
    public List<EventCollection> EventCollections => Value.EventCollections;
    public List<EntityPopulation> EntityPopulations => Value.EntityPopulations;
    public List<Population> SitePopulations => Value.SitePopulations;
    public List<Population> CivilizedPopulations => Value.CivilizedPopulations;
    public List<Population> OutdoorPopulations => Value.OutdoorPopulations;
    public List<Population> UndergroundPopulations => Value.UndergroundPopulations;
    public List<Duel> Duels => Value.Duels;
    public List<Insurrection> Insurrections => Value.Insurrections;
    public List<Persecution> Persecutions => Value.Persecutions;
    public List<Purge> Purges => Value.Purges;
    public List<Raid> Raids => Value.Raids;
    public List<SiteConquered> SiteConquerings => Value.SiteConquerings;
    public List<EntityOverthrownCollection> Coups => Value.Coups;
    public List<Abduction> Abductions => Value.Abductions;
    public List<Theft> Thefts => Value.Thefts;
    public List<ProcessionCollection> Processions => Value.Processions;
    public List<PerformanceCollection> Performances => Value.Performances;
    public List<Journey> Journeys => Value.Journeys;
    public List<CompetitionCollection> Competitions => Value.Competitions;
    public List<CeremonyCollection> Ceremonies => Value.Ceremonies;
    public List<Occasion> Occasions => Value.Occasions;
    public StringBuilder Log => Value.Log;
    public ParsingErrors ParsingErrors => Value.ParsingErrors;
    public List<WorldObject> PlayerRelatedObjects => Value.PlayerRelatedObjects;
    public Dictionary<CreatureInfo, Color> MainRaces => Value.MainRaces;
    public List<CreatureInfo> CreatureInfos => Value.CreatureInfos;
    public Dictionary<int, WorldEvent> SpecialEventsById => Value.SpecialEventsById;
    public Dictionary<Location, WorldRegion> WorldGrid => Value.WorldGrid;
    public Dictionary<string, List<HistoricalFigure>> Breeds => Value.Breeds;
    public Task ParseAsync(string xmlFile, string? xmlPlusFile, string? historyFile, string? sitesAndPopulationsFile, string? mapFile) => Value.ParseAsync(xmlFile, xmlPlusFile, historyFile, sitesAndPopulationsFile, mapFile);
    public CreatureInfo GetCreatureInfo(string id) => Value.GetCreatureInfo(id);
    public void AddCreatureInfo(CreatureInfo creatureInfo) => Value.AddCreatureInfo(creatureInfo);
    public void AddPlayerRelatedDwarfObjects(WorldObject dwarfObject) => Value.AddPlayerRelatedDwarfObjects(dwarfObject);
    public void AddHFtoHfLink(HistoricalFigure hf, Property link) => Value.AddHFtoHfLink(hf, link);
    public void AddHFtoEntityLink(HistoricalFigure hf, Property link) => Value.AddHFtoEntityLink(hf, link);
    public void AddHFtoSiteLink(HistoricalFigure hf, Property link) => Value.AddHFtoSiteLink(hf, link);
    public void AddEntityEntityLink(Entity entity, Property property) => Value.AddEntityEntityLink(entity, property);
    public void AddReputation(HistoricalFigure hf, Property link) => Value.AddReputation(hf, link);
    public void ProcessHFtoHfLinks() => Value.ProcessHFtoHfLinks();
    public void ProcessHFtoEntityLinks() => Value.ProcessHFtoEntityLinks();
    public void ProcessHFtoSiteLinks() => Value.ProcessHFtoSiteLinks();
    public void ProcessEntityEntityLinks() => Value.ProcessEntityEntityLinks();
    public void ProcessReputations() => Value.ProcessReputations();
    public Artifact? GetArtifact(int id) => Value.GetArtifact(id);
    public DanceForm? GetDanceForm(int id) => Value.GetDanceForm(id);
    public Entity? GetEntity(int id) => Value.GetEntity(id);
    public EntityPopulation? GetEntityPopulation(int id) => Value.GetEntityPopulation(id);
    public Era? GetEra(int id) => Value.GetEra(id);
    public WorldEvent? GetEvent(int id) => Value.GetEvent(id);
    public EventCollection? GetEventCollection(int id) => Value.GetEventCollection(id);
    public HistoricalFigure? GetHistoricalFigure(int id) => Value.GetHistoricalFigure(id);
    public MusicalForm? GetMusicalForm(int id) => Value.GetMusicalForm(id);
    public PoeticForm? GetPoeticForm(int id) => Value.GetPoeticForm(id);
    public WorldRegion? GetRegion(int id) => Value.GetRegion(id);
    public Site? GetSite(int id) => Value.GetSite(id);
    public Structure? GetStructure(int id) => Value.GetStructure(id);
    public UndergroundRegion? GetUndergroundRegion(int id) => Value.GetUndergroundRegion(id);
    public WorldConstruction? GetWorldConstruction(int id) => Value.GetWorldConstruction(id);
    public WrittenContent? GetWrittenContent(int id) => Value.GetWrittenContent(id);
    public Landmass? GetLandmass(int id) => Value.GetLandmass(id);
    public River? GetRiver(int id) => Value.GetRiver(id);
    public MountainPeak? GetMountainPeak(int id) => Value.GetMountainPeak(id);
    public void Clear() => Value.Clear();
    public T? GetEventCollection<T>(int id) where T : EventCollection => Value.GetEventCollection<T>(id);
}
