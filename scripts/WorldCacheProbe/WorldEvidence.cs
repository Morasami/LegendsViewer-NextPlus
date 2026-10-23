using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LegendsViewer.Backend.Legends;
using LegendsViewer.Backend.Legends.Events;
using LegendsViewer.Backend.Legends.Various;
using LegendsViewer.Backend.Legends.WorldObjects;

namespace WorldCacheProbe;

public static class WorldEvidence
{
    // Observable behavior, not serializer output. Every event is rendered and every primary
    // object exercises its lookup, navigation and presentation helpers. Hashes keep real-world
    // evidence small without omitting events from the comparison.
    public static string Behavior(World world, Action<string>? observe = null)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(object? value)
        {
            var line = JsonSerializer.Serialize(value); observe?.Invoke(line);
            hash.AppendData(Encoding.UTF8.GetBytes(line + "\n"));
        }
        Add(new { world.Name, world.AlternativeName, world.Width, world.Height, world.CurrentYear, world.CurrentMonth, world.CurrentDay });
        foreach (var hf in world.HistoricalFigures)
        {
            if (!ReferenceEquals(hf, world.GetHistoricalFigure(hf.Id))) throw new Exception("Figure lookup identity");
            Add(new { hf.Id, Link = hf.ToLink(), Icon = hf.GetIcon(), hf.Age, Race = hf.Race.Id, hf.RelatedHistoricalFigureList, hf.RelatedEntityList,
                hf.RelatedSiteList, hf.WorshippedDeities, hf.WorshippingFiguresList, hf.VagueRelationshipList, hf.SkillDescriptions, hf.DedicatedStructuresLinks });
        }
        foreach (var entity in world.Entities)
        {
            if (!ReferenceEquals(entity, world.GetEntity(entity.Id))) throw new Exception("Entity lookup identity");
            Add(new { entity.Id, Link = entity.ToLink(), entity.CurrentSiteList, entity.WorshippedLinks, Groups = entity.Groups.Select(g => g.Id), Wars = entity.Wars.Select(w => w.Id) });
            foreach (var honor in entity.Honors) Add(honor.PrintRequirementsAsString());
        }
        foreach (var site in world.Sites)
        {
            if (!ReferenceEquals(site, world.GetSite(site.Id))) throw new Exception("Site lookup identity");
            Add(new { site.Id, Link = site.ToLink(), Owner = site.CurrentOwner?.Id, Civ = site.CurrentCiv?.Id, Region = site.Region?.Id });
        }
        foreach (var artifact in world.Artifacts)
        {
            if (!ReferenceEquals(artifact, world.GetArtifact(artifact.Id))) throw new Exception("Artifact lookup identity");
            Add(new { artifact.Id, Link = artifact.ToLink() });
        }
        var eventPositions = new Dictionary<WorldEvent, int>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < world.Events.Count; i++) eventPositions.TryAdd(world.Events[i], i);
        foreach (var e in world.Events)
        {
            var lookup = world.GetEvent(e.Id);
            // Some legacy plus events share IDs with main-export events. Preserve the parser's
            // existing lookup result and list position rather than assuming IDs are unique here.
            Add(new { e.Id, LookupIndex = lookup == null ? -1 : eventPositions.GetValueOrDefault(lookup, -1), Type = e.GetType().Name, Text = e.Print(), Parent = e.ParentCollection?.Id });
        }
        foreach (var collection in world.EventCollections)
        {
            if (!ReferenceEquals(collection, world.GetEventCollection(collection.Id))) throw new Exception("Collection lookup identity");
            Add(new { collection.Id, Type = collection.GetType().Name, Text = collection.ToLink(), Events = collection.AllEvents.Select(e => e.Id) });
        }
        foreach (var battle in world.Battles) Add(new { battle.Id, battle.DeathsByRace, battle.AttackerDeathCount, battle.DefenderDeathCount, Notable = battle.NotableDeaths.Select(h => h.Id) });
        foreach (var war in world.Wars) Add(new { war.Id, war.DeathCount, Battles = war.Battles.Select(b => b.Id), war.AttackerToDefenderVictories });
        foreach (var pop in world.CivilizedPopulations.Concat(world.SitePopulations).Concat(world.OutdoorPopulations).Concat(world.UndergroundPopulations)
            .Concat(world.Entities.SelectMany(e => e.Populations))) Add(new { pop.Race.Id, pop.Count, pop.IsMainRace, pop.IsAnimalPeople, pop.IsOutcasts, pop.IsVisitors, pop.IsSlaves, pop.IsPrisoners });
        Add(WorldRecordsCalculator.Calculate(world));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    // Independent graph walk: all model fields (including private fields) are compared,
    // with a bijection proving shared objects were neither duplicated nor conflated.
    public static int CompareGraph(World original, World restored)
    {
        var forward = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        var reverse = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<(object?, object?)>(); queue.Enqueue((original, restored));
        while (queue.TryDequeue(out var pair))
        {
            var (a, b) = pair;
            if (a == null || b == null) { if (a != b) throw new Exception("Null field mismatch"); continue; }
            var t = a.GetType();
            if (t != b.GetType()) throw new Exception("Concrete type mismatch: " + t);
            if (a is string || t.IsPrimitive || t.IsEnum || t.Namespace == "System.Drawing" || a is decimal)
            { if (!a.Equals(b)) throw new Exception("Value mismatch: " + t); continue; }
            if (a is StringBuilder) { if (a.ToString() != b.ToString()) throw new Exception("Log mismatch"); continue; }
            if (!t.IsValueType)
            {
                if (forward.TryGetValue(a, out var old)) { if (!ReferenceEquals(old, b)) throw new Exception("Shared reference duplicated"); continue; }
                if (reverse.ContainsKey(b)) throw new Exception("Distinct references conflated");
                forward.Add(a, b); reverse.Add(b, a);
            }
            if (ReferenceEquals(a, CreatureInfo.Unknown) && !ReferenceEquals(b, CreatureInfo.Unknown)) throw new Exception("Unknown creature singleton lost");
            if (a is IDictionary da && b is IDictionary db)
            {
                if (da.Count != db.Count) throw new Exception("Dictionary size");
                var ea = da.GetEnumerator(); var eb = db.GetEnumerator();
                while (ea.MoveNext() && eb.MoveNext()) { queue.Enqueue((ea.Key, eb.Key)); queue.Enqueue((ea.Value, eb.Value)); }
            }
            else if (a is IList la && b is IList lb)
            {
                if (la.Count != lb.Count) throw new Exception("List size");
                for (var i = 0; i < la.Count; i++) queue.Enqueue((la[i], lb[i]));
            }
            else for (var baseType = t; baseType != null && baseType != typeof(object); baseType = baseType.BaseType)
                foreach (var f in baseType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.Name is "_formatting" or "_relationships" or "_battleInfo" or "_familyTreeData" or "_jobs" or "_warGraphData" or "_civilizationInfo" or "_icon" or "_title" or "_battleGraphData" or "_cachedWorldRecords" or "_requirementsStringCache") continue;
                    queue.Enqueue((f.GetValue(a), f.GetValue(b)));
                }
        }
        return forward.Count;
    }
}
