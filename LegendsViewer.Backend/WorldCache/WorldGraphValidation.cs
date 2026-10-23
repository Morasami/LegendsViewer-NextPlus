using LegendsViewer.Backend.Legends;
using LegendsViewer.Backend.Legends.WorldObjects;

namespace LegendsViewer.Backend.WorldCache;

public static class WorldGraphValidation
{
    public static void Validate(World world)
    {
        if (world.Width <= 0 || world.Height <= 0 || world.Width > 4096 || world.Height > 4096)
            throw new InvalidDataException("Invalid world dimensions.");
        void Table<T>(IEnumerable<T> items, Func<int, T?> lookup) where T : DwarfObject
        {
            var ids = new HashSet<int>();
            foreach (var item in items)
                if (item == null || !ids.Add(item.Id) || !ReferenceEquals(item, lookup(item.Id))) throw new InvalidDataException("Invalid canonical " + typeof(T).Name + " table.");
        }
        Table(world.HistoricalFigures, world.GetHistoricalFigure); Table(world.Entities, world.GetEntity);
        Table(world.Sites, world.GetSite); Table(world.Artifacts, world.GetArtifact);
        var figures = world.HistoricalFigures.ToHashSet(ReferenceEqualityComparer.Instance);
        var entities = world.Entities.ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var obj in world.HistoricalFigures.Cast<WorldObject>().Concat(world.Entities).Concat(world.Sites).Concat(world.Artifacts).Concat(world.EventCollections))
            if (obj.World != null && !ReferenceEquals(obj.World, world)) throw new InvalidDataException("Invalid world back-reference.");
        foreach (var e in world.Events)
            if (e == null || e.World != null && !ReferenceEquals(e.World, world)) throw new InvalidDataException("Invalid event world reference.");
        foreach (var battle in world.Battles)
            if (battle.NotableAttackers.Concat(battle.NotableDefenders).Any(h => !figures.Contains(h))) throw new InvalidDataException("Invalid battle participant.");
        foreach (var site in world.Sites)
            if (site.CurrentOwner != null && !entities.Contains(site.CurrentOwner)) throw new InvalidDataException("Invalid site owner.");
        var completed = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var start in world.EventCollections)
        {
            var path = new HashSet<object>(ReferenceEqualityComparer.Instance);
            for (var current = start; current != null && !completed.Contains(current); current = current.ParentCollection)
                if (!path.Add(current)) throw new InvalidDataException("Cyclic collection parents.");
            completed.UnionWith(path);
        }
    }
}
