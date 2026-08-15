using System.Buffers.Binary;
using System.Text;

namespace Cpp2IL.Plugin.Mfuscator;

public static class CustomMetadataHeaderDecoder
{
    public const int HeaderOffset = 4;
    public const int HeaderLength = 676;

    private const uint Delta = 0x9E3779B9;

    public static bool IsSupportedMetadata(ReadOnlySpan<byte> metadata) =>
        IsSupportedMetadata(metadata, MfuscatorMetadataContract.Everlusting);

    public static bool IsSupportedMetadata(
        ReadOnlySpan<byte> metadata,
        MfuscatorMetadataContract contract) =>
        metadata.Length >= contract.HeaderOffset + contract.HeaderLength &&
        metadata.Length >= contract.HeaderPrefix.Length &&
        metadata[..contract.HeaderPrefix.Length].SequenceEqual(contract.HeaderPrefix);

    public static bool TryDecodeMetadataHeader(ReadOnlySpan<byte> metadata, out byte[] header)
        => TryDecodeMetadataHeader(metadata, MfuscatorMetadataContract.Everlusting, out header);

    public static bool TryDecodeMetadataHeader(
        ReadOnlySpan<byte> metadata,
        MfuscatorMetadataContract contract,
        out byte[] header)
    {
        if (!IsSupportedMetadata(metadata, contract))
        {
            header = [];
            return false;
        }

        header = Decrypt(
            metadata.Slice(contract.HeaderOffset, contract.HeaderLength), contract);
        return true;
    }

    public static bool TryRebuildMetadata(ReadOnlySpan<byte> metadata, out byte[] rebuilt)
    {
        return TryRebuildMetadata(
            metadata,
            MfuscatorMetadataContract.Everlusting,
            out rebuilt,
            out _);
    }

    public static bool TryRebuildMetadata(
        ReadOnlySpan<byte> metadata,
        MfuscatorMetadataContract contract,
        out byte[] rebuilt,
        out MfuscatorValidationReport validation)
    {
        if (!TryDecodeMetadataHeader(metadata, contract, out var customHeader))
        {
            rebuilt = [];
            validation = new(false, 0,
            [
                new MfuscatorValidationFinding(
                    "metadata.signature", "error", null, "Metadata does not match the contract signature.")
            ]);
            return false;
        }

        if (!ApplyLoaderHeaderMutations(customHeader, contract.Mutations))
        {
            rebuilt = [];
            validation = new(false, 0,
            [
                new MfuscatorValidationFinding(
                    "header.mutation", "error", null, "A loader-header mutation is out of bounds.")
            ]);
            return false;
        }

        var descriptors = new List<MfuscatorSectionDescriptor>(contract.Sections.Count);
        foreach (var section in contract.Sections)
        {
            if (section.CustomOffsetField + sizeof(uint) > customHeader.Length ||
                section.CustomSizeField + sizeof(uint) > customHeader.Length)
            {
                rebuilt = [];
                validation = new(false, 0,
                [
                    new MfuscatorValidationFinding(
                        "header.field", "error", section.Name,
                        "A randomized header field is out of bounds.")
                ]);
                return false;
            }
            descriptors.Add(new(
                ReadHeaderWord(customHeader, section.CustomOffsetField) + contract.SourceBias,
                ReadHeaderWord(customHeader, section.CustomSizeField)));
        }

        validation = MfuscatorMetadataValidator.Validate(metadata.Length, contract, descriptors);
        if (!validation.Accepted)
        {
            rebuilt = [];
            return false;
        }

        rebuilt = metadata.ToArray();
        var standardHeaderSize = 8 + contract.Sections.Count * 8;
        if (standardHeaderSize > rebuilt.Length)
        {
            rebuilt = [];
            validation = new(false, 0,
            [
                new MfuscatorValidationFinding(
                    "header.standard", "error", null, "Standard metadata header exceeds the input.")
            ]);
            return false;
        }
        for (var sectionIndex = 0; sectionIndex < descriptors.Count; sectionIndex++)
        {
            var target = rebuilt.AsSpan(8 + sectionIndex * 8, 8);
            BinaryPrimitives.WriteUInt32LittleEndian(target, descriptors[sectionIndex].Offset);
            BinaryPrimitives.WriteUInt32LittleEndian(target[4..], descriptors[sectionIndex].Size);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(rebuilt, contract.MetadataMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(rebuilt.AsSpan(4), contract.MetadataVersion);

        for (var index = 0; index < contract.Sections.Count; index++)
        {
            var transform = contract.Sections[index].PayloadTransform;
            if (transform is not null)
                ApplySequentialXor(rebuilt, descriptors[index], transform);
        }
        return true;
    }

    public static byte[] Decrypt(ReadOnlySpan<byte> encrypted)
        => Decrypt(encrypted, MfuscatorMetadataContract.Everlusting);

    public static byte[] Decrypt(
        ReadOnlySpan<byte> encrypted,
        MfuscatorMetadataContract contract)
    {
        if (encrypted.Length != contract.HeaderLength)
            throw new ArgumentException(
                $"The custom metadata header must be {contract.HeaderLength} bytes.", nameof(encrypted));

        var values = new uint[encrypted.Length / sizeof(uint)];
        for (var index = 0; index < values.Length; index++)
            values[index] = BinaryPrimitives.ReadUInt32LittleEndian(encrypted.Slice(index * sizeof(uint), sizeof(uint)));

        var key = DeriveKeyWords(contract.XxteaKey);
        var sum = unchecked((uint)(6 + 52 / values.Length) * Delta);

        while (sum != 0)
        {
            var y = values[0];
            for (var index = values.Length - 1; index > 0; index--)
            {
                var z = values[index - 1];
                y = values[index] = unchecked(values[index] - Mix(sum, y, z, index, key));
            }

            values[0] = unchecked(values[0] - Mix(sum, y, values[^1], 0, key));
            sum = unchecked(sum - Delta);
        }

        var decoded = new byte[encrypted.Length];
        for (var index = 0; index < values.Length; index++)
            BinaryPrimitives.WriteUInt32LittleEndian(decoded.AsSpan(index * sizeof(uint), sizeof(uint)), values[index]);

        decoded.AsSpan(decoded.Length - sizeof(uint)).Clear();
        return decoded;
    }

    private static bool ApplyLoaderHeaderMutations(
        Span<byte> header,
        IReadOnlyList<MfuscatorHeaderMutation> mutations)
    {
        foreach (var mutation in mutations)
        {
            if (mutation.Kind == MfuscatorMutationKind.Swap)
            {
                if ((uint)mutation.A >= (uint)header.Length || (uint)mutation.B >= (uint)header.Length)
                    return false;
                (header[mutation.A], header[mutation.B]) = (header[mutation.B], header[mutation.A]);
            }
            else
            {
                if ((uint)mutation.Offset >= (uint)header.Length)
                    return false;
                header[mutation.Offset] ^= mutation.Value;
            }
        }
        return true;
    }

    private static uint ReadHeaderWord(ReadOnlySpan<byte> header, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(offset, sizeof(uint)));

    private static void ApplySequentialXor(
        Span<byte> metadata,
        MfuscatorSectionDescriptor descriptor,
        MfuscatorPayloadTransform transform)
    {
        var offset = checked((int)descriptor.Offset);
        var size = checked((int)descriptor.Size);
        var section = metadata.Slice(offset, size);
        for (var index = 0; index < section.Length; index++)
            section[index] ^= (byte)((transform.Initial + transform.Step * index) & 0xFF);
    }

    private static uint[] DeriveKeyWords(ReadOnlySpan<byte> key) =>
    [
        (uint)(key[0] | (key[1] << 8) | (key[2] << 16) | (key[6] << 24)),
        BinaryPrimitives.ReadUInt32LittleEndian(key.Slice(4, sizeof(uint))),
        BinaryPrimitives.ReadUInt32LittleEndian(key.Slice(8, sizeof(uint))),
        BinaryPrimitives.ReadUInt32LittleEndian(key.Slice(12, sizeof(uint))),
    ];

    private static uint Mix(uint sum, uint y, uint z, int index, uint[] key)
    {
        var keyIndex = (index & 3) ^ (int)((sum >> 2) & 3);
        var left = unchecked(((z >> 5) ^ (y << 2)) + ((y >> 3) ^ (z << 4)));
        var right = unchecked((sum ^ y) + (key[keyIndex] ^ z));
        return left ^ right;
    }
}
