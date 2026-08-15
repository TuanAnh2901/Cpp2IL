using System;
using System.Buffers.Binary;
using NUnit.Framework;

namespace Cpp2IL.Plugin.Mfuscator.Tests;

[TestFixture]
public class CustomMetadataHeaderDecoderTests
{
    private const string EncryptedHeaderBase64 = "H459uVIIoYc4vdliFwsg3e1MK2zkGxAdm2bjTS7qiFF1gleZ/zYEU5MEEHQ3wmrNXxgJzc4g+hrdPPDyOAR9E0RIf8xOa1ZZ26H8fY5E2EhKb/DeGLsoPaeIPkSitCE/a7JXPO6U++pgZBCxazw5lxUJ+AlNknlHVCXGvImosfRe13OWCh/AbIFXDuUHqCMQtHF0a4y4+28SDAcxHiIGlLVQaMLSYPaE5vha8kUpd3a2yeC4U/hRzokzL3Ba4DAw4w4x0OvDIw7RHU2M9oyMIug77ZuFaj7Dk+qOykHqc2GMsjwklDlKiZMKOWxGSu+YqPUeQ2pyb9Pl0ueXlWzU4EA67X9s9AYn8HZZ31/EplwZ2QsIihhTUjbEQ5C/6+L3rqJSgbQvRHB4dqgU7YuFRTZO9W1iDUtz19EMIdN5huE42OTTPwhCQtLBbmMDX5hH+kSZVnMr4dlUaSrVKCTssd2Mw4knvv1+I4otQO5inD/DMuYCE91xuKsJ1W6HSpGaeD4WW59DmMbZv2tlnv3yrrDay5lLhccud7m5+t1er7PRJOpPQLUKJIRUqhqWybH4dj8tjJdynjM2GoQqcDGkXKuMiFd7QA8jbmfYxznh9giB5ZPvHGtYsC8prsvdp8FtMbyZP60kVAHizKrqU8Hd7JnyuX48Icqom1AM5NNdjHa7yVg1Vs6PGTjBUsKCJf47Co8E230wbeLrPjZpgqeK8ZKuteBl/T4/DJJ0CAnmU70cCeMCrB/+ndf3z0gzw5vw1Jffg/B86Wb1UO9duMceDYpiCcIyl/qP0ubArxoCX35Jq7YJzVhw8dl0p3rGaNZiJaxdPM74NkCgnNUUBnyHu/rgpiR00Qkb8EGVuslY4nB0LsU2pvkqixqmcjpwaB3kHJAYjg==";
    private const string DecodedHeaderBase64 = "AOIpAdyXzQDk2P//eFDsAOR/hQHonQEAVF0uAQwaAAD4HpABLEMBACgjyQDsRQ4A0CsAALzbAAC0tAAAHEOPAThccQBk6CwBxFkDALRlSwCELwEAIIAIAJwQjgGYKEQBxFoSANSebwH4GQAAGBQEAESOVgDw0hwA9H+FAeR/CADAKQAAAJ8rAfQiyQC8qwQA0KsEACR8FQDsGQAAOARwAbwpAADsGQAAjJ5vAfj5OADQ+TgA6CUEAChDjwF4r28BwLgrAYSzSwBQFQQA5BkAAABUjwGsRQ4AmMhvAbgRAADwnisB6LQAADQUcAG42wAAwMwBACwjyQDQAAAADBoAAFh8FQBkQwEAHB+QAcA4AQCYoksAyKsEAIyQhQGkzkABLNIpAdBNAADEf4UB8LQAALyEbwFk6woA1J0BAIA/7AAYGgAAENMcAJTMAQAAAAAAMOrHAAC1AAA0dRcAbK8AAOAzyQAgGgAAnFkDAIDfQAEAFQQAAAAAAOS4KwHMtAAApG0SAaBMLgGYTQAAuC+QASyvAADIf4UBNI5WALxrEgC0zAEAZEwuAchaEgAwFAQABNIpAeS0AAAsghIAZKgtAVQvAQAIgAgA3B6QAdhZAwD4W3EAkLLqAOArAAC42wAA4LgrAZDJKwE4FQQASEOPAYAvAQAEgAgA6J5WABABAAD8DwAA0LQAADjZbwHw0hwAWDlEAQwaAADsAAAAYJVvAVBDAQAQ+SwB/BMEAAyOVgBEdRcAIPvHABCACACw/40B4B6QAQCCEgAIXCsAlKJLADAjyQBQQwEAuC+QAfR/hQEwI8kApCkAAAArAADURQ4A+HQXAFDzbwG4L5AB1P///5ivKwGg380A7P+NAVSvAACgly0BAAAAACwdJgAgAADYAAAAAA==";

    [Test]
    public void Decrypt_known_header_matches_runtime_capture()
    {
        var encrypted = Convert.FromBase64String(EncryptedHeaderBase64);
        var expected = Convert.FromBase64String(DecodedHeaderBase64);

        var actual = CustomMetadataHeaderDecoder.Decrypt(encrypted);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void IsSupportedMetadata_recognizes_the_captured_file_prefix()
    {
        var metadata = new byte[CustomMetadataHeaderDecoder.HeaderOffset + Convert.FromBase64String(EncryptedHeaderBase64).Length];
        new byte[] { 0xE1, 0xB0, 0x6C, 0xFE, 0x1F, 0x8E, 0x7D, 0xB9 }.CopyTo(metadata, 0);

        Assert.That(CustomMetadataHeaderDecoder.IsSupportedMetadata(metadata), Is.True);
    }

    [Test]
    public void TryDecodeMetadataHeader_extracts_and_decrypts_the_custom_header()
    {
        var encrypted = Convert.FromBase64String(EncryptedHeaderBase64);
        var expected = Convert.FromBase64String(DecodedHeaderBase64);
        var metadata = new byte[CustomMetadataHeaderDecoder.HeaderOffset + encrypted.Length];
        new byte[] { 0xE1, 0xB0, 0x6C, 0xFE, 0x1F, 0x8E, 0x7D, 0xB9 }.CopyTo(metadata, 0);
        encrypted.CopyTo(metadata, CustomMetadataHeaderDecoder.HeaderOffset);

        var decoded = CustomMetadataHeaderDecoder.TryDecodeMetadataHeader(metadata, out var header);

        Assert.Multiple(() =>
        {
            Assert.That(decoded, Is.True);
            Assert.That(header, Is.EqualTo(expected));
        });
    }

    [Test]
    public void TryRebuildMetadata_writes_v31_header_and_decrypts_loader_sections()
    {
        const int metadataLength = 0x019060C4;
        const int stringLiteralDataOffset = 0x000427CC;
        const int stringsOffset = 0x00126DA0;
        var encryptedHeader = Convert.FromBase64String(EncryptedHeaderBase64);
        var metadata = new byte[metadataLength];
        new byte[] { 0xE1, 0xB0, 0x6C, 0xFE, 0x1F, 0x8E, 0x7D, 0xB9 }.CopyTo(metadata, 0);
        encryptedHeader.CopyTo(metadata, CustomMetadataHeaderDecoder.HeaderOffset);

        byte[] expectedLiteralData = [0x10, 0x20, 0x30, 0x40];
        byte[] expectedStrings = [0x50, 0x60, 0x70, 0x80];
        for (var index = 0; index < expectedLiteralData.Length; index++)
            metadata[stringLiteralDataOffset + index] = (byte)(expectedLiteralData[index] ^ ((0x0D - index) & 0xFF));
        for (var index = 0; index < expectedStrings.Length; index++)
            metadata[stringsOffset + index] = (byte)(expectedStrings[index] ^ ((0x5F + index) & 0xFF));

        var rebuilt = CustomMetadataHeaderDecoder.TryRebuildMetadata(
            metadata,
            MfuscatorMetadataContract.Everlusting,
            out var output,
            out var validation);

        var expectedSections = new (uint Offset, uint Size)[]
        {
            (0x0000139C, 0x00041430), (0x000427CC, 0x000E45D4),
            (0x00126DA0, 0x0038F9F8), (0x004B6798, 0x00004DD0),
            (0x004BB568, 0x000AEB64), (0x0056A0CC, 0x00715C38),
            (0x00C7FD04, 0x000138C0), (0x00C935C4, 0x0004ABBC),
            (0x00CDE184, 0x001CD2F0), (0x00EAB474, 0x00019DE8),
            (0x00EC525C, 0x00261D2C), (0x01126F88, 0x00177534),
            (0x0129E4BC, 0x0001CCC0), (0x012BB17C, 0x000019F8),
            (0x012BCB74, 0x00012F80), (0x012CFAF4, 0x0000AF54),
            (0x012DAA48, 0x0000B4F0), (0x012E5F38, 0x0012822C),
            (0x0140E164, 0x000359D8), (0x01443B3C, 0x002B5C08),
            (0x016F9744, 0x00001A18), (0x016FB15C, 0x000029C0),
            (0x016FDB1C, 0x00002B00), (0x0170061C, 0x00000FFC),
            (0x01701618, 0x00157C58), (0x01859270, 0x00088010),
            (0x018E1280, 0x00014364), (0x018F55E4, 0x0000DBB8),
            (0x0190319C, 0x00000000), (0x0190319C, 0x00000000),
            (0x0190319C, 0x00002BD0),
        };

        Assert.That(rebuilt, Is.True);
        Assert.That(validation.Accepted, Is.True);
        Assert.That(validation.Findings, Is.Empty);
        Assert.That(output, Has.Length.EqualTo(metadata.Length));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(output), Is.EqualTo(0xFAB11BAF));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(4)), Is.EqualTo(31));
        for (var index = 0; index < expectedSections.Length; index++)
        {
            var headerOffset = 8 + index * 8;
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(headerOffset)),
                Is.EqualTo(expectedSections[index].Offset), $"section {index} offset");
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(headerOffset + 4)),
                Is.EqualTo(expectedSections[index].Size), $"section {index} size");
        }
        Assert.That(output.AsSpan(stringLiteralDataOffset, expectedLiteralData.Length).ToArray(),
            Is.EqualTo(expectedLiteralData));
        Assert.That(output.AsSpan(stringsOffset, expectedStrings.Length).ToArray(),
            Is.EqualTo(expectedStrings));
    }
}
