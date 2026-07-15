using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace Bennewitz.Ninja.ClaudeForge.Views;

/// <summary>
/// Body for the Hooks group's "Flow" tab: the hook lifecycle diagram SVG rendered
/// at natural size inside a scrollable, zoomable surface. Selected by
/// <see cref="GroupTabBodyTemplate"/> for
/// <see cref="ViewModels.ClaudeGroupTabCustomizer.HooksFlowId"/>.
/// </summary>
/// <remarks>
/// Zoom is pure view state, so it lives here in code-behind rather than the editor
/// VM. Ctrl+MouseWheel zooms toward the cursor; the toolbar buttons and
/// Fit-width/Reset zoom about the viewport centre. The zoom is applied as the
/// <c>LayoutTransform</c> of a <c>LayoutTransformControl</c> so the enclosing
/// <c>ScrollViewer</c>'s scroll extent grows/shrinks with the zoom.
/// </remarks>
public partial class HooksFlowView : UserControl
{
    private const double MinZoom = 0.25;
    private const double MaxZoom = 5.0;
    private const double WheelStep = 1.1;   // multiplicative per wheel notch
    private const double ButtonStep = 0.25; // additive per +/- click
    private const double HostMargin = 16;   // ZoomHost.Margin (per side)

    private readonly ScaleTransform _scale = new();
    private double _zoom = 1.0;
    private bool _initialized;

    public HooksFlowView()
    {
        InitializeComponent();
        ZoomHost.LayoutTransform = _scale;

        // Tunnel so we intercept Ctrl+wheel BEFORE the ScrollViewer consumes it
        // for scrolling; plain wheel (no Ctrl) falls through to normal scroll.
        Scroller.AddHandler(PointerWheelChangedEvent, OnPointerWheel, RoutingStrategies.Tunnel);

        // Default view = fit-to-width. The SVG's natural size isn't known until it
        // has laid out, so attempt the fit on load and retry on layout until the
        // content + viewport report real sizes.
        Loaded += (_, _) =>
        {
            if (!TryInitialFit())
            {
                Scroller.LayoutUpdated += OnLayoutUpdatedForInitialFit;
            }
        };
    }

    private void OnLayoutUpdatedForInitialFit(object? sender, EventArgs e)
    {
        if (TryInitialFit())
        {
            Scroller.LayoutUpdated -= OnLayoutUpdatedForInitialFit;
        }
    }

    private bool TryInitialFit()
    {
        if (_initialized)
        {
            return true;
        }

        if (ContentNaturalWidth() <= 0 || Scroller.Viewport.Width <= 0)
        {
            return false; // not laid out yet
        }

        _initialized = true;
        FitWidth();
        return true;
    }

    // ── Zoom application ─────────────────────────────────────────────────────

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        _scale.ScaleX = _zoom;
        _scale.ScaleY = _zoom;
        ZoomReadout.Text = string.Create(CultureInfo.CurrentCulture, $"{_zoom * 100:0}%");
    }

    /// <summary>Zoom to <paramref name="newZoom"/> keeping <paramref name="anchor"/>
    /// (a point in the ScrollViewer viewport) over the same content pixel.</summary>
    private void ZoomTo(double newZoom, Point anchor)
    {
        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
        double old = _zoom;
        if (Math.Abs(newZoom - old) < 0.0001)
        {
            return;
        }

        Vector offset = Scroller.Offset;
        // Content pixel (unscaled) currently under the anchor.
        double cx = (offset.X + anchor.X) / old;
        double cy = (offset.Y + anchor.Y) / old;

        SetZoom(newZoom);

        // The ScrollViewer's extent only updates on the next layout pass, so
        // re-anchor the offset after that (clamped to the valid range).
        Dispatcher.UIThread.Post(
            () => Scroller.Offset = new Vector(
                Math.Max(0, cx * _zoom - anchor.X),
                Math.Max(0, cy * _zoom - anchor.Y)),
            DispatcherPriority.Render);
    }

    private void FitWidth()
    {
        double contentWidth = ContentNaturalWidth();
        double available = Scroller.Viewport.Width - (2 * HostMargin);
        if (contentWidth <= 0 || available <= 0)
        {
            SetZoom(1.0);
            return;
        }

        SetZoom(available / contentWidth);
        Dispatcher.UIThread.Post(
            () => Scroller.Offset = new Vector(0, 0),
            DispatcherPriority.Render);
    }

    private Point ViewportCenter() =>
        new(Scroller.Viewport.Width / 2, Scroller.Viewport.Height / 2);

    /// <summary>Unscaled width of the SVG content. The child of the
    /// LayoutTransformControl is measured/arranged at its natural size (the
    /// transform is applied on top), so its Bounds are pre-zoom.</summary>
    private double ContentNaturalWidth() =>
        (ZoomHost.Child as Control)?.Bounds.Width ?? 0;

    // ── Input handlers ───────────────────────────────────────────────────────

    private void OnPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return; // let the ScrollViewer scroll normally
        }

        e.Handled = true;
        double factor = e.Delta.Y >= 0 ? WheelStep : 1 / WheelStep;
        ZoomTo(_zoom * factor, e.GetPosition(Scroller));
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => ZoomTo(_zoom + ButtonStep, ViewportCenter());

    private void OnZoomOut(object? sender, RoutedEventArgs e) => ZoomTo(_zoom - ButtonStep, ViewportCenter());

    private void OnResetZoom(object? sender, RoutedEventArgs e) => ZoomTo(1.0, ViewportCenter());

    private void OnFitWidth(object? sender, RoutedEventArgs e) => FitWidth();
}
