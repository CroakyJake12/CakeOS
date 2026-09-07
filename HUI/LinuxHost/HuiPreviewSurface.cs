using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Svg.Skia;
using CakeOS.HuiLinuxHost.Canvas;
using Haven.UI;
using Haven.UI.Components;
using HuiButton = Haven.UI.Components.Button;
using HuiImage = Haven.UI.Components.Image;
using HuiPage = Haven.UI.Components.Page;
using HuiText = Haven.UI.Components.Text;

namespace CakeOS.HuiLinuxHost;

public sealed class HuiPreviewSurface : Control, IHavenMeasureContext, IDisposable
{
    private const string CanvasFrameSource = "cake-canvas://rnote-frame";
    private const double CanvasMinZoom = 1d;
    private const double CanvasMaxZoom = 32d;
    private const double CanvasInitialZoom = 4d;

    private readonly HuiPage _root;
    private readonly HuiButton _action;
    private readonly HuiText _status;
    private readonly HavenLayoutEngine _layout = new();
    private readonly HavenSceneRenderer _renderer = new();
    private readonly HavenInputRouter _input;
    private readonly bool _canvasMode;
    private readonly CanvasNativeSession? _canvasSession;
    private readonly HuiImage? _canvasElement;
    private SvgSource? _canvasSvgSource;
    private SvgImage? _canvasSvgImage;
    private CanvasDocumentBounds? _canvasDocumentBounds;
    private bool _canvasStrokeActive;
    private bool _canvasPanActive;
    private IPointer? _canvasPanPointer;
    private Point _canvasPanLastSurfacePoint;
    private double _canvasZoom = CanvasInitialZoom;
    private double _canvasViewportCenterX;
    private double _canvasViewportCenterY;
    private bool _canvasViewportInitialized;
    private bool _disposed;

    public HuiPreviewSurface()
    {
        Focusable = true;
        ClipToBounds = true;
        _canvasMode = Environment.GetEnvironmentVariable("CAKEOS_HUI_CANVAS_PREVIEW") == "1";

        if (_canvasMode)
        {
            var scene = BuildCanvasScene();
            _root = scene.Root;
            _action = scene.Action;
            _status = scene.Status;
            _canvasElement = scene.Canvas;
            _canvasSession = new CanvasNativeSession();
            InitializeCanvasFrame(_canvasSession);
        }
        else
        {
            (_root, _action, _status) = BuildScene();
        }

        _input = new HavenInputRouter(_root);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 960d;
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : 600d;
        _layout.Layout(_root, new HavenSize(width, height), HavenPlatform.Linux, this);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _layout.Layout(_root, new HavenSize(finalSize.Width, finalSize.Height), HavenPlatform.Linux, this);
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var scopes = new Stack<IDisposable>();
        try
        {
            foreach (var command in _renderer.Render(_root))
            {
                switch (command)
                {
                    case HavenPushTransformCommand push:
                    {
                        var transform = Matrix.CreateTranslation(-push.Origin.X, -push.Origin.Y)
                            * Matrix.CreateScale(push.Transform.ScaleX, push.Transform.ScaleY)
                            * Matrix.CreateRotation(push.Transform.RotationDegrees * Math.PI / 180d)
                            * Matrix.CreateTranslation(
                                push.Origin.X + push.Transform.TranslateX,
                                push.Origin.Y + push.Transform.TranslateY);
                        scopes.Push(context.PushTransform(transform));
                        continue;
                    }
                    case HavenPushClipCommand clip:
                        scopes.Push(context.PushClip(Rect(clip.Rect)));
                        continue;
                    case HavenPopTransformCommand or HavenPopClipCommand:
                        if (scopes.Count == 0)
                            throw new InvalidOperationException("HUI renderer emitted an unbalanced transform/clip pop.");
                        scopes.Pop().Dispose();
                        continue;
                    case HavenFillRoundedRectCommand fill:
                        context.DrawRectangle(Brush(fill.Brush, fill.Opacity), null, Rect(fill.Rect), fill.Radius, fill.Radius);
                        break;
                    case HavenStrokeRoundedRectCommand stroke:
                        context.DrawRectangle(null, Pen(stroke.Pen, stroke.Opacity), Rect(stroke.Rect), stroke.Radius, stroke.Radius);
                        break;
                    case HavenTextCommand text:
                    {
                        var formatted = Text(text.Layout, Brush(text.Brush, text.Opacity));
                        var y = text.Layout.CenterVertically
                            ? text.Rect.Y + Math.Max(0d, (text.Rect.Height - formatted.Height) / 2d)
                            : text.Rect.Y;
                        context.DrawText(formatted, new Point(text.Rect.X, y));
                        break;
                    }
                    case HavenLineCommand line:
                        context.DrawLine(Pen(line.Pen, line.Opacity), Point(line.Start), Point(line.End));
                        break;
                    case HavenEllipseCommand ellipse:
                        context.DrawEllipse(Brush(ellipse.Brush, ellipse.Opacity), ellipse.Pen is null ? null : Pen(ellipse.Pen, ellipse.Opacity), Rect(ellipse.Rect));
                        break;
                    case HavenImageCommand image:
                        DrawImage(context, image);
                        break;
                    case HavenShadowCommand shadow:
                        DrawEffect(context, shadow.Rect, shadow.Radius, shadow.Shadow.Brush, shadow.Shadow.OffsetX, shadow.Shadow.OffsetY, shadow.Shadow.Blur, shadow.Shadow.Spread, shadow.Opacity);
                        break;
                    case HavenGlowCommand glow:
                        DrawEffect(context, glow.Rect, glow.Radius, glow.Glow.Brush, 0d, 0d, glow.Glow.Blur, 0d, glow.Opacity);
                        break;
                    default:
                        throw new NotSupportedException($"Graphical preview backend does not yet support {command.GetType().Name}.");
                }
            }

            if (scopes.Count != 0)
                throw new InvalidOperationException("HUI renderer emitted unbalanced transform/clip pushes.");
        }
        finally
        {
            while (scopes.Count > 0) scopes.Pop().Dispose();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);

        if (_canvasPanActive && ReferenceEquals(_canvasPanPointer, e.Pointer))
        {
            UpdateCanvasPan(p);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        _input.PointerMoved(new HavenPoint(p.X, p.Y), PointerKind(e.Pointer.Type));

        if (_canvasStrokeActive)
            UpdateCanvasStroke(e, p);

        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);

        if (TryBeginCanvasPan(e, p))
        {
            Focus();
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        _input.PointerPressed(new HavenPoint(p.X, p.Y), PointerKind(e.Pointer.Type), HavenPointerButton.Primary);
        TryBeginCanvasStroke(e, p);
        Focus();
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var p = e.GetPosition(this);

        if (_canvasPanActive && ReferenceEquals(_canvasPanPointer, e.Pointer))
        {
            EndCanvasPan(p);
            e.Pointer.Capture(null);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        _input.PointerReleased(new HavenPoint(p.X, p.Y));

        if (_canvasStrokeActive)
            EndCanvasStroke(e, p);

        e.Pointer.Capture(null);
        e.Handled = true;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!_canvasMode || _canvasStrokeActive || _canvasPanActive || _canvasElement is null || Math.Abs(e.Delta.Y) < 0.0001d)
            return;

        var point = e.GetPosition(this);
        if (!Rect(_canvasElement.Bounds).Contains(point))
            return;

        ZoomCanvasViewport(point, e.Delta.Y);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var key = e.Key switch
        {
            Key.Enter => HavenKey.Enter,
            Key.Space => HavenKey.Space,
            Key.Tab => HavenKey.Tab,
            Key.Escape => HavenKey.Escape,
            _ => HavenKey.Unknown,
        };
        if (key == HavenKey.Unknown || !_input.KeyDown(key)) return;
        e.Handled = true;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        var key = e.Key switch
        {
            Key.Enter => HavenKey.Enter,
            Key.Space => HavenKey.Space,
            Key.Tab => HavenKey.Tab,
            Key.Escape => HavenKey.Escape,
            _ => HavenKey.Unknown,
        };
        if (key == HavenKey.Unknown || !_input.KeyUp(key)) return;
        e.Handled = true;
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void RunInputSelfTest()
    {
        _layout.Layout(_root, new HavenSize(Math.Max(1, Bounds.Width), Math.Max(1, Bounds.Height)), HavenPlatform.Linux, this);
        var center = new HavenPoint(_action.Bounds.X + _action.Bounds.Width / 2d, _action.Bounds.Y + _action.Bounds.Height / 2d);
        _action.SetState(HavenElementState.Selected, false);
        _action.Accessibility.Selected = false;
        _input.PointerPressed(center);
        if (!_input.PointerReleased(center) || _action.Accessibility.Selected != true)
            throw new InvalidOperationException("HUI pointer activation self-test failed.");

        _action.SetState(HavenElementState.Selected, false);
        _action.Accessibility.Selected = false;
        _input.Focus(_action);
        if (!_input.KeyDown(HavenKey.Enter) || !_input.KeyUp(HavenKey.Enter) || _action.Accessibility.Selected != true)
            throw new InvalidOperationException("HUI keyboard activation self-test failed.");

        _status.Content = _canvasMode
            ? $"Rnote through HUI; input passed; viewport {_canvasZoom:0.##}x"
            : "HUI pointer + keyboard input passed";
        InvalidateMeasure();
        InvalidateVisual();
    }

    public HavenSize MeasureLeaf(HavenElement element, HavenSize available)
    {
        return element switch
        {
            HuiText text => MeasureText(text, available),
            HuiButton button => new HavenSize(Math.Min(available.Width, Math.Max(160, button.Content.Length * 9 + 40)), Math.Min(available.Height, 46)),
            HuiImage => new HavenSize(Math.Min(available.Width, 820), Math.Min(available.Height, 360)),
            _ => new HavenSize(Math.Min(available.Width, 48), Math.Min(available.Height, 48)),
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _canvasSvgImage = null;
        _canvasSvgSource?.Dispose();
        _canvasSvgSource = null;
        _canvasSession?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void InitializeCanvasFrame(CanvasNativeSession session)
    {
        session.SetTool(CanvasStrokeTool.Pen);
        session.BeginStroke(120, 120, 0.18, 12, -5);
        session.UpdateStroke(160, 145, 0.35, 10, -4);
        session.UpdateStroke(210, 165, 0.62, 8, -3);
        session.UpdateStroke(260, 205, 0.88, 6, -2);
        session.EndStroke(315, 235, 0.56, 4, -1);

        if (!session.CanUndo || !session.Undo() || !session.CanRedo || !session.Redo())
            throw new InvalidOperationException("Canvas managed/native undo-redo proof failed.");

        var frame = RefreshCanvasFrame(session);
        _status.Content = $"Rnote ABI 1 / SVG / document {frame.Bounds.Width:0} x {frame.Bounds.Height:0} / viewport {_canvasZoom:0.##}x";
        Console.WriteLine(
            $"CANVAS_RNOTE_HUI_RENDER_READY abi=1 format=svg coordinate=document width={frame.Bounds.Width:0.###} height={frame.Bounds.Height:0.###}");
    }

    private CanvasSvgFrame RefreshCanvasFrame(CanvasNativeSession session)
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

        var previous = _canvasSvgSource;
        _canvasSvgSource = source;
        _canvasSvgImage = image;
        _canvasDocumentBounds = frame.Bounds;
        if (!_canvasViewportInitialized)
        {
            _canvasViewportCenterX = frame.Bounds.X;
            _canvasViewportCenterY = frame.Bounds.Y;
            _canvasViewportInitialized = true;
        }
        previous?.Dispose();
        return frame;
    }

    private bool TryBeginCanvasStroke(PointerPressedEventArgs e, Point surfacePoint)
    {
        if (!_canvasMode || _canvasSession is null || _canvasElement is null || _canvasStrokeActive || _canvasPanActive)
            return false;
        if (e.Pointer.Type == PointerType.Touch)
            return false;

        var properties = e.GetCurrentPoint(this).Properties;
        if (e.Pointer.Type == PointerType.Mouse && !properties.IsLeftButtonPressed)
            return false;
        if (!TrySurfaceToDocument(surfacePoint, requireInside: true, out var documentPoint))
            return false;

        var pressure = PointerPressure(e.Pointer.Type, properties.Pressure);
        _canvasSession.SetTool(CanvasStrokeTool.Pen);
        _canvasSession.BeginStroke(documentPoint.X, documentPoint.Y, pressure, properties.XTilt, properties.YTilt);
        _canvasStrokeActive = true;
        return true;
    }

    private void UpdateCanvasStroke(PointerEventArgs e, Point surfacePoint)
    {
        if (_canvasSession is null || !TrySurfaceToDocument(surfacePoint, requireInside: false, out var documentPoint))
            return;

        var properties = e.GetCurrentPoint(this).Properties;
        var pressure = PointerPressure(e.Pointer.Type, properties.Pressure);
        _canvasSession.UpdateStroke(documentPoint.X, documentPoint.Y, pressure, properties.XTilt, properties.YTilt);
    }

    private void EndCanvasStroke(PointerReleasedEventArgs e, Point surfacePoint)
    {
        if (_canvasSession is null || !TrySurfaceToDocument(surfacePoint, requireInside: false, out var documentPoint))
        {
            _canvasStrokeActive = false;
            return;
        }

        var properties = e.GetCurrentPoint(this).Properties;
        var pressure = PointerPressure(e.Pointer.Type, properties.Pressure);
        _canvasSession.EndStroke(documentPoint.X, documentPoint.Y, pressure, properties.XTilt, properties.YTilt);
        _canvasStrokeActive = false;
        var frame = RefreshCanvasFrame(_canvasSession);
        _status.Content = $"Pointer ink committed through HUI to Rnote; viewport {_canvasZoom:0.##}x; undo available";
        Console.WriteLine(
            $"CANVAS_RNOTE_POINTER_STROKE_COMMITTED pointer={e.Pointer.Type} pressure={pressure:0.###} width={frame.Bounds.Width:0.###} height={frame.Bounds.Height:0.###} zoom={_canvasZoom:0.###}");
    }

    private bool TryBeginCanvasPan(PointerPressedEventArgs e, Point surfacePoint)
    {
        if (!_canvasMode || _canvasElement is null || _canvasStrokeActive || _canvasPanActive)
            return false;

        var target = Rect(_canvasElement.Bounds);
        if (!target.Contains(surfacePoint))
            return false;

        var properties = e.GetCurrentPoint(this).Properties;
        var isPanGesture = e.Pointer.Type == PointerType.Touch
            || (e.Pointer.Type == PointerType.Mouse && (properties.IsMiddleButtonPressed || properties.IsRightButtonPressed));
        if (!isPanGesture)
            return false;

        _canvasPanActive = true;
        _canvasPanPointer = e.Pointer;
        _canvasPanLastSurfacePoint = surfacePoint;
        return true;
    }

    private void UpdateCanvasPan(Point surfacePoint)
    {
        if (_canvasElement is null || _canvasDocumentBounds is null)
            return;

        var target = Rect(_canvasElement.Bounds);
        if (target.Width <= 0 || target.Height <= 0)
            return;

        var viewport = CanvasViewport(target);
        var delta = surfacePoint - _canvasPanLastSurfacePoint;
        _canvasViewportCenterX -= delta.X / target.Width * viewport.Width;
        _canvasViewportCenterY -= delta.Y / target.Height * viewport.Height;
        _canvasPanLastSurfacePoint = surfacePoint;
        _ = CanvasViewport(target);
        _status.Content = $"HUI viewport pan / {_canvasZoom:0.##}x";
    }

    private void EndCanvasPan(Point surfacePoint)
    {
        UpdateCanvasPan(surfacePoint);
        _canvasPanActive = false;
        _canvasPanPointer = null;
        if (_canvasElement is null)
            return;

        var viewport = CanvasViewport(Rect(_canvasElement.Bounds));
        Console.WriteLine(
            $"CANVAS_RNOTE_VIEWPORT_PAN_COMMITTED zoom={_canvasZoom:0.###} x={viewport.X:0.###} y={viewport.Y:0.###} width={viewport.Width:0.###} height={viewport.Height:0.###}");
    }

    private void ZoomCanvasViewport(Point surfacePoint, double wheelDelta)
    {
        if (_canvasElement is null || _canvasDocumentBounds is not { } bounds)
            return;

        var target = Rect(_canvasElement.Bounds);
        if (target.Width <= 0 || target.Height <= 0)
            return;

        var oldViewport = CanvasViewport(target);
        var u = Math.Clamp((surfacePoint.X - target.X) / target.Width, 0d, 1d);
        var v = Math.Clamp((surfacePoint.Y - target.Y) / target.Height, 0d, 1d);
        var documentX = oldViewport.X + u * oldViewport.Width;
        var documentY = oldViewport.Y + v * oldViewport.Height;
        var nextZoom = Math.Clamp(_canvasZoom * Math.Pow(1.25d, wheelDelta), CanvasMinZoom, CanvasMaxZoom);
        if (Math.Abs(nextZoom - _canvasZoom) < 0.0001d)
            return;

        _canvasZoom = nextZoom;
        var nextSize = CanvasViewportSize(target, bounds, _canvasZoom);
        _canvasViewportCenterX = documentX - (u - 0.5d) * nextSize.Width;
        _canvasViewportCenterY = documentY - (v - 0.5d) * nextSize.Height;
        var viewport = CanvasViewport(target);
        _status.Content = $"HUI viewport {_canvasZoom:0.##}x";
        Console.WriteLine(
            $"CANVAS_RNOTE_VIEWPORT_ZOOM zoom={_canvasZoom:0.###} x={viewport.X:0.###} y={viewport.Y:0.###} width={viewport.Width:0.###} height={viewport.Height:0.###}");
    }

    private bool TrySurfaceToDocument(Point surfacePoint, bool requireInside, out (double X, double Y) documentPoint)
    {
        documentPoint = default;
        if (_canvasElement is null || _canvasDocumentBounds is null)
            return false;

        var target = Rect(_canvasElement.Bounds);
        if (target.Width <= 0 || target.Height <= 0)
            return false;
        if (requireInside && !target.Contains(surfacePoint))
            return false;

        var viewport = CanvasViewport(target);
        var x = Math.Clamp((surfacePoint.X - target.X) / target.Width, 0d, 1d);
        var y = Math.Clamp((surfacePoint.Y - target.Y) / target.Height, 0d, 1d);
        documentPoint = (viewport.X + x * viewport.Width, viewport.Y + y * viewport.Height);
        return true;
    }

    private Rect CanvasViewport(Rect target)
    {
        if (_canvasDocumentBounds is not { } bounds)
            return default;

        var size = CanvasViewportSize(target, bounds, _canvasZoom);
        if (!_canvasViewportInitialized)
        {
            _canvasViewportCenterX = bounds.X;
            _canvasViewportCenterY = bounds.Y;
            _canvasViewportInitialized = true;
        }

        var minCenterX = bounds.X + size.Width / 2d;
        var maxCenterX = bounds.X + bounds.Width - size.Width / 2d;
        var minCenterY = bounds.Y + size.Height / 2d;
        var maxCenterY = bounds.Y + bounds.Height - size.Height / 2d;
        _canvasViewportCenterX = Math.Clamp(_canvasViewportCenterX, minCenterX, maxCenterX);
        _canvasViewportCenterY = Math.Clamp(_canvasViewportCenterY, minCenterY, maxCenterY);
        return new Rect(
            _canvasViewportCenterX - size.Width / 2d,
            _canvasViewportCenterY - size.Height / 2d,
            size.Width,
            size.Height);
    }

    private static Size CanvasViewportSize(Rect target, CanvasDocumentBounds bounds, double zoom)
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

        var clampedZoom = Math.Clamp(zoom, CanvasMinZoom, CanvasMaxZoom);
        return new Size(Math.Max(1d, baseWidth / clampedZoom), Math.Max(1d, baseHeight / clampedZoom));
    }

    private static double PointerPressure(PointerType pointerType, float pressure) =>
        pointerType == PointerType.Mouse || pressure <= 0 ? 0.5d : Math.Clamp(pressure, 0f, 1f);

    private void DrawImage(DrawingContext context, HavenImageCommand command)
    {
        if (!_canvasMode || command.Image.Source != CanvasFrameSource || _canvasSvgImage is null || _canvasDocumentBounds is not { } bounds)
            throw new NotSupportedException($"Graphical preview cannot resolve HUI image source '{command.Image.Source}'.");

        var target = Rect(command.Rect);
        var viewport = CanvasViewport(target);
        if (viewport.Width <= 0 || viewport.Height <= 0)
            return;

        var source = new Rect(
            viewport.X - bounds.X,
            viewport.Y - bounds.Y,
            viewport.Width,
            viewport.Height);
        using var opacity = context.PushOpacity(Math.Clamp(command.Opacity, 0d, 1d));
        using var clip = context.PushClip(target);
        context.DrawImage(_canvasSvgImage, source, target);
    }

    private static (HuiPage Root, HuiButton Action, HuiText Status) BuildScene()
    {
        var root = new HuiPage { Name = "PreviewRoot", Layout = HavenLayout.Vertical };
        root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        root.SetValue(HavenProperties.Padding, HavenThickness.Parse("42px"));
        root.SetValue(HavenProperties.Gap, HavenLength.Px(18));

        var eyebrow = new HuiText { Content = "CAKEOS / HUI LINUX BACKEND" };
        eyebrow.SetValue(HavenProperties.FontSize, 13d);
        eyebrow.SetValue(HavenProperties.Foreground, "TextSecondary");

        var title = new HuiText { Content = "A real HUI scene, rendered as a Linux desktop window." };
        title.SetValue(HavenProperties.FontSize, 30d);
        title.SetValue(HavenProperties.Foreground, "TextPrimary");

        var body = new HuiText { Content = "GNOME and Mutter are untouched. This preview is an ordinary unprivileged process translating HUI draw commands through the Linux Avalonia backend." };
        body.SetValue(HavenProperties.FontSize, 16d);
        body.SetValue(HavenProperties.Foreground, "TextSecondary");

        var action = new HuiButton { Name = "Action", Content = "Test HUI input" };
        action.SetValue(HavenProperties.Width, HavenLength.Px(190));
        action.SetValue(HavenProperties.Height, HavenLength.Px(46));
        action.ClickActions.Add(HavenAction.Parse("Name.Action -> Selected=True"));

        var status = new HuiText { Name = "Status", Content = "Ready for pointer or keyboard input" };
        status.SetValue(HavenProperties.FontSize, 15d);
        status.SetValue(HavenProperties.Foreground, "TextSecondary");

        root.Add(eyebrow);
        root.Add(title);
        root.Add(body);
        root.Add(action);
        root.Add(status);
        return (root, action, status);
    }

    private static (HuiPage Root, HuiButton Action, HuiText Status, HuiImage Canvas) BuildCanvasScene()
    {
        var root = new HuiPage { Name = "CanvasPreviewRoot", Layout = HavenLayout.Vertical };
        root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        root.SetValue(HavenProperties.Padding, HavenThickness.Parse("24px"));
        root.SetValue(HavenProperties.Gap, HavenLength.Px(10));

        var eyebrow = new HuiText { Content = "CAKEOS / HUI CANVAS / RNOTE ENGINE" };
        eyebrow.SetValue(HavenProperties.FontSize, 12d);
        eyebrow.SetValue(HavenProperties.Foreground, "TextSecondary");

        var title = new HuiText { Content = "Canvas is rendering through HUI." };
        title.SetValue(HavenProperties.FontSize, 28d);
        title.SetValue(HavenProperties.Foreground, "TextPrimary");

        var body = new HuiText
        {
            Content = "HUI owns ink and viewport state. Wheel zoom is cursor-anchored; touch or middle/right drag pans; left mouse and pen contact map into Rnote document coordinates."
        };
        body.SetValue(HavenProperties.FontSize, 14d);
        body.SetValue(HavenProperties.Foreground, "TextSecondary");

        var canvas = new HuiImage { Name = "CanvasFrame", Source = CanvasFrameSource, Fit = HavenImageFit.Contain };
        canvas.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        canvas.SetValue(HavenProperties.Height, HavenLength.Px(360));

        var action = new HuiButton { Name = "Action", Content = "Test HUI input" };
        action.SetValue(HavenProperties.Width, HavenLength.Px(190));
        action.SetValue(HavenProperties.Height, HavenLength.Px(42));
        action.ClickActions.Add(HavenAction.Parse("Name.Action -> Selected=True"));

        var status = new HuiText { Name = "Status", Content = "Starting Rnote bridge..." };
        status.SetValue(HavenProperties.FontSize, 13d);
        status.SetValue(HavenProperties.Foreground, "TextSecondary");

        root.Add(eyebrow);
        root.Add(title);
        root.Add(body);
        root.Add(canvas);
        root.Add(action);
        root.Add(status);
        return (root, action, status, canvas);
    }

    private static HavenSize MeasureText(HuiText text, HavenSize available)
    {
        var fontSize = text.GetValue(HavenProperties.FontSize);
        if (fontSize <= 0) fontSize = 14d;
        var maxWidth = double.IsFinite(available.Width) ? Math.Max(1d, available.Width) : 10000d;
        var formatted = Text(
            new HavenTextLayout(
                text.Content,
                text.GetValue(HavenProperties.FontFamily),
                fontSize,
                text.GetValue(HavenProperties.FontWeight),
                maxWidth),
            Brushes.Transparent);
        return new HavenSize(
            Math.Min(available.Width, formatted.Width + 2d),
            Math.Min(available.Height, formatted.Height + 2d));
    }

    private static FormattedText Text(HavenTextLayout layout, IBrush brush)
    {
        var maxWidth = double.IsFinite(layout.MaxWidth) ? Math.Max(1d, layout.MaxWidth) : 10000d;
        return new FormattedText(layout.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, layout.Italic ? FontStyle.Italic : FontStyle.Normal, Weight(layout.FontWeight), FontStretch.Normal),
            layout.FontSize <= 0 ? 14d : layout.FontSize, brush)
        { MaxTextWidth = maxWidth };
    }

    private static FontWeight Weight(int weight) => weight switch
    {
        >= 800 => FontWeight.ExtraBold,
        >= 700 => FontWeight.Bold,
        >= 600 => FontWeight.SemiBold,
        >= 500 => FontWeight.Medium,
        _ => FontWeight.Normal,
    };

    private static IBrush Brush(HavenBrush brush, double opacity) =>
        new SolidColorBrush(ApplyOpacity(ColorFor(brush), opacity));

    private static Color ColorFor(HavenBrush brush) => brush switch
    {
        HavenSolidBrush solid => Color.FromArgb(solid.A, solid.R, solid.G, solid.B),
        HavenTokenBrush token when token.Token.Contains("Accent", StringComparison.OrdinalIgnoreCase) => Color.Parse("#8A7CFF"),
        HavenTokenBrush token when token.Token.Contains("Secondary", StringComparison.OrdinalIgnoreCase) => Color.Parse("#A8AFBD"),
        HavenTokenBrush token when token.Token.Contains("Text", StringComparison.OrdinalIgnoreCase) => Color.Parse("#F5F7FB"),
        HavenTokenBrush token when token.Token.Contains("Transparent", StringComparison.OrdinalIgnoreCase) => Colors.Transparent,
        HavenTokenBrush => Color.Parse("#242834"),
        _ => Color.Parse("#242834"),
    };

    private static Color ApplyOpacity(Color color, double opacity)
    {
        var alpha = (byte)Math.Clamp(Math.Round(color.A * opacity), 0d, 255d);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static void DrawEffect(
        DrawingContext context,
        HavenRect bounds,
        double radius,
        HavenBrush brush,
        double offsetX,
        double offsetY,
        double blur,
        double spread,
        double opacity)
    {
        var shadows = new BoxShadows(new BoxShadow
        {
            OffsetX = offsetX,
            OffsetY = offsetY,
            Blur = Math.Max(0d, blur),
            Spread = spread,
            Color = ApplyOpacity(ColorFor(brush), opacity),
        });
        context.DrawRectangle(Brushes.Transparent, null, Rect(bounds), radius, radius, shadows);
    }

    private static IPen Pen(HavenPen pen, double opacity) => new Pen(Brush(pen.Brush, opacity), pen.Thickness);
    private static Rect Rect(HavenRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
    private static Point Point(HavenPoint point) => new(point.X, point.Y);
    private static HavenPointerKind PointerKind(PointerType type) => type switch
    {
        PointerType.Touch => HavenPointerKind.Touch,
        PointerType.Pen => HavenPointerKind.Pen,
        _ => HavenPointerKind.Mouse,
    };
}
