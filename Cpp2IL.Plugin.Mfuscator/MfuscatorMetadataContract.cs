using System.Text;

namespace Cpp2IL.Plugin.Mfuscator;

public sealed record MfuscatorPayloadTransform(int Initial, int Step);

public enum MfuscatorMutationKind
{
    Swap,
    Xor
}

public sealed record MfuscatorHeaderMutation(
    MfuscatorMutationKind Kind,
    int A = -1,
    int B = -1,
    int Offset = -1,
    byte Value = 0);

public sealed record MfuscatorSectionContract(
    string Name,
    int CustomOffsetField,
    int CustomSizeField,
    int? RecordSize = null,
    int Alignment = 4,
    MfuscatorPayloadTransform? PayloadTransform = null);

public readonly record struct MfuscatorSectionDescriptor(uint Offset, uint Size);

public sealed class MfuscatorMetadataContract
{
    public string Name { get; }
    public uint MetadataMagic { get; }
    public uint MetadataVersion { get; }
    public byte[] HeaderPrefix { get; }
    public int HeaderOffset { get; }
    public int HeaderLength { get; }
    public int PayloadStart { get; }
    public int PreservedTailStart { get; }
    public uint SourceBias { get; }
    public byte[] XxteaKey { get; }
    public IReadOnlyList<MfuscatorSectionContract> Sections { get; }
    public IReadOnlyList<MfuscatorHeaderMutation> Mutations { get; }

    public MfuscatorMetadataContract(
        string name,
        uint metadataMagic,
        uint metadataVersion,
        byte[] headerPrefix,
        int headerOffset,
        int headerLength,
        int payloadStart,
        int preservedTailStart,
        uint sourceBias,
        byte[] xxteaKey,
        IReadOnlyList<MfuscatorSectionContract> sections,
        IReadOnlyList<MfuscatorHeaderMutation> mutations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(headerPrefix);
        ArgumentNullException.ThrowIfNull(xxteaKey);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(mutations);
        if (headerOffset < 0 || headerLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(headerLength), "Header range is invalid.");
        if (payloadStart < 0 || preservedTailStart < 0)
            throw new ArgumentOutOfRangeException(nameof(payloadStart), "Payload range is invalid.");
        if (xxteaKey.Length != 16)
            throw new ArgumentException("XXTEA key must contain exactly 16 bytes.", nameof(xxteaKey));
        var duplicate = sections.GroupBy(section => section.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Duplicate section name: {duplicate.Key}", nameof(sections));
        if (sections.Any(section => section.Alignment <= 0 || section.CustomOffsetField < 0 ||
                                    section.CustomSizeField < 0 || section.RecordSize <= 0))
            throw new ArgumentException("Section contract contains invalid dimensions.", nameof(sections));

        Name = name;
        MetadataMagic = metadataMagic;
        MetadataVersion = metadataVersion;
        HeaderPrefix = headerPrefix.ToArray();
        HeaderOffset = headerOffset;
        HeaderLength = headerLength;
        PayloadStart = payloadStart;
        PreservedTailStart = preservedTailStart;
        SourceBias = sourceBias;
        XxteaKey = xxteaKey.ToArray();
        Sections = sections.ToArray();
        Mutations = mutations.ToArray();
    }

    public static MfuscatorMetadataContract Everlusting { get; } = new(
        "everlusting-life-v31",
        0xFAB11BAF,
        31,
        [0xE1, 0xB0, 0x6C, 0xFE, 0x1F, 0x8E, 0x7D, 0xB9],
        4,
        676,
        684,
        256,
        0x1E4,
        Encoding.ASCII.GetBytes("fc48e86b730833ef"),
        new MfuscatorSectionContract[]
        {
            new("stringLiteral", 0x0DC, 0x1AC),
            new("stringLiteralData", 0x0B4, 0x26C, PayloadTransform: new(0x0D, -1)),
            new("string", 0x19C, 0x0AC, PayloadTransform: new(0x5F, 1)),
            new("events", 0x04C, 0x124),
            new("properties", 0x0C4, 0x134),
            new("methods", 0x1F8, 0x040),
            new("parameterDefaultValues", 0x234, 0x10C),
            new("fieldDefaultValues", 0x160, 0x08C),
            new("fieldAndParameterDefaultValueData", 0x284, 0x074),
            new("fieldMarshaledSizes", 0x1D4, 0x014),
            new("parameters", 0x00C, 0x298),
            new("fields", 0x180, 0x158),
            new("genericParameters", 0x000, 0x0F0),
            new("genericParameterConstraints", 0x280, 0x068),
            new("genericContainers", 0x1E4, 0x1F0),
            new("nestedTypes", 0x224, 0x28C),
            new("interfaces", 0x1BC, 0x12C),
            new("vtableMethods", 0x018, 0x1B8),
            new("interfaceOffsets", 0x16C, 0x1CC),
            new("typeDefinitions", 0x210, 0x248),
            new("images", 0x21C, 0x140),
            new("assemblies", 0x0BC, 0x080),
            new("fieldRefs", 0x208, 0x268),
            new("referencedAssemblies", 0x09C, 0x200),
            new("attributeData", 0x0E8, 0x100),
            new("attributeDataRange", 0x118, 0x238),
            new("unresolvedVirtualCallParameterTypes", 0x058, 0x104),
            new("unresolvedVirtualCallParameterRanges", 0x0D0, 0x0EC),
            new("windowsRuntimeTypeNames", 0x278, 0x14C),
            new("windowsRuntimeStrings", 0x258, 0x174),
            new("exportedTypeDefinitions", 0x18C, 0x030)
        },
        new MfuscatorHeaderMutation[]
        {
            new(MfuscatorMutationKind.Swap, A: 0, B: 671),
            new(MfuscatorMutationKind.Xor, Offset: 0x09, Value: 0x27),
            new(MfuscatorMutationKind.Xor, Offset: 0x05, Value: 0x59)
        });
}
