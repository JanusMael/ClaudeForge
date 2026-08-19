namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Navigation;

/// <summary>
/// Implemented by an editor that needs to act when the user arrives at or leaves
/// its page — re-read a directory listing, flush a deferred rebuild, clear a
/// filter the next visit should not inherit.
///
/// <para>
/// The host used to answer this with a chain of <c>editor is SomeConcreteViewModel</c>
/// checks, one per page, each calling that page's differently-named refresh method
/// (<c>Refresh</c>, <c>Reload</c>, <c>Activate</c>, <c>RefreshConfigAvailability</c>,
/// <c>RefreshAsync</c>). That shape has two costs: the host has to know every page
/// type in the app, and a newly added page is silently *not* refreshed until someone
/// remembers to extend the chain — a failure mode with no compiler signal and no
/// visible symptom beyond stale content. Pages now declare the behaviour themselves.
/// </para>
/// <para>
/// Both members have default no-op bodies so a page implements only the half it
/// cares about; the host always calls through this interface, so the defaults
/// dispatch correctly.
/// </para>
/// <para>
/// This is a <em>navigation</em> hook, not a lifetime hook. It says nothing about
/// construction or disposal, and it may be called many times on one instance —
/// several pages deliberately outlive a workspace reload.
/// </para>
/// </summary>
public interface INavigablePage
{
    /// <summary>
    /// Called immediately after this page becomes the active editor. Runs on the
    /// UI thread and must not block: a page needing async work starts it and lets
    /// its own gate serialise concurrent callers.
    /// </summary>
    void OnNavigatedTo()
    {
    }

    /// <summary>
    /// Called as the user navigates away, before the incoming page becomes active.
    /// </summary>
    /// <param name="replaced">
    /// <see langword="true"/> when a <em>different</em> editor is taking over.
    /// <see langword="false"/> when the incoming editor is this same instance,
    /// which happens because several pages survive a workspace reload and are
    /// re-attached to a freshly built navigation node. A page that discards
    /// transient state here (a typed filter, a scroll position) should check this
    /// first, or a reload will throw away state the user never navigated away from.
    /// </param>
    void OnNavigatedFrom(bool replaced)
    {
    }
}
