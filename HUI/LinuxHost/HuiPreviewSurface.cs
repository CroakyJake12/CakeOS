using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Haven.UI;
using Haven.UI.Components;
using HuiButton = Haven.UI.Components.Button;
using HuiText = Haven.UI.Components.Text;

namespace CakeOS.HuiLinuxHost;

public sealed class HuiPreviewSurface : Control, IHavenMeasureContext
{
    private readonly Page _root;
    private readonly HuiButton _action;
    private readonly HuiText _status;
    private readonly HavenLayoutEngine _layout = new();
    private readonly HavenSceneRenderer _renderer = new();
    private readonly HavenInputRouter _input;

    public HuiPreviewSurface()
    {
        Focusable = true;
        ClipToBounds = true;
        (_root, _action, _status) = BuildScene();
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
        foreach (var command in _renderer.Render(_root))
        {
            switch (command)
            {
                case HavenFillRoundedRectCommand fill:
                    context.DrawRectangle(Brush(fill.Brush, fill.Opacity), null, Rect(fill.Rect), fill.Radius, fill.Radius);
                    break;
                case HavenStrokeRoundedRectCommand stroke:
                    context.DrawRectangle(null, Pen(stroke.Pen, stroke.Opacity), Rect(stroke.Rect), stroke.Radius, stroke.Radius);
                    break;
                case HavenTextCommand text:
                    var formatted = Text(text.Layout, Brush(text.Brush, text.Opacity));
                    context.DrawText(formatted, new Point(text.Rect.X, text.Rect.Y));
                    break;
                case HavenLineCommand line:
                    context.DrawLine(Pen(line.Pen, line.Opacity), Point(line.Start), Point(line.End));
                    break;
                case HavenEllipseCommand ellipse:
                    context.DrawEllipse(Brush(ellipse.Brush, ellipse.Opacity), ellipse.Pen is null ? null : Pen(ellipse.Pen, ellipse.Opacity), Rect(ellipse.Rect));
                    break;
                default:
                    throw new NotSupportedException($"Graphical preview backend does not yet support {command.GetType().Name}.");
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        _input.PointerMoved(new HavenPoint(p.X, p.Y), PointerKind(e.Pointer.Type));
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        _input.PointerPressed(new HavenPoint(p.X, p.Y), PointerKind(e.Pointer.Type), HavenPointerButton.Primary);
        Focus();
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var p = e.GetPosition(this);
        _input.PointerReleased(new HavenPoint(p.X, p.Y));
        e.Pointer.Capture(null);
        e.Handled = true;
        InvalidateMeasure();
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
        _input.PointerPressed(center);
        if (!_input.PointerReleased(center) || !_status.Content.Contains("pointer", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("HUI pointer activation self-test failed.");

        _status.Content = "Ready for keyboard activation";
        _input.Focus(_action);
        if (!_input.KeyDown(HavenKey.Enter) || !_input.KeyUp(HavenKey.Enter) || !_status.Content.Contains("pointer", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("HUI keyboard activation self-test failed.");

        _status.Content = "HUI pointer + keyboard input passed";
        InvalidateMeasure();
        InvalidateVisual();
    }

    public HavenSize MeasureLeaf(HavenElement element, HavenSize available)
    {
        return element switch
        {
            HuiText text => MeasureText(text.Content, text.GetValue(HavenProperties.FontSize), available),
            HuiButton button => new HavenSize(Math.Min(available.Width, Math.Max(160, button.Content.Length * 9 + 40)), Math.Min(available.Height, 46)),
            _ => new HavenSize(Math.Min(available.Width, 48), Math.Min(available.Height, 48)),
        };
    }

    private static (Page Root, HuiButton Action, HuiText Status) BuildScene()
    {
        var root = new Page { Name = "PreviewRoot", Layout = HavenLayout.Vertical };
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
        action.ClickActions.Add(HavenAction.Parse("Name.Status -> Content=HUI pointer input activated"));

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

    private static HavenSize MeasureText(string value, double size, HavenSize available)
    {
        var fontSize = size <= 0 ? 14d : size;
        var width = Math.Min(available.Width, Math.Max(24d, value.Length * fontSize * .55d));
        return new HavenSize(width, Math.Min(available.Height, fontSize * 1.45d));
    }

    private static FormattedText Text(HavenTextLayout layout, IBrush brush) =>
        new(layout.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, layout.Italic ? FontStyle.Italic : FontStyle.Normal, new FontWeight(layout.FontWeight), FontStretch.Normal),
            layout.FontSize <= 0 ? 14d : layout.FontSize, brush)
        { MaxTextWidth = Math.Max(1d, layout.MaxWidth) };

    private static IBrush Brush(HavenBrush brush, double opacity)
    {
        Color color = brush switch
        {
            HavenSolidBrush solid => Color.FromArgb(solid.A, solid.R, solid.G, solid.B),
            HavenTokenBrush token when token.Token.Contains("Accent", StringComparison.OrdinalIgnoreCase) => Color.Parse("#8A7CFF"),
            HavenTokenBrush token when token.Token.Contains("Secondary", StringComparison.OrdinalIgnoreCase) => Color.Parse("#A8AFBD"),
            HavenTokenBrush token when token.Token.Contains("Text", StringComparison.OrdinalIgnoreCase) => Color.Parse("#F5F7FB"),
            HavenTokenBrush => Color.Parse("#242834"),
            _ => Color.Parse("#242834"),
        };
        var alpha = (byte)Math.Clamp(Math.Round(color.A * opacity), 0, 255);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
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
