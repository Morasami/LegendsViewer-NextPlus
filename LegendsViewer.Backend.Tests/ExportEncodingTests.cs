using System.Text;
using LegendsViewer.Backend.Legends;
using LegendsViewer.Backend.Legends.Parser;

namespace LegendsViewer.Backend.Tests;

[TestClass]
public class ExportEncodingTests
{
    [DataTestMethod]
    [DataRow("utf-8")]
    [DataRow("IBM437")]
    public async Task XmlImportPreservesPremiumAndLegacyNames(string encoding)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"CP437\"?><df_world><sites><site><id>1</id><name>?lves</name></site></sites></df_world>", Encoding.GetEncoding(encoding));
            using var world = new World();
            using var parser = new XmlParser(world, path, null);
            await parser.ParseAsync();
            Assert.AreEqual("?lves", world.Sites.Single().Name);
        }
        finally { File.Delete(path); }
    }
}
