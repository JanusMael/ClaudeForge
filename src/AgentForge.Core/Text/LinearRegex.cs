using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.AgentForge.Core.Text;

/// <summary>
/// Shared, linear-time <see cref="Regex"/> instances for patterns built at run time
/// from user input, such as a glob in a permission rule or a <c>.gitignore</c> line.
/// </summary>
/// <remarks>
/// <para>
/// Every instance runs on <see cref="RegexOptions.NonBacktracking"/>, which is linear
/// in the input length whatever the pattern, and has
/// <see cref="Regex.InfiniteMatchTimeout"/>. The timeout is passed explicitly, so a
/// process-wide <c>REGEX_DEFAULT_MATCH_TIMEOUT</c> cannot put one back.
/// </para>
/// <para>
/// ⛔ <b>Do not bound a user-built regex with a wall-clock match timeout.</b> These sites
/// used to, and treated a timeout as "no match". The timeout measures elapsed time, not
/// work, so on a loaded machine JIT, GC or descheduling spent it on 7-character input.
/// It also fired without load: the glob <c>*a*a*a*a*a*Z*Q</c> against a 51-character
/// command took 429 ms on the backtracking engine, so the matcher reported a command
/// that DOES match as not matching. For a deny rule, or a negated <c>.gitignore</c>
/// line, that answer is the unsafe one.
/// </para>
/// <para>
/// ⚠ NonBacktracking rejects lookarounds, backreferences, atomic groups and
/// conditionals by throwing <see cref="NotSupportedException"/> at construction. A
/// generator feeding this type must not emit them.
/// </para>
/// <para>
/// ⚠ The engine costs about 2 ms to construct, roughly a hundred times the backtracking
/// engine, so instances are cached per pattern and options. The permission collision
/// detector matches pairwise, and without the cache it would rebuild every rule's regex
/// for every pair. The cache is cleared when it reaches <see cref="Capacity"/>, which
/// bounds it when a live tester builds a new pattern on every keystroke.
/// </para>
/// </remarks>
internal static class LinearRegex
{
    /// <summary>Number of cached instances at which the cache is cleared.</summary>
    internal const int Capacity = 512;

    private static readonly ConcurrentDictionary<(string Pattern, RegexOptions Options), Regex> s_cache = new();

    /// <summary>Number of instances currently cached. For tests.</summary>
    internal static int CachedCount => s_cache.Count;

    /// <summary>
    /// Returns the shared linear-time regex for <paramref name="pattern"/>.
    /// <see cref="RegexOptions.NonBacktracking"/> is added to
    /// <paramref name="options"/>.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// <paramref name="pattern"/> uses a construct NonBacktracking does not support.
    /// </exception>
    internal static Regex Get(string pattern, RegexOptions options = RegexOptions.None)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        (string, RegexOptions) key = (pattern, options | RegexOptions.NonBacktracking);
        if (s_cache.TryGetValue(key, out Regex? cached))
        {
            return cached;
        }

        if (s_cache.Count >= Capacity)
        {
            s_cache.Clear();
        }

        return s_cache.GetOrAdd(key, static k => Create(k.Item1, k.Item2));
    }

    /// <summary>
    /// Builds an uncached linear-time regex, for a site that holds its own instance.
    /// </summary>
    internal static Regex Create(string pattern, RegexOptions options = RegexOptions.None) =>
        new(pattern, options | RegexOptions.NonBacktracking, Regex.InfiniteMatchTimeout);
}
