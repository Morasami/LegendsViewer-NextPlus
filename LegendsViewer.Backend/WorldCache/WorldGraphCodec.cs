using System.Collections;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using LegendsViewer.Backend.Legends;
using LegendsViewer.Backend.Legends.Interfaces;
using LegendsViewer.Backend.Legends.Various;
using LegendsViewer.Backend.Legends.WorldObjects;

namespace LegendsViewer.Backend.WorldCache;

/// <summary>
/// Closed model-only graph format. No CLR names, callbacks, properties or parsing constructors
/// are executed from the input. Containers are encoded through their public elements, never
/// framework implementation fields. All references are resolved before dictionary insertion.
/// </summary>
public sealed class WorldGraphCodec
{
    public const int MaxNodes = 20_000_000;
    public const int MaxElements = 20_000_000;
    private const int MaxStringBytes = 4 * 1024 * 1024;
    private static readonly Dictionary<Type, FieldInfo[]> Fields = new();
    private static readonly Type[] Types;
    private static readonly Dictionary<Type, int> Codes;
    public static readonly string ContractVersion;

    static WorldGraphCodec()
    {
        var approved = GraphTypes.Allowed.ToHashSet();
        var types = new HashSet<Type> { typeof(string), typeof(StringBuilder) };
        void Visit(Type t)
        {
            t = Nullable.GetUnderlyingType(t) ?? t;
            if (t.IsPrimitive || t.IsEnum || t == typeof(decimal) || t == typeof(Color) || t == typeof(Point) || t == typeof(Rectangle)) return;
            if (t.IsInterface || t.IsAbstract) return; // concrete implementations still require a registered code
            if (!types.Add(t)) return;
            if (t.IsArray) { Visit(t.GetElementType()!); return; }
            if (t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(List<>) || t.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                || t.GetGenericTypeDefinition().FullName!.StartsWith("System.ValueTuple`", StringComparison.Ordinal)))
            { foreach (var a in t.GetGenericArguments()) Visit(a); if (!t.IsValueType) return; }
            else if (!approved.Contains(t)) throw new InvalidOperationException("Unaudited cache contract: " + t);
            var fields = new List<FieldInfo>();
            for (var b = t; b != null && b != typeof(object); b = b.BaseType)
                fields.AddRange(b.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(f => !Excluded(f)));
            Fields[t] = fields.OrderBy(f => f.DeclaringType!.FullName + "." + f.Name, StringComparer.Ordinal).ToArray();
            foreach (var f in fields) Visit(f.FieldType);
        }
        foreach (var t in approved) Visit(t);
        Types = types.OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();
        Codes = Types.Select((t, i) => (t, i)).ToDictionary(p => p.t, p => p.i);
        var schema = string.Join("\n", Types.Select(t => t.FullName + ":" + string.Join(",", Fields.GetValueOrDefault(t, [])
            .Select(f => f.DeclaringType!.FullName + "." + f.Name + "=" + f.FieldType.FullName))));
        ContractVersion = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schema)));
    }

    private static bool Excluded(FieldInfo f) => (f.DeclaringType!.Name, f.Name) switch
    {
        ("World", "_cachedWorldRecords") => true,
        ("HistoricalFigure", "_formatting" or "_relationships" or "_battleInfo" or "_familyTreeData" or "_jobs") => true,
        ("Entity", "_warGraphData" or "_civilizationInfo" or "_icon" or "_title") => true,
        ("War", "_battleGraphData") => true,
        ("Honor", "_requirementsStringCache") => true,
        _ => false
    };

    private static bool IsDictionary(Type t) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>);
    private static bool IsList(Type t) => t.IsArray || t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>);
    private static Type Element(Type t) => t.IsArray ? t.GetElementType()! : t.GetGenericArguments()[0];

    public int Write(Stream stream, World world)
    {
        var nodes = new List<object>();
        var refs = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        var strings = new Dictionary<string, int>(StringComparer.Ordinal);
        int Reference(object? value)
        {
            if (value == null) return -1;
            if (ReferenceEquals(value, CreatureInfo.Unknown)) return -2;
            if (value is string s && strings.TryGetValue(s, out var si)) return si;
            if (refs.TryGetValue(value, out var id)) return id;
            if (!Codes.ContainsKey(value.GetType())) throw new InvalidDataException("Unregistered graph type: " + value.GetType());
            id = nodes.Count;
            if (id >= MaxNodes) throw new InvalidDataException("Graph exceeds node limit.");
            refs.Add(value, id); nodes.Add(value);
            if (value is string text) strings.Add(text, id);
            return id;
        }
        void Discover(object? value, Type t)
        {
            t = Nullable.GetUnderlyingType(t) ?? t;
            if (!t.IsValueType) { Reference(value); return; }
            if (Fields.TryGetValue(t, out var fields) && value != null)
                foreach (var f in fields) Discover(f.GetValue(value), f.FieldType);
        }
        Reference(world);
        for (var i = 0; i < nodes.Count; i++)
        {
            var obj = nodes[i]; var t = obj.GetType();
            if (obj is string or StringBuilder) continue;
            if (IsDictionary(t))
            {
                var args = t.GetGenericArguments();
                foreach (DictionaryEntry entry in (IDictionary)obj) { Discover(entry.Key, args[0]); Discover(entry.Value, args[1]); }
            }
            else if (IsList(t)) foreach (var v in (IList)obj) Discover(v, Element(t));
            else foreach (var f in Fields[t]) Discover(f.GetValue(obj), f.FieldType);
        }
        using var w = new BinaryWriter(stream, Encoding.UTF8, true);
        w.Write(nodes.Count);
        // Allocation table allows cycles and forward references without deferred field fixups.
        foreach (var obj in nodes)
        {
            var t = obj.GetType(); w.Write(Codes[t]);
            if (obj is string or StringBuilder) WriteText(w, obj.ToString()!);
            else if (obj is ICollection c) w.Write(c.Count);
        }
        void Value(object? value, Type t)
        {
            if (Nullable.GetUnderlyingType(t) is { } underlying) { w.Write(value != null); if (value != null) Value(value, underlying); return; }
            if (!t.IsValueType) { w.Write(Reference(value)); return; }
            if (t.IsEnum) { w.Write(Convert.ToInt64(value)); return; }
            if (t == typeof(int)) w.Write((int)value!);
            else if (t == typeof(bool)) w.Write((bool)value!);
            else if (t == typeof(double)) w.Write((double)value!);
            else if (t == typeof(float)) w.Write((float)value!);
            else if (t == typeof(long)) w.Write((long)value!);
            else if (t == typeof(short)) w.Write((short)value!);
            else if (t == typeof(byte)) w.Write((byte)value!);
            else if (t == typeof(decimal)) w.Write((decimal)value!);
            else if (t == typeof(Color)) { var color = (Color)value!; w.Write(color.IsKnownColor ? 1 : color.IsNamedColor ? 2 : color.IsEmpty ? 3 : 0); w.Write(color.ToArgb()); WriteText(w, color.Name); }
            else if (t == typeof(Point)) { w.Write(((Point)value!).X); w.Write(((Point)value!).Y); }
            else if (t == typeof(Rectangle)) { var rect = (Rectangle)value!; w.Write(rect.X); w.Write(rect.Y); w.Write(rect.Width); w.Write(rect.Height); }
            else if (Fields.TryGetValue(t, out var fields)) foreach (var f in fields) Value(f.GetValue(value), f.FieldType);
            else throw new InvalidDataException("Unsupported scalar: " + t);
        }
        // Dictionaries follow all model fields, so keys are fully initialized before hashing.
        foreach (var obj in nodes.Where(o => !IsDictionary(o.GetType())))
        {
            var t = obj.GetType();
            if (obj is string or StringBuilder) continue;
            if (IsList(t)) foreach (var v in (IList)obj) Value(v, Element(t));
            else foreach (var f in Fields[t]) Value(f.GetValue(obj), f.FieldType);
        }
        foreach (var obj in nodes.Where(o => IsDictionary(o.GetType())))
        {
            var args = obj.GetType().GetGenericArguments();
            foreach (DictionaryEntry entry in (IDictionary)obj) { Value(entry.Key, args[0]); Value(entry.Value, args[1]); }
        }
        return nodes.Count;
    }

    public World Read(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, true);
        var count = Bound(r.ReadInt32(), MaxNodes);
        if (count == 0) throw new InvalidDataException("Empty graph.");
        var nodes = new object[count]; var sizes = new int[count];
        long totalElements = 0;
        for (var i = 0; i < count; i++)
        {
            var code = r.ReadInt32();
            if ((uint)code >= Types.Length) throw new InvalidDataException("Unknown type code.");
            var t = Types[code];
            if (t == typeof(string)) nodes[i] = ReadText(r);
            else if (t == typeof(StringBuilder)) nodes[i] = new StringBuilder(ReadText(r));
            else if (IsList(t) || IsDictionary(t))
            {
                sizes[i] = Bound(r.ReadInt32(), MaxElements);
                totalElements += sizes[i];
                if (totalElements > 100_000_000) throw new InvalidDataException("Graph exceeds aggregate element limit.");
                nodes[i] = t.IsArray ? Array.CreateInstance(Element(t), sizes[i]) : Activator.CreateInstance(t)!;
            }
            else if (GraphTypes.Allowed.Contains(t)) nodes[i] = RuntimeHelpers.GetUninitializedObject(t);
            else throw new InvalidDataException("Type cannot be allocated.");
        }
        object? Value(Type t)
        {
            if (Nullable.GetUnderlyingType(t) is { } underlying) return r.ReadBoolean() ? Value(underlying) : null;
            if (!t.IsValueType)
            {
                var id = r.ReadInt32();
                object? v = id switch { -1 => null, -2 => CreatureInfo.Unknown, _ when (uint)id < nodes.Length => nodes[id], _ => throw new InvalidDataException("Invalid reference.") };
                if (v != null && !t.IsInstanceOfType(v)) throw new InvalidDataException("Reference type mismatch.");
                return v;
            }
            if (t.IsEnum) return Enum.ToObject(t, r.ReadInt64());
            if (t == typeof(int)) return r.ReadInt32();
            if (t == typeof(bool)) return r.ReadBoolean();
            if (t == typeof(double)) return r.ReadDouble();
            if (t == typeof(float)) return r.ReadSingle();
            if (t == typeof(long)) return r.ReadInt64();
            if (t == typeof(short)) return r.ReadInt16();
            if (t == typeof(byte)) return r.ReadByte();
            if (t == typeof(decimal)) return r.ReadDecimal();
            if (t == typeof(Color)) { var kind = r.ReadInt32(); var argb = r.ReadInt32(); var name = ReadText(r); return kind switch { 0 => Color.FromArgb(argb), 1 or 2 => Color.FromName(name), 3 => Color.Empty, _ => throw new InvalidDataException("Invalid color.") }; }
            if (t == typeof(Point)) return new Point(r.ReadInt32(), r.ReadInt32());
            if (t == typeof(Rectangle)) return new Rectangle(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            var obj = Activator.CreateInstance(t)!;
            foreach (var f in Fields[t]) f.SetValue(obj, Value(f.FieldType));
            return obj;
        }
        for (var i = 0; i < count; i++)
        {
            var obj = nodes[i]; var t = obj.GetType();
            if (obj is string or StringBuilder || IsDictionary(t)) continue;
            if (IsList(t))
            {
                var list = (IList)obj;
                for (var j = 0; j < sizes[i]; j++) { var v = Value(Element(t)); if (t.IsArray) list[j] = v; else list.Add(v); }
            }
            else foreach (var f in Fields[t]) f.SetValue(obj, Value(f.FieldType));
        }
        for (var i = 0; i < count; i++)
        {
            if (!IsDictionary(nodes[i].GetType())) continue;
            var args = nodes[i].GetType().GetGenericArguments(); var dict = (IDictionary)nodes[i];
            for (var j = 0; j < sizes[i]; j++) dict.Add(Value(args[0])!, Value(args[1]));
        }
        if (r.BaseStream.ReadByte() != -1) throw new InvalidDataException("Trailing graph data.");
        if (nodes[0] is not World world) throw new InvalidDataException("Invalid root.");
        foreach (var obj in nodes)
        {
            if (obj is World && !ReferenceEquals(obj, world)) throw new InvalidDataException("Multiple world roots.");
            if (!Fields.TryGetValue(obj.GetType(), out var fields)) continue;
            foreach (var field in fields.Where(f => f.FieldType == typeof(IWorld)))
                if (field.GetValue(obj) is { } owner && !ReferenceEquals(owner, world)) throw new InvalidDataException("Invalid world owner.");
        }
        foreach (var field in Fields[typeof(World)].Where(f => !f.FieldType.IsValueType))
            if (field.GetValue(world) == null) throw new InvalidDataException("Missing world table.");
        foreach (var honor in nodes.OfType<Honor>()) honor.RestoreCacheHelpers();
        return world;
    }

    private static int Bound(int n, int maximum) => n >= 0 && n <= maximum ? n : throw new InvalidDataException("Cache length exceeds limit.");
    private static void WriteText(BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        Bound(bytes.Length, MaxStringBytes); w.Write(bytes.Length); w.Write(bytes);
    }
    private static string ReadText(BinaryReader r)
    {
        var n = Bound(r.ReadInt32(), MaxStringBytes); var bytes = r.ReadBytes(n);
        if (bytes.Length != n) throw new EndOfStreamException();
        return new UTF8Encoding(false, true).GetString(bytes);
    }
}
