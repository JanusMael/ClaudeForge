// Emitted by Bennewitz.Ninja.Templates scripts/mstest-to-xunit.cs --emit-helpers.
// Regenerate rather than edit: the conversion rules call exactly these members.

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Sdk;

namespace Bennewitz.Ninja.Testing;

/// <summary>
/// xUnit's own assertions, with the explanatory message the original MSTest assertion carried.
/// </summary>
/// <remarks>
/// <para>
/// xUnit deliberately gives <c>Equal</c>, <c>Null</c>, <c>Same</c> and <c>Contains</c> no message
/// parameter. A converted MSTest suite's messages are routinely the whole point of the
/// assertion: they say WHY the value matters, which is what a reader needs when it fails.
/// </para>
/// <para>
/// ⭐ Each method CALLS xUnit's assertion and prefixes the message only when it fails, so a failure
/// still shows xUnit's expected/actual diff, and every argument is evaluated exactly once. The
/// alternative, <c>Assert.True(Equals(e, a), message)</c>, loses the diff and evaluates twice.
/// </para>
/// <para>
/// ⚠ Not an MSTest compatibility layer, and not for new tests: it exists for converted assertions
/// that carried a message. New tests use xUnit's Assert directly.
/// </para>
/// </remarks>
internal static class MessageAssert
{
    public static void Equal<T>(T expected, T actual, string message) =>
        With(message, () => Assert.Equal(expected, actual));

    /// <summary>MSTest's AreEqual(double, double, delta, message): |expected - actual| &lt;= tolerance.</summary>
    public static void Equal(double expected, double actual, double tolerance, string message) =>
        With(message, () => Assert.Equal(expected, actual, tolerance));

    public static void NotEqual<T>(T expected, T actual, string message) =>
        With(message, () => Assert.NotEqual(expected, actual));

    public static void Null(object? value, string message) =>
        With(message, () => Assert.Null(value));

    /// <remarks>
    /// Written out rather than delegated: [NotNull] promises the caller's flow analysis that the
    /// value is non-null afterwards, and the compiler cannot see that promise kept through a lambda.
    /// </remarks>
    public static void NotNull([NotNull] object? value, string message)
    {
        if (value is null)
        {
            throw new XunitException(message + Environment.NewLine + "Assert.NotNull() Failure: Value is null");
        }
    }

    public static void Same(object? expected, object? actual, string message) =>
        With(message, () => Assert.Same(expected, actual));

    public static void Contains<T>(T expected, IEnumerable<T> collection, string message) =>
        With(message, () => Assert.Contains(expected, collection));

    public static void Contains(string expectedSubstring, string? actualString, string message) =>
        With(message, () => Assert.Contains(expectedSubstring, actualString));

    public static T IsAssignableFrom<T>(object? value, string message)
    {
        T result = default!;
        With(message, () => result = Assert.IsAssignableFrom<T>(value));
        return result;
    }

    // ── Added for the conversion rules; not in the ScopedEditors original ──

    /// <remarks>void, like both MSTest's non-generic IsInstanceOfType and xUnit's non-generic IsAssignableFrom.</remarks>
    public static void IsAssignableFrom(Type expectedType, object? value, string message) =>
        With(message, () => Assert.IsAssignableFrom(expectedType, value));

    public static void NotSame(object? expected, object? actual, string message) =>
        With(message, () => Assert.NotSame(expected, actual));

    public static void DoesNotContain<T>(T expected, IEnumerable<T> collection, string message) =>
        With(message, () => Assert.DoesNotContain(expected, collection));

    public static void DoesNotContain(string expectedSubstring, string? actualString, string message) =>
        With(message, () => Assert.DoesNotContain(expectedSubstring, actualString));

    public static void StartsWith(string? expectedStart, string? actualString, string message) =>
        With(message, () => Assert.StartsWith(expectedStart, actualString));

    public static void EndsWith(string? expectedEnd, string? actualString, string message) =>
        With(message, () => Assert.EndsWith(expectedEnd, actualString));

    public static void Matches(Regex expectedRegex, string? actualString, string message) =>
        With(message, () => Assert.Matches(expectedRegex, actualString));

    public static void DoesNotMatch(Regex expectedRegex, string? actualString, string message) =>
        With(message, () => Assert.DoesNotMatch(expectedRegex, actualString));

    /// <summary>MSTest's CollectionAssert.AreEqual with a message: same elements, same order.</summary>
    public static void SequenceEqual<T>(IEnumerable<T>? expected, IEnumerable<T>? actual, string message) =>
        With(message, () => Assert.Equal(expected, actual));

    /// <summary>MSTest's CollectionAssert.AreNotEqual with a message: differs in elements or order.</summary>
    public static void SequenceNotEqual<T>(IEnumerable<T>? expected, IEnumerable<T>? actual, string message) =>
        With(message, () => Assert.NotEqual(expected, actual));

    /// <summary>MSTest's CollectionAssert.AllItemsAreUnique with a message.</summary>
    public static void Distinct<T>(IEnumerable<T> collection, string message) =>
        With(message, () => Assert.Distinct(collection));

    /// <summary>MSTest's AreEqual(string, string, ignoreCase, message).</summary>
    public static void Equal(string? expected, string? actual, bool ignoreCase, string message) =>
        With(message, () => Assert.Equal(expected, actual, ignoreCase: ignoreCase));

    /// <summary>MSTest's AreEqual(expected, actual, comparer, message).</summary>
    public static void Equal<T>(T expected, T actual, IEqualityComparer<T> comparer, string message) =>
        With(message, () => Assert.Equal(expected, actual, comparer));

    /// <summary>MSTest's AreNotEqual(notExpected, actual, comparer, message).</summary>
    public static void NotEqual<T>(T expected, T actual, IEqualityComparer<T> comparer, string message) =>
        With(message, () => Assert.NotEqual(expected, actual, comparer));

    public static void Contains(string expectedSubstring, string? actualString, StringComparison comparisonType, string message) =>
        With(message, () => Assert.Contains(expectedSubstring, actualString, comparisonType));

    public static void DoesNotContain(string expectedSubstring, string? actualString, StringComparison comparisonType, string message) =>
        With(message, () => Assert.DoesNotContain(expectedSubstring, actualString, comparisonType));

    public static void StartsWith(string? expectedStart, string? actualString, StringComparison comparisonType, string message) =>
        With(message, () => Assert.StartsWith(expectedStart, actualString, comparisonType));

    public static void EndsWith(string? expectedEnd, string? actualString, StringComparison comparisonType, string message) =>
        With(message, () => Assert.EndsWith(expectedEnd, actualString, comparisonType));

    public static T Throws<T>(Action testCode, string message) where T : Exception
    {
        T result = default!;
        With(message, () => result = Assert.Throws<T>(testCode));
        return result;
    }

    public static async Task<T> ThrowsAsync<T>(Func<Task> testCode, string message) where T : Exception
    {
        try
        {
            return await Assert.ThrowsAsync<T>(testCode);
        }
        catch (XunitException failure)
        {
            throw new XunitException(message + Environment.NewLine + failure.Message, failure);
        }
    }

    /// <summary>
    /// MSTest's CollectionAssert.AreEquivalent: the same elements with the same multiplicity, in
    /// any order, compared with the default equality. Both null passes; one null fails.
    /// </summary>
    /// <remarks>
    /// ⚠ Deliberately NOT xUnit's <c>Assert.Equivalent</c>, which compares object graphs
    /// structurally and by different rules. A converted assertion must keep the meaning it had.
    /// </remarks>
    public static void SameElements<T>(IEnumerable<T>? expected, IEnumerable<T>? actual, string? message = null)
    {
        if (expected is null && actual is null)
        {
            return;
        }
        if (expected is null || actual is null)
        {
            Fail(message, $"one collection is null (expected {(expected is null ? "null" : "non-null")}, actual {(actual is null ? "null" : "non-null")})");
            return;
        }

        List<T> e = [.. expected], a = [.. actual];
        var remaining = new List<T>(a);
        var missing = new List<T>();
        foreach (T item in e)
        {
            int at = remaining.FindIndex(x => EqualityComparer<T>.Default.Equals(x, item));
            if (at < 0)
            {
                missing.Add(item);
            }
            else
            {
                remaining.RemoveAt(at);
            }
        }
        if (missing.Count > 0 || remaining.Count > 0)
        {
            Fail(message,
                $"collections do not hold the same elements (expected {e.Count}, actual {a.Count}); "
                + $"missing from actual: [{string.Join(", ", missing)}]; unexpected in actual: [{string.Join(", ", remaining)}]");
        }
    }

    static void Fail(string? message, string detail) =>
        throw new XunitException((message is null or "" ? "" : message + Environment.NewLine)
                                 + "MessageAssert.SameElements() Failure: " + detail);

    private static void With(string message, Action assertion)
    {
        try
        {
            assertion();
        }
        catch (XunitException failure)
        {
            throw new XunitException(message + Environment.NewLine + failure.Message, failure);
        }
    }
}

/// <summary>
/// The collection a converted <c>[DoNotParallelize]</c> class joins. Its tests run after every
/// parallel collection has finished, one at a time — MSTest's meaning for the attribute.
/// </summary>
[CollectionDefinition("DoNotParallelize", DisableParallelization = true)]
public sealed class DoNotParallelizeDefinition;