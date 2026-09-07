using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Haven.UI;
using Haven.UI.Components;
using HuiButton = Haven.UI.Components.Button;
using HuiPage = Haven.UI.Components.Page;
using HuiText = Haven.UI.Components.Text;

namespace CakeOS.HuiLinuxHost;

public sealed class HuiPreviewSurface : Control, IHavenMeasureContext
{
    private readonly HuiPage _root;
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

    private static HavenSize MeasureText(string value, double size, HavenSize available)
    {
        var fontSize = size <= 0 ? 14d : size;
        var width = Math.Min(available.Width, Math.Max(24d, value.Length * fontSize * .55d));
        return new HavenSize(width, Math.Min(available.Height, fontSize * 1.45d));
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
