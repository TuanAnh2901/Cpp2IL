using AssetRipper.Primitives;
using NUnit.Framework;

namespace Cpp2IL.Plugin.Mfuscator.Tests;

[TestFixture]
public class MfuscatorMetadataLayoutTests
{
    [TestCase(3, 1, UnityVersionType.Alpha, 31, 8, 31, 21, true)]
    [TestCase(3, 2, UnityVersionType.Alpha, 35, 8, 31, 21, true)]
    [TestCase(3, 5, UnityVersionType.Alpha, 38, 12, 31, 21, false)]
    [TestCase(3, 6, UnityVersionType.Alpha, 38, 12, 31, 21, false)]
    [TestCase(3, 1, UnityVersionType.Beta, 39, 12, 31, 21, false)]
    [TestCase(5, 3, UnityVersionType.Alpha, 104, 12, 32, 22, false)]
    [TestCase(5, 5, UnityVersionType.Alpha, 105, 12, 32, 22, false)]
    [TestCase(5, 6, UnityVersionType.Alpha, 106, 12, 32, 22, false)]
    public void Unity_6000_3_layout_boundaries_are_reachable(
        int minor, byte release, UnityVersionType type, int metadataVersion,
        int descriptorWidth, int sectionCount, int assembliesIndex, bool canReconstruct)
    {
        var unity = new UnityVersion(6000, checked((ushort)minor), 0, type, release);

        var layout = MfuscatorSupportPlugin.GetMetadataLayout(unity);

        Assert.Multiple(() =>
        {
            Assert.That(layout.MetadataVersion, Is.EqualTo(metadataVersion));
            Assert.That(layout.DescriptorWidth, Is.EqualTo(descriptorWidth));
            Assert.That(layout.SectionCount, Is.EqualTo(sectionCount));
            Assert.That(layout.AssembliesSectionIndex, Is.EqualTo(assembliesIndex));
            Assert.That(layout.CanReconstruct, Is.EqualTo(canReconstruct));
        });
    }
}
