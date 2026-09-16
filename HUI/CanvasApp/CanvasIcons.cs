namespace CakeOS.Canvas.App;

/// <summary>
/// Hand-authored stroke icon geometry for the Canvas toolbar, in the same
/// 24x24 line-icon spirit as the Haven catalog. Rendered with
/// <c>StrokeThickness ~1.7</c> round caps; no emoji, no text glyphs.
/// </summary>
public static class CanvasIcons
{
    public const string Pen =
        "M5 19 L6.2 15.8 L14.5 7.5 L16.5 9.5 L8.2 17.8 Z M13.2 4.8 L15.5 3.5 L20.5 8.5 L19.2 10.8 L16.9 8.5 Z M4 21 L4.5 20 L6 20.5 Z";
    public const string Highlighter =
        "M4 16 L6 14 L14 6 L18 10 L10 18 L6 20 Z M13 5 L15 3 L21 9 L19 11 Z M3 21 L8 21 L8 19 L4 19 Z";
    public const string Eraser =
        "M7.5 12.5 L12 8 L18.5 14.5 L13 19.5 L6.5 19.5 L4.5 17.5 Z M12 8 L14.5 5.5 L19 10 L18.5 14.5 M4.5 17.5 L6.5 19.5";
    public const string Select =
        "M7 3.5 L16.5 12 L11.5 12.5 L14 19 L11.5 20 L9 13.5 L5.5 16.5 Z";
    public const string Shape =
        "M4 5 L13 5 L13 14 L4 14 Z M16 15 m-4 0 a4 4 0 1 0 8 0 a4 4 0 1 0 -8 0";
    public const string ShapeRect = "M5 7 L19 7 L19 17 L5 17 Z";
    public const string ShapeEllipse = "M12 5 m-7 0 a7 7 0 1 0 14 0 a7 7 0 1 0 -14 0";
    public const string ShapeLine = "M5 19 L19 5";
    public const string ShapeArrow = "M5 19 L19 5 M12 5 L19 5 L19 12";
    public const string Undo = "M9 5 L4.5 9.5 L9 14 M4.5 9.5 L15 9.5 A6 6 0 0 1 15 17 L15 20";
    public const string Redo = "M15 5 L19.5 9.5 L15 14 M19.5 9.5 L9 9.5 A6 6 0 0 0 9 17 L9 20";
    public const string ZoomIn = "M11 5 L11 11 L5 11 M11 11 L17 11 M11 17 L11 11 M4 11 A7 7 0 1 0 11 4 A7 7 0 1 0 4 11 M16 16 L21 21";
    public const string ZoomOut = "M5 11 L19 11 M4 11 A7 7 0 1 0 11 4 A7 7 0 1 0 4 11 M16 16 L21 21";
    public const string Fit = "M4 9 L4 4 L9 4 M15 4 L20 4 L20 9 M20 15 L20 20 L15 20 M9 20 L4 20 L4 15";
    public const string FolderOpen = "M3 6 L10 6 L12 9 L21 9 L21 19 L3 19 Z M3 6 L3 19";
}
