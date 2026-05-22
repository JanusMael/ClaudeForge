namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Messages;

/// <summary>
/// Sent via <see cref="CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger"/>
/// when the user clicks a "View in &lt;group&gt;" deep-link from the
/// Essentials page (or any future surface that wants to deep-link into a
/// schema-driven nav group).
/// <para>
/// The receiver (typically `Bennewitz.Ninja.ClaudeForge.ViewModels.MainWindowViewModel`)
/// looks up the matching child node by title and selects it, optionally
/// applying a property filter so the matched property is highlighted on
/// arrival.
/// </para>
/// </summary>
/// <param name="GroupTitle">
/// Display title of the target nav-group (e.g. <c>"Permissions"</c>,
/// <c>"Sandbox"</c>, <c>"Environment"</c>).  Compared against
/// <c>NavigationNodeViewModel.Title</c>.  When no matching node is found,
/// the receiver should silently no-op rather than throw.
/// </param>
/// <param name="PropertyFilter">
/// Optional property-name / dotted-JSON-path to apply as a filter on the
/// target editor (e.g. <c>"sandbox.network.allowedDomains"</c>).  When
/// <see langword="null"/> or empty, the target page renders without a
/// filter applied.
/// </param>
public sealed record NavigateToNavGroupMessage(string GroupTitle, string? PropertyFilter = null);