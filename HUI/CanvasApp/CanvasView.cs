using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Svg.Skia;

namespace CakeOS.Canvas.App;

/// <summary>
/// The Canvas drawing surface. Renders the live Rnote SVG frame cropped to
/// the HUI-style document viewport and routes pointer/pen/touch/wheel input
/// to the session. It draws ink only: toolbar, menus and status live in
/// <see cref="CanvasShell"/>. Touch and middle/right-drag pan (platform
/// parity); pen and primary-mouse contact draw with the selected tool.
/// </summary>
public sealed class CanvasView : Control, IDisposable
{
    private readonly CanvasController _controller;
    private SvgSource? _svgSource;
    private SvgImage? _svgImage;
    private CanvasDocumentBounds? _documentBounds;
    private bool _strokeActive;
    private bool _panActive;
    private IPointer? _panPointer;
    private Point _panLastPoint;
    private double _viewportCenterX;
    private double _viewportCenterY;
    private bool _viewportInitialized;
    private bool _disposed;

    public CanvasView(CanvasController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        Focusable = true;
        ClipToBounds = true;
        _controller.StateChanged += OnControllerStateChanged;
    }

    public event Action? ViewChanged;

    public void ResetDocument()
    {
        _svgImage = null;
        _svgSource?.Dispose();
        _svgSource = null;
        _documentBounds = null;
        _viewportInitialized = false;
        _strokeActive = false;
        _panActive = false;
        _panPointer = null;
        RefreshFrame();
        InvalidateVisual();
    }

    /// <summary>Refresh the SVG frame after an operation that changed pixels.</summary>
    public void RefreshFrame()
    {
        var session = _controller.Session;
        try
        {
            var frame = session.RenderSvg();
            if (frame.Bounds.Width <= 0 || frame.Bounds.Height <= 0)
                throw new InvalidOperationException("Canvas native frame returned invalid document bounds.");
            var source = SvgSource.LoadFromSvg(frame.Svg);
            if (source.Picture is null)
            {
                source.Dispose();
                throw new InvalidOperationException("Canvas SVG decoder produced no renderable picture.");
            }
            var image = new SvgImage { Source = source };
            if (image.Size.Width <= 0 || image.Size.Height <= 0)
            {
                source.Dispose();
                throw new InvalidOperationException("Canvas SVG decoder produced invalid image dimensions.");
            }
            var previous = _svgSource;
            _svgSource = source;
            _svgImage = image;
            _documentBounds = frame.Bounds;
            if (!_viewportInitialized)
            {
                _viewportCenterX = Math.Clamp(0d, frame.Bounds.X, frame.Bounds.X + frame.Bounds.Width);
                _viewportCenterY = Math.Clamp(0d, frame.Bounds.Y, frame.Bounds.Y + frame.Bounds.Height);
                _viewportInitialized = true;
            }
            previous?.Dispose();
            Console.WriteLine(
                $"CANVAS_RNOTE_HUI_RENDER_READY abi=3 format=svg coordinate=document width={frame.Bounds.Width:0.###} height={frame.Bounds.Height:0.###}");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("empty", StringComparison.OrdinalIgnoreCase))
        {
            // Blank document: no renderable content yet. Show the paper placeholder.
            _svgImage = null;
            _svgSource?.Dispose();
            _svgSource = null;
            _documentBounds = null;
        }
        ViewChanged?.Invoke();
    }

    public void FitToContent()
    {
        if (_documentBounds is not { } bounds || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        var fitZoom = Math.Min(Bounds.Width / Math.Max(1d, bounds.Width), Bounds.Height / Math.Max(1d, bounds.Height));
        _controller.SetZoom(Math.Clamp(fitZoom, CanvasController.MinZoom, CanvasController.MaxZoom));
        _viewportCenterX = bounds.X + bounds.Width / 2d;
        _viewportCenterY = bounds.Y + bounds.Height / 2d;
        _viewportInitialized = true;
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var target = new Rect(0, 0, Bounds.Width, Bounds.Height);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#14161c")), target);
        if (_svgImage is null || _documentBounds is not { } bounds)
        {
            DrawPaperPlaceholder(context, target);
            return;
        }
        var viewport = Viewport(target, bounds);
        if (viewport.Width <= 0 || viewport.Height <= 0)
            return;
        var source = new Rect(
            viewport.X - bounds.X,
            viewport.Y - bounds.Y,
            viewport.Width,
            viewport.Height);
        using var clip = context.PushClip(target);
        context.DrawImage(_svgImage, source, target);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_panActive && ReferenceEquals(_panPointer, e.Pointer))
        {
            UpdatePan(p);
            InvalidateVisual();
            return;
        }
        if (_strokeActive)
            UpdateStroke(e, p);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        if (TryBeginPan(e, p))
        {
            Focus();
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            return;
        }
        if (TryBeginStroke(e, p))
        {
            Focus();
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var p = e.GetPosition(this);
        if (_panActive && ReferenceEquals(_panPointer, e.Pointer))
        {
            EndPan(p);
            e.Pointer.Capture(null);
            e.Handled = true;
            InvalidateVisual();
            return;
        }
        if (_strokeActive)
            EndStroke(e, p);
        e.Pointer.Capture(null);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_strokeActive || _panActive || Math.Abs(e.Delta.Y) < 0.0001d)
            return;
        ZoomAt(e.GetPosition(this), e.Delta.Y);
        e.Handled = true;
        InvalidateVisual();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller.StateChanged -= OnControllerStateChanged;
        _svgImage = null;
        _svgSource?.Dispose();
        _svgSource = null;
        GC.SuppressFinalize(this);
    }

    private void OnControllerStateChanged() => InvalidateVisual();

    private static void DrawPaperPlaceholder(DrawingContext context, Rect target)
    {
        context.FillRectangle(new SolidColorBrush(Color.Parse("#f4f5f7")), target);
        var dot = new SolidColorBrush(Color.Parse("#d5d9e0"));
        const double step = 28d;
        const double radius = 1.6d;
        for (var y = step; y < target.Height; y += step)
        {
            for (var x = step; x < target.Width; x += step)
            {
                context.DrawEllipse(dot, null, new Point(x, y), radius, radius);
            }
        }
    }

    private bool TryBeginStroke(PointerPressedEventArgs e, Point surfacePoint)
    {
        if (_strokeActive || _panActive)
            return false;
        if (e.Pointer.Type == PointerType.Touch)
            return false;
        var properties = e.GetCurrentPoint(this).Properties;
        if (e.Pointer.Type == PointerType.Mouse && !properties.IsLeftButtonPressed)
            return false;
        if (!TrySurfaceToDocument(surfacePoint, requireInside: true, out var documentPoint))
            return false;
        // Blank document: any press inside the paper starts the world.
        var session = _controller.Session;
        var pressure = PointerPressure(e.Pointer.Type, properties.Pressure);
        session.BeginStroke(documentPoint.X, documentPoint.Y, pressure, properties.XTilt, properties.YTilt);
        _strokeActive = true;
        return true;
    }

    private void UpdateStroke(PointerEventArgs e, Point surfacePoint)
    {
        if (!TrySurfaceToDocument(surfacePoint, requireInside: false, out var documentPoint))
            return;
        var properties = e.GetCurrentPoint(this).Properties;
        var pressure = PointerPressure(e.Pointer.Type, properties.Pressure);
        _controller.Session.UpdateStroke(documentPoint.X, documentPoint.Y, pressure, properties.XTilt, properties.YTilt);
    }

    private void EndStroke(PointerReleasedEventArgs e, Point surfacePoint)
    {
        var session = _controller.Session;
        if (!TrySurfaceToDocument(surfacePoint, requireInside: false, out var documentPoint))
        {
            _strokeActive = false;
            return;
        }
        var properties = e.GetCurrentPoint(this).Properties;
        var pressure = PointerPressure(e.Pointer.Type, properties.Pressure);
        session.EndStroke(documentPoint.X, documentPoint.Y, pressure, properties.XTilt, properties.YTilt);
        _strokeActive = false;
        RefreshFrame();
        _controller.MarkDirty();
        var frame = _documentBounds;
        _controller.SetStatus(
            $"{_controller.Tool} stroke committed; {_controller.Zoom:0.#}x" +
            (frame is CanvasDocumentBounds b ? $"; {b.Width:0}x{b.Height:0}" : string.Empty));
        Console.WriteLine(
            $"CANVAS_RNOTE_POINTER_STROKE_COMMITTED pointer={e.Pointer.Type} tool={_controller.Tool} pressure={pressure:0.###} zoom={_controller.Zoom:0.###}");
        ViewChanged?.Invoke();
    }

    private bool TryBeginPan(PointerPressedEventArgs e, Point surfacePoint)
    {
        if (_strokeActive || _panActive)
            return false;
        var properties = e.GetCurrentPoint(this).Properties;
        var isPanGesture = e.Pointer.Type == PointerType.Touch
            || (e.Pointer.Type == PointerType.Mouse && (properties.IsMiddleButtonPressed || properties.IsRightButtonPressed));
        if (!isPanGesture)
            return false;
        _panActive = true;
        _panPointer = e.Pointer;
        _panLastPoint = surfacePoint;
        return true;
    }

    private void UpdatePan(Point surfacePoint)
    {
        if (_documentBounds is not { } bounds || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        var target = new Rect(0, 0, Bounds.Width, Bounds.Height);
        var viewport = Viewport(target, bounds);
        var delta = surfacePoint - _panLastPoint;
        _viewportCenterX -= delta.X / target.Width * viewport.Width;
        _viewportCenterY -= delta.Y / target.Height * viewport.Height;
        _panLastPoint = surfacePoint;
        _ = Viewport(target, bounds);
        _controller.SetStatus($"Pan / {_controller.Zoom:0.#}x");
    }

    private void EndPan(Point surfacePoint)
    {
        UpdatePan(surfacePoint);
        _panActive = false;
        _panPointer = null;
        var bounds = _documentBounds;
        var viewport = bounds is CanvasDocumentBounds b && Bounds.Width > 0
            ? Viewport(new Rect(0, 0, Bounds.Width, Bounds.Height), b)
            : default;
        Console.WriteLine(
            $"CANVAS_RNOTE_VIEWPORT_PAN_COMMITTED zoom={_controller.Zoom:0.###} x={viewport.X:0.###} y={viewport.Y:0.###} width={viewport.Width:0.###} height={viewport.Height:0.###}");
        ViewChanged?.Invoke();
    }

    private void ZoomAt(Point surfacePoint, double wheelDelta)
    {
        if (_documentBounds is not { } bounds || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        var target = new Rect(0, 0, Bounds.Width, Bounds.Height);
        var oldViewport = Viewport(target, bounds);
        var u = Math.Clamp(surfacePoint.X / target.Width, 0d, 1d);
        var v = Math.Clamp(surfacePoint.Y / target.Height, 0d, 1d);
        var documentX = oldViewport.X + u * oldViewport.Width;
        var documentY = oldViewport.Y + v * oldViewport.Height;
        var nextZoom = Math.Clamp(
            _controller.Zoom * Math.Pow(1.25d, wheelDelta),
            CanvasController.MinZoom, CanvasController.MaxZoom);
        if (Math.Abs(nextZoom - _controller.Zoom) < 0.0001d)
            return;
        _controller.SetZoom(nextZoom);
        var nextSize = ViewportSize(target, bounds, nextZoom);
        _viewportCenterX = documentX - (u - 0.5d) * nextSize.Width;
        _viewportCenterY = documentY - (v - 0.5d) * nextSize.Height;
        var viewport = Viewport(target, bounds);
        _controller.SetStatus($"Zoom {_controller.Zoom:0.#}x");
        Console.WriteLine(
            $"CANVAS_RNOTE_VIEWPORT_ZOOM zoom={_controller.Zoom:0.###} x={viewport.X:0.###} y={viewport.Y:0.###} width={viewport.Width:0.###} height={viewport.Height:0.###}");
        ViewChanged?.Invoke();
    }

    private bool TrySurfaceToDocument(Point surfacePoint, bool requireInside, out (double X, double Y) documentPoint)
    {
        // Blank document: the paper maps 1:1 to a fresh world origin.
        if (_documentBounds is null)
        {
            documentPoint = (surfacePoint.X, surfacePoint.Y);
            return !requireInside || new Rect(0, 0, Bounds.Width, Bounds.Height).Contains(surfacePoint);
        }
        documentPoint = default;
        if (_documentBounds is not CanvasDocumentBounds bounds)
            return false;
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return false;
        var target = new Rect(0, 0, Bounds.Width, Bounds.Height);
        if (requireInside && !target.Contains(surfacePoint))
            return false;
        var viewport = Viewport(target, bounds);
        var x = Math.Clamp(surfacePoint.X / target.Width, 0d, 1d);
        var y = Math.Clamp(surfacePoint.Y / target.Height, 0d, 1d);
        documentPoint = (viewport.X + x * viewport.Width, viewport.Y + y * viewport.Height);
        return true;
    }

    private Rect Viewport(Rect target, CanvasDocumentBounds bounds)
    {
        var size = ViewportSize(target, bounds, _controller.Zoom);
        if (!_viewportInitialized)
        {
            _viewportCenterX = bounds.X;
            _viewportCenterY = bounds.Y;
            _viewportInitialized = true;
        }
        var minCenterX = bounds.X + size.Width / 2d;
        var maxCenterX = bounds.X + bounds.Width - size.Width / 2d;
        var minCenterY = bounds.Y + size.Height / 2d;
        var maxCenterY = bounds.Y + bounds.Height - size.Height / 2d;
        _viewportCenterX = Math.Clamp(_viewportCenterX, minCenterX, maxCenterX);
        _viewportCenterY = Math.Clamp(_viewportCenterY, minCenterY, maxCenterY);
        return new Rect(
            _viewportCenterX - size.Width / 2d,
            _viewportCenterY - size.Height / 2d,
            size.Width,
            size.Height);
    }

    private static Size ViewportSize(Rect target, CanvasDocumentBounds bounds, double zoom)
    {
        if (target.Width <= 0 || target.Height <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            return default;
        var targetAspect = target.Width / target.Height;
        var documentAspect = bounds.Width / bounds.Height;
        double baseWidth;
        double baseHeight;
        if (documentAspect > targetAspect)
        {
            baseHeight = bounds.Height;
            baseWidth = baseHeight * targetAspect;
        }
        else
        {
            baseWidth = bounds.Width;
            baseHeight = baseWidth / targetAspect;
        }
        var clampedZoom = Math.Clamp(zoom, CanvasController.MinZoom, CanvasController.MaxZoom);
        return new Size(Math.Max(1d, baseWidth / clampedZoom), Math.Max(1d, baseHeight / clampedZoom));
    }

    private static double PointerPressure(PointerType pointerType, float pressure) =>
        pointerType == PointerType.Mouse || pressure <= 0 ? 0.5d : Math.Clamp(pressure, 0f, 1f);
}
