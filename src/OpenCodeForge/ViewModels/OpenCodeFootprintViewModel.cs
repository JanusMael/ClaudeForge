using System.Collections.ObjectModel;
using System.Globalization;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Navigation;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;
using Bennewitz.Ninja.OpenCodeForge.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace Bennewitz.Ninja.OpenCodeForge.ViewModels;

/// <summary>
/// OpenCodeForge's disk-footprint page: what OpenCode has left on this machine, where, and which
/// of it is worth reclaiming.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>The order of the rows is the guidance, and it is NOT a size sort.</b>
/// <c>OpenCodeFootprint.Catalog</c> is ordered most-disposable first, so the top row is the
/// largest and safest thing to delete and the bottom row is the smallest and the only
/// irreplaceable one. This page renders catalog order and offers no sort, because every sort a
/// table usually offers — size descending above all — puts the one row that must not be touched
/// at the top of the list.
/// </para>
/// <para>
/// ⛔ <b>There is no delete button, and that is a decision rather than an omission.</b> The sibling
/// app deletes per category; here the same button would sit against a catalog whose regeneration
/// and retention behaviour is explicitly unmeasured — Phase 16's quantitative half is blocked on
/// <c>usage.isUsedInstall</c> — with one row that no standard backup carries and no undo. The page
/// measures and reveals; the file manager it opens is where a deletion happens, with the OS's own
/// confirmation in front of it.
/// </para>
/// <para>
/// ⚠ <b>Reads through the CLIENT, not through a locally built service.</b> A page that constructed
/// its own <c>FootprintService</c> would have gone on working while
/// <c>OpenCodeClient.GetFootprintStatsAsync</c> kept returning Claude's categories to every other
/// caller — which is the state this repository was actually in until 2026-09-12.
/// </para>
/// <para>
/// ⚠ <b>No client is required to have OPENED.</b> The footprint is a walk of the filesystem and
/// owes nothing to a parsed document, so this page still answers on a machine whose
/// <c>opencode.json</c> will not load — which is exactly the machine whose user is looking for
/// what to clear out.
/// </para>
/// </remarks>
internal sealed partial class OpenCodeFootprintViewModel : ObservableObject, INavigablePage
{
    private readonly AgentConfigClientCore _client;

    /// <summary>
    /// Serialises overlapping loads.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>A gate rather than a cancellation.</b> <c>OnNavigatedTo</c> fires on every arrival and
    /// the Refresh button is one click away from it, so two walks can overlap; the second one
    /// finishing first would leave the table showing the older disk. Dropping the re-entrant call
    /// is right here because both calls would produce the same answer — unlike a backup, where the
    /// second request means something different from the first.
    /// </remarks>
    private bool _loading;

    internal OpenCodeFootprintViewModel(AgentConfigClientCore client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        Rows = [];
    }

    /// <summary>One row per category, in the catalog's deliberate prune order.</summary>
    public ObservableCollection<OpenCodeFootprintRowViewModel> Rows { get; }

    /// <summary>True while a walk is in flight — the view swaps the table for a progress line.</summary>
    [ObservableProperty] private bool _isBusy;

    /// <summary>
    /// Aggregate of every row, humanised; empty until the first walk completes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMeasured))]
    private string _totalDisplay = string.Empty;

    /// <summary>Whether a walk has completed, so the view can hold the total line back.</summary>
    public bool HasMeasured => !string.IsNullOrEmpty(TotalDisplay);

    /// <summary>
    /// The total line — "52.6 MB across 6 categories".
    /// </summary>
    /// <remarks>
    /// ⚠ Counts every category, including the ones measuring zero. A category that found nothing
    /// is still a category that was checked, and dropping it from the count would make the number
    /// move for a reason the user cannot see.
    /// </remarks>
    public string TotalLine => string.Format(
        CultureInfo.CurrentCulture, Strings.LabelFootprintTotalFmt, TotalDisplay, Rows.Count);

    /// <inheritdoc/>
    /// <remarks>
    /// Re-measured on every arrival rather than cached: the numbers are a point-in-time snapshot
    /// of a directory that OpenCode is still writing to, and a stale one looks exactly like a
    /// fresh one.
    /// </remarks>
    public void OnNavigatedTo() => RefreshCommand.Execute(null);

    /// <summary>Walk the categories and rebuild the table.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        IsBusy = true;
        try
        {
            IReadOnlyList<FootprintCategoryStats> stats =
                await _client.GetFootprintStatsAsync(CancellationToken.None).ConfigureAwait(true);

            Rows.Clear();
            long total = 0;
            foreach (FootprintCategoryStats row in stats)
            {
                Rows.Add(new OpenCodeFootprintRowViewModel(row));
                total += row.TotalBytes;
            }

            TotalDisplay = OpenCodeFootprintRowViewModel.FormatBytes(total);
            OnPropertyChanged(nameof(TotalLine));

            Log.Information(
                "[Footprint] measured categories={Count} totalBytes={Bytes}", Rows.Count, total);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Narrow on purpose — a directory that vanished or refused a read mid-walk is the one
            // thing a caller can act on, and it must not take the window down. Anything else is a
            // bug worth surfacing, per the repo's never-swallow rule.
            Log.Warning(ex, "[Footprint] the walk could not complete");
            TotalDisplay = string.Empty;
            OnPropertyChanged(nameof(TotalLine));
        }
        finally
        {
            IsBusy = false;
            _loading = false;
        }
    }
}
