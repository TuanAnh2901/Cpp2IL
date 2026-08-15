namespace Cpp2IL.Plugin.Mfuscator;

public sealed record MfuscatorValidationFinding(
    string Code,
    string Severity,
    string? Section,
    string Message);

public sealed record MfuscatorValidationReport(
    bool Accepted,
    int Score,
    IReadOnlyList<MfuscatorValidationFinding> Findings);

public static class MfuscatorMetadataValidator
{
    public static MfuscatorValidationReport ValidateLegacyRanges(
        int metadataLength,
        IReadOnlyList<(int Start, int End)> ranges,
        int offsetDelta)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        var findings = new List<MfuscatorValidationFinding>();
        var successfulChecks = 0;
        for (var index = 0; index < ranges.Count; index++)
        {
            var (start, end) = ranges[index];
            var targetStart = (long)start + offsetDelta;
            var targetEnd = (long)end + offsetDelta;
            if (start < 0 || end < start || end > metadataLength ||
                targetStart < 0 || targetEnd > metadataLength)
                findings.Add(new("section.bounds", "error", index.ToString(),
                    $"Legacy range {index} ({start:X}-{end:X}) is outside the metadata."));
            else
                successfulChecks++;
            if ((targetStart & 3) != 0)
                findings.Add(new("section.alignment", "error", index.ToString(),
                    $"Legacy range {index} target offset 0x{targetStart:X} is not 4-byte aligned."));
            else
                successfulChecks++;
            if (index > 0 && start < ranges[index - 1].End)
                findings.Add(new("section.overlap", "error", index.ToString(),
                    $"Legacy range {index} starts before range {index - 1} ends."));
        }

        var errors = findings.Count(finding => finding.Severity == "error");
        return new(errors == 0, Math.Max(0, successfulChecks - errors * 100), findings);
    }

    public static MfuscatorValidationReport Validate(
        int metadataLength,
        MfuscatorMetadataContract contract,
        IReadOnlyList<MfuscatorSectionDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(descriptors);
        var findings = new List<MfuscatorValidationFinding>();
        if (descriptors.Count != contract.Sections.Count)
            findings.Add(new("descriptor.count", "error", null,
                $"Expected {contract.Sections.Count} descriptors, got {descriptors.Count}."));

        var ranges = new List<(ulong Start, ulong End, string Name)>();
        var successfulChecks = 0;
        foreach (var pair in contract.Sections.Zip(descriptors))
        {
            var section = pair.First;
            var descriptor = pair.Second;
            var end = (ulong)descriptor.Offset + descriptor.Size;
            if (end > (ulong)metadataLength)
                findings.Add(new("section.bounds", "error", section.Name,
                    $"0x{descriptor.Offset:X} + 0x{descriptor.Size:X} exceeds 0x{metadataLength:X}."));
            else
                successfulChecks++;

            if (descriptor.Offset % section.Alignment != 0)
                findings.Add(new("section.alignment", "error", section.Name,
                    $"Offset 0x{descriptor.Offset:X} is not aligned to {section.Alignment}."));
            else
                successfulChecks++;

            if (section.RecordSize is { } recordSize && descriptor.Size % recordSize != 0)
                findings.Add(new("section.record_size", "error", section.Name,
                    $"Size 0x{descriptor.Size:X} is not divisible by {recordSize}."));
            else if (section.RecordSize is not null)
                successfulChecks++;

            if (descriptor.Size > 0)
                ranges.Add((descriptor.Offset, end, section.Name));
        }

        ranges.Sort((left, right) => left.Start != right.Start
            ? left.Start.CompareTo(right.Start)
            : left.End.CompareTo(right.End));
        for (var index = 1; index < ranges.Count; index++)
        {
            var previous = ranges[index - 1];
            var current = ranges[index];
            if (current.Start < previous.End)
                findings.Add(new("section.overlap", "error", current.Name,
                    $"Section {current.Name} starts at 0x{current.Start:X} before " +
                    $"section {previous.Name} ends at 0x{previous.End:X}."));
        }

        var errors = findings.Count(finding => finding.Severity == "error");
        var warnings = findings.Count(finding => finding.Severity == "warning");
        return new(errors == 0, Math.Max(0, successfulChecks - errors * 100 - warnings * 10), findings);
    }
}
