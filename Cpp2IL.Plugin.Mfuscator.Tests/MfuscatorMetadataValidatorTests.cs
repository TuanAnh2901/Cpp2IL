using System.Linq;
using Cpp2IL.Plugin.Mfuscator;
using NUnit.Framework;

namespace Cpp2IL.Plugin.Mfuscator.Tests;

[TestFixture]
public class MfuscatorMetadataValidatorTests
{
    private static MfuscatorMetadataContract Contract(params MfuscatorSectionContract[] sections) =>
        new("fixture", 0xFAB11BAF, 31, [0x61, 0x62, 0x63, 0x64],
            4, 64, 68, 16, 0, "0123456789abcdef"u8.ToArray(), sections, []);

    [Test]
    public void Validator_rejects_bounds_and_record_size_errors()
    {
        var contract = Contract(new MfuscatorSectionContract("methods", 4, 8, RecordSize: 8));

        var report = MfuscatorMetadataValidator.Validate(
            64, contract, [new MfuscatorSectionDescriptor(60, 6)]);

        Assert.That(report.Accepted, Is.False);
        Assert.That(report.Findings.Select(finding => finding.Code),
            Is.EquivalentTo(new[] { "section.bounds", "section.record_size" }));
    }

    [Test]
    public void Validator_rejects_overlapping_nonempty_sections()
    {
        var contract = Contract(
            new MfuscatorSectionContract("methods", 4, 8),
            new MfuscatorSectionContract("types", 12, 16));

        var report = MfuscatorMetadataValidator.Validate(256, contract,
        [
            new MfuscatorSectionDescriptor(64, 64),
            new MfuscatorSectionDescriptor(96, 64)
        ]);

        Assert.That(report.Accepted, Is.False);
        Assert.That(report.Findings.Any(finding => finding.Code == "section.overlap"), Is.True);
    }

    [Test]
    public void Validator_accepts_aligned_nonoverlapping_sections()
    {
        var contract = Contract(
            new MfuscatorSectionContract("methods", 4, 8, RecordSize: 8),
            new MfuscatorSectionContract("types", 12, 16, RecordSize: 16));

        var report = MfuscatorMetadataValidator.Validate(256, contract,
        [
            new MfuscatorSectionDescriptor(64, 32),
            new MfuscatorSectionDescriptor(96, 32)
        ]);

        Assert.That(report.Accepted, Is.True);
        Assert.That(report.Score, Is.GreaterThan(0));
        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void Legacy_layout_validation_rejects_decoy_overlap_and_accepts_valid_layout()
    {
        (int Start, int End)[] decoy = [(64, 128), (96, 160)];
        (int Start, int End)[] valid = [(64, 96), (96, 128)];

        var decoyReport = MfuscatorMetadataValidator.ValidateLegacyRanges(256, decoy, 0);
        var validReport = MfuscatorMetadataValidator.ValidateLegacyRanges(256, valid, 0);

        Assert.Multiple(() =>
        {
            Assert.That(decoyReport.Accepted, Is.False);
            Assert.That(decoyReport.Findings.Any(finding => finding.Code == "section.overlap"), Is.True);
            Assert.That(validReport.Accepted, Is.True);
            Assert.That(validReport.Score, Is.GreaterThan(decoyReport.Score));
        });
    }
}
