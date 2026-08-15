using System.Collections.Generic;
#if NETFRAMEWORK
using System.Text;
#else
using System.Text.Json;
using System.Text.Json.Serialization;
#endif

namespace Cpp2IL;

#if !NETFRAMEWORK
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<MethodPointerMapEntry>))]
internal partial class MethodPointerMapJsonContext : JsonSerializerContext
{
}
#endif

public static class MethodPointerMapJson
{
#if NETFRAMEWORK
    public static string Serialize(List<MethodPointerMapEntry> rows)
    {
        var output = new StringBuilder(rows.Count * 256);
        output.Append('[');
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (index > 0)
                output.Append(',');
            output.Append("\n  {\n")
                .Append("    \"assembly\": \"").Append(Escape(row.Assembly)).Append("\",\n")
                .Append("    \"type\": \"").Append(Escape(row.Type)).Append("\",\n")
                .Append("    \"method\": \"").Append(Escape(row.Method)).Append("\",\n")
                .Append("    \"signature\": \"").Append(Escape(row.Signature)).Append("\",\n")
                .Append("    \"pointer\": \"").Append(Escape(row.Pointer)).Append("\",\n")
                .Append("    \"rva\": \"").Append(Escape(row.Rva)).Append("\"\n")
                .Append("  }");
        }
        if (rows.Count > 0)
            output.Append('\n');
        return output.Append(']').ToString();
    }

    private static string Escape(string value)
    {
        var output = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            switch (character)
            {
                case '\"': output.Append("\\\""); break;
                case '\\': output.Append("\\\\"); break;
                case '\b': output.Append("\\b"); break;
                case '\f': output.Append("\\f"); break;
                case '\n': output.Append("\\n"); break;
                case '\r': output.Append("\\r"); break;
                case '\t': output.Append("\\t"); break;
                default:
                    if (character < 0x20)
                        output.Append("\\u").Append(((int)character).ToString("x4"));
                    else
                        output.Append(character);
                    break;
            }
        }
        return output.ToString();
    }
#else
    public static string Serialize(List<MethodPointerMapEntry> rows) =>
        JsonSerializer.Serialize(rows, MethodPointerMapJsonContext.Default.ListMethodPointerMapEntry);
#endif
}
