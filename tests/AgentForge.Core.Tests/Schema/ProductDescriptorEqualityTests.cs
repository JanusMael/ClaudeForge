using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Testing;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Schema;

/// <summary>
/// <see cref="ProductDescriptor"/> equality is by <see cref="ProductDescriptor.Id"/>, so one
/// product resolved for two homes is still one product.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>Nothing guarded this, and the synthesized record equality it replaced had already
/// stopped being meaningful.</b> <c>ProductBackupLayout</c> holds <c>Func</c> destinations and
/// delegates compare by REFERENCE, so two descriptors built from identical inputs one line apart
/// were unequal. That was invisible only because each product used to be a single static
/// instance, where reference equality gave the right answer by accident. Deleting
/// <c>SchemaRegistry.ClaudeCodeProduct</c> for <c>ClaudeCodeProductFor(env)</c> removed the
/// accident: every call now builds a fresh descriptor.
/// </para>
/// <para>
/// ⚠ <b>The failure it prevents is silent.</b> <c>BackupRequest.Includes</c> and the navigation's
/// <c>SectionFor</c> both match products; under reference equality a descriptor resolved in one
/// place would simply never match one resolved in another, so a backup would quietly include no
/// sections and a page would quietly resolve to none — with no exception and nothing logged.
/// </para>
/// <para>
/// ⭐ On <see cref="NeutralTestProduct"/> since <c>plans/00008</c> step 5: the contract belongs
/// to the descriptor type, and any product built per home has the same exposure as Claude's.
/// </para>
/// </remarks>
public sealed class ProductDescriptorEqualityTests
{
    private static readonly string HomeA = Path.Combine(Path.GetTempPath(), "descriptor-equality-a");
    private static readonly string HomeB = Path.Combine(Path.GetTempPath(), "descriptor-equality-b");

    [Fact]
    public void TheSameProduct_ResolvedForTwoEnvironments_IsEqual()
    {
        ProductDescriptor plain = NeutralTestProduct.For(HomeA);
        ProductDescriptor moved = NeutralTestProduct.For(HomeB);

        // Assert the PREMISE first: these must differ in what they resolve to, or the equality
        // claim below would pass for a reason that has nothing to do with the contract under test.
        MessageAssert.NotEqual(plain.Backup.Sections[0].Destination(), moved.Backup.Sections[0].Destination(),
            "The two descriptors must point at different homes, or this test is vacuous.");

        MessageAssert.Equal(plain, moved, "One product resolved for two homes is one product.");
        MessageAssert.Equal(plain.GetHashCode(), moved.GetHashCode(),
            "Equal descriptors must hash equally, or a dictionary or Distinct() silently splits them.");
    }

    [Fact]
    public void TheSameProduct_ResolvedTwiceForOneEnvironment_IsEqual()
    {
        // ⭐ The case reference equality already failed: the layout's Func destinations are new
        // delegate instances on every construction.
        ProductDescriptor first = NeutralTestProduct.For(HomeA);
        ProductDescriptor second = NeutralTestProduct.For(HomeA);

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
    }

    [Fact]
    public void EqualDescriptors_CollapseInASetAndMatchAsADictionaryKey()
    {
        // The two real consumers reach for exactly these: Contains/Distinct over a product list,
        // and a descriptor used as a lookup key.
        ProductDescriptor plain = NeutralTestProduct.For(HomeA);
        ProductDescriptor moved = NeutralTestProduct.For(HomeB);

        MessageAssert.Equal(1, new HashSet<ProductDescriptor> { plain, moved }.Count,
            "Two resolutions of one product must collapse to one set member.");

        Dictionary<ProductDescriptor, string> byProduct = new() { [plain] = "value" };
        Assert.True(byProduct.ContainsKey(moved),
            "A descriptor resolved elsewhere must find the entry keyed by this product.");
    }

    [Fact]
    public void TwoDifferentProducts_AreNotEqual()
    {
        // ⛔ The other half. An Id-based Equals that returned true too readily would make every
        // product match every other, and each assertion above would still pass. Everything but
        // the id is the same here, so only the id can be what tells them apart.
        ProductDescriptor neutral = NeutralTestProduct.For(HomeA);
        ProductDescriptor other = neutral with { Id = "another-product" };

        Assert.NotEqual(neutral, other);
    }

    [Fact]
    public void ADescriptorIsNeverEqualToNull()
    {
        ProductDescriptor product = NeutralTestProduct.For(HomeA);

        Assert.False(product.Equals(null));
    }
}
