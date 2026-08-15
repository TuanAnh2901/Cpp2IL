using System;
using Cpp2IL.Plugin.Mfuscator;
using NUnit.Framework;

namespace Cpp2IL.Plugin.Mfuscator.Tests;

[TestFixture]
public class MfuscatorMetadataContractTests
{
    [Test]
    public void Everlusting_contract_matches_the_captured_loader_layout()
    {
        var contract = MfuscatorMetadataContract.Everlusting;

        Assert.Multiple(() =>
        {
            Assert.That(contract.HeaderOffset, Is.EqualTo(4));
            Assert.That(contract.HeaderLength, Is.EqualTo(676));
            Assert.That(contract.SourceBias, Is.EqualTo(484));
            Assert.That(contract.MetadataVersion, Is.EqualTo(31));
            Assert.That(contract.Sections, Has.Count.EqualTo(31));
            Assert.That(contract.Mutations, Has.Count.EqualTo(3));
            Assert.That(contract.Sections[1].PayloadTransform,
                Is.EqualTo(new MfuscatorPayloadTransform(0x0D, -1)));
            Assert.That(contract.Sections[2].PayloadTransform,
                Is.EqualTo(new MfuscatorPayloadTransform(0x5F, 1)));
        });
    }

    [Test]
    public void Contract_rejects_duplicate_section_names()
    {
        var section = new MfuscatorSectionContract("methods", 4, 8);

        Assert.Throws<ArgumentException>(() => new MfuscatorMetadataContract(
            "duplicate", 0xFAB11BAF, 31, [0x61, 0x62, 0x63, 0x64],
            4, 64, 68, 16, 0, "0123456789abcdef"u8.ToArray(),
            [section, section], []));
    }
}
