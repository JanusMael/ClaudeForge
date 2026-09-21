using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Schema;

/// <summary>
/// <see cref="ProductDescriptor"/> equality is by <see cref="ProductDescriptor.Id"/>, so one
/// product resolved for two environments is still one product.
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
/// </remarks>
[TestClass]
public sealed class ProductDescriptorEqualityTests
{
    private static readonly ClaudeEnvironment Relocated =
        new(Path.Combine(Path.GetTempPath(), "descriptor-equality-relocated"));

    [TestMethod]
    public void TheSameProduct_ResolvedForTwoEnvironments_IsEqual()
    {
        ProductDescriptor plain = SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty);
        ProductDescriptor moved = SchemaRegistry.ClaudeCodeProductFor(Relocated);

        // Assert the PREMISE first: if these were the same instance the equality claim below
        // would pass for a reason that has nothing to do with the contract under test.
        Assert.AreNotSame(plain, moved,
            "ClaudeCodeProductFor must build a fresh descriptor per call, or this test is vacuous.");

        Assert.AreEqual(plain, moved, "One product resolved for two environments is one product.");
        Assert.AreEqual(plain.GetHashCode(), moved.GetHashCode(),
            "Equal descriptors must hash equally, or a dictionary or Distinct() silently splits them.");
    }

    [TestMethod]
    public void TheSameProduct_ResolvedTwiceForOneEnvironment_IsEqual()
    {
        // ⭐ The case reference equality already failed, BEFORE the environment existed: the
        // layout's Func destinations are new delegate instances on every construction.
        ProductDescriptor first = SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty);
        ProductDescriptor second = SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty);

        Assert.AreNotSame(first, second);
        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void EqualDescriptors_CollapseInASetAndMatchAsADictionaryKey()
    {
        // The two real consumers reach for exactly these: Contains/Distinct over a product list,
        // and a descriptor used as a lookup key.
        ProductDescriptor plain = SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty);
        ProductDescriptor moved = SchemaRegistry.ClaudeCodeProductFor(Relocated);

        Assert.AreEqual(1, new HashSet<ProductDescriptor> { plain, moved }.Count,
            "Two resolutions of one product must collapse to one set member.");

        Dictionary<ProductDescriptor, string> byProduct = new() { [plain] = "value" };
        Assert.IsTrue(byProduct.ContainsKey(moved),
            "A descriptor resolved elsewhere must find the entry keyed by this product.");
    }

    [TestMethod]
    public void TwoDifferentProducts_AreNotEqual()
    {
        // ⛔ The other half. An Id-based Equals that returned true too readily would make every
        // product match every other, and each assertion above would still pass.
        ProductDescriptor claudeCode = SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty);
        ProductDescriptor claudeDesktop = SchemaRegistry.ClaudeDesktopProduct;

        Assert.AreNotEqual(claudeCode.Id, claudeDesktop.Id, "The fixture needs two distinct ids.");
        Assert.AreNotEqual(claudeCode, claudeDesktop);
    }

    [TestMethod]
    public void ADescriptorIsNeverEqualToNull()
    {
        ProductDescriptor product = SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty);

        Assert.IsFalse(product.Equals(null));
    }
}
