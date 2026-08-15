using System.Collections.Generic;
using NUnit.Framework;

namespace Cpp2IL.Plugin.Mfuscator.Tests;

[TestFixture]
public class MethodPointerMapSerializationTests
{
    [Test]
    public void Method_pointer_map_serializes_with_generated_metadata_and_lowercase_schema()
    {
        var rows = new List<MethodPointerMapEntry>
        {
            new(
                "Assembly-CSharp",
                "Example.Type",
                "Run",
                "System.Void Example.Type::Run()",
                "0x1234",
                "0x234")
        };

        var json = MethodPointerMapJson.Serialize(rows);

        Assert.That(json, Does.Contain("\"assembly\": \"Assembly-CSharp\""));
        Assert.That(json, Does.Contain("\"rva\": \"0x234\""));
    }
}
