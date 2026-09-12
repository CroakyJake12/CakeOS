using System.Runtime.InteropServices;
using System.Text;

namespace CakeOS.HuiLinuxHost.Canvas;

internal enum CanvasStrokeTool : uint
{
    Pen = 0,
    Eraser = 1,
}

internal readonly record struct CanvasDocumentBounds(double X, double Y, double Width, double Height);
internal readonly record struct CanvasSvgFrame(CanvasDocumentBounds Bounds, string Svg);

internal sealed class CanvasNativeSession : IDisposable
{
    private const uint ExpectedAbiVersion = 1;
    private const uint ExpectedRenderFormatSvg = 1;
    private const uint ExpectedCoordinateSpaceDocument = 1;

    private IntPtr _handle;
    private bool _disposed;

    public CanvasNativeSession()
    {
        ValidateAbi();
        _handle = Native.cake_canvas_engine_new();
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("Canvas native engine creation failed.");
    }

    private CanvasNativeSession(IntPtr handle)
    {
        ValidateAbi();
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("Canvas native engine restore returned a null handle.");
        _handle = handle;
    }

    ~CanvasNativeSession() => Dispose(disposing: false);

    public bool CanUndo
    {
        get
        {
            ThrowIfDisposed();
            return Native.cake_canvas_can_undo(_handle) != 0;
        }
    }

    public bool CanRedo
    {
        get
        {
            ThrowIfDisposed();
            return Native.cake_canvas_can_redo(_handle) != 0;
        }
    }

    public static CanvasNativeSession FromRnote(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
            throw new ArgumentException("Canvas Rnote payload must not be empty.", nameof(bytes));

        ValidateAbi();
        var status = Native.cake_canvas_engine_from_rnote(bytes, (nuint)bytes.Length, out var handle);
        EnsureOk(status, "restore Rnote payload");
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("Canvas native Rnote restore succeeded without returning an engine handle.");

        return new CanvasNativeSession(handle);
    }

    public void SetTool(CanvasStrokeTool tool)
    {
        ThrowIfDisposed();
        EnsureOk(Native.cake_canvas_set_stroke_tool(_handle, (uint)tool), "set stroke tool");
    }

    public void BeginStroke(double x, double y, double pressure, double tiltX = 0, double tiltY = 0)
    {
        ThrowIfDisposed();
        EnsureOk(Native.cake_canvas_begin_stroke(_handle, Sample(x, y, pressure, tiltX, tiltY)), "begin stroke");
    }

    public void UpdateStroke(double x, double y, double pressure, double tiltX = 0, double tiltY = 0)
    {
        ThrowIfDisposed();
        EnsureOk(Native.cake_canvas_update_stroke(_handle, Sample(x, y, pressure, tiltX, tiltY)), "update stroke");
    }

    public void EndStroke(double x, double y, double pressure, double tiltX = 0, double tiltY = 0)
    {
        ThrowIfDisposed();
        EnsureOk(Native.cake_canvas_end_stroke(_handle, Sample(x, y, pressure, tiltX, tiltY)), "end stroke");
    }

    public bool Undo()
    {
        ThrowIfDisposed();
        return EnsureHistoryResult(Native.cake_canvas_undo(_handle), "undo");
    }

    public bool Redo()
    {
        ThrowIfDisposed();
        return EnsureHistoryResult(Native.cake_canvas_redo(_handle), "redo");
    }

    public CanvasSvgFrame RenderSvg()
    {
        ThrowIfDisposed();
        var status = Native.cake_canvas_render_frame(_handle, out var frame);
        EnsureOk(status, "render frame");

        try
        {
            if (frame.Format != ExpectedRenderFormatSvg)
                throw new InvalidOperationException($"Canvas native bridge returned unsupported render format {frame.Format}.");
            if (frame.CoordinateSpace != ExpectedCoordinateSpaceDocument)
                throw new InvalidOperationException($"Canvas native bridge returned unsupported coordinate space {frame.CoordinateSpace}.");
            if (frame.Data == IntPtr.Zero || frame.Length == 0)
                throw new InvalidOperationException("Canvas native bridge returned an empty SVG frame.");
            if (frame.Length > int.MaxValue)
                throw new InvalidOperationException("Canvas SVG frame is too large for the managed preview boundary.");

            var bytes = new byte[(int)frame.Length];
            Marshal.Copy(frame.Data, bytes, 0, bytes.Length);
            var svg = Encoding.UTF8.GetString(bytes);
            if (!svg.Contains("<svg", StringComparison.Ordinal))
                throw new InvalidOperationException("Canvas native frame was not valid SVG text.");

            return new CanvasSvgFrame(
                new CanvasDocumentBounds(frame.X, frame.Y, frame.Width, frame.Height),
                svg);
        }
        finally
        {
            Native.cake_canvas_render_frame_release(ref frame);
        }
    }

    public byte[] SaveRnote()
    {
        ThrowIfDisposed();
        var status = Native.cake_canvas_save_rnote(_handle, out var buffer);
        EnsureOk(status, "save Rnote payload");

        try
        {
            if (buffer.Data == IntPtr.Zero || buffer.Length == 0)
                throw new InvalidOperationException("Canvas native bridge returned an empty Rnote payload.");
            if (buffer.Length > int.MaxValue)
                throw new InvalidOperationException("Canvas Rnote payload is too large for the managed preview boundary.");

            var bytes = new byte[(int)buffer.Length];
            Marshal.Copy(buffer.Data, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            Native.cake_canvas_buffer_release(ref buffer);
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle == IntPtr.Zero) return;
        Native.cake_canvas_engine_free(_handle);
        _handle = IntPtr.Zero;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static void ValidateAbi()
    {
        var abi = Native.cake_canvas_abi_version();
        if (abi != ExpectedAbiVersion)
            throw new InvalidOperationException($"Canvas native ABI mismatch. Expected {ExpectedAbiVersion}, got {abi}.");
    }

    private static NativePointerSample Sample(double x, double y, double pressure, double tiltX, double tiltY) => new()
    {
        X = x,
        Y = y,
        Pressure = pressure,
        TiltX = tiltX,
        TiltY = tiltY,
    };

    private static bool EnsureHistoryResult(CanvasStatus status, string operation) => status switch
    {
        CanvasStatus.Ok => true,
        CanvasStatus.NoChange => false,
        _ => throw new InvalidOperationException($"Canvas native {operation} failed with status {(int)status} ({status})."),
    };

    private static void EnsureOk(CanvasStatus status, string operation)
    {
        if (status != CanvasStatus.Ok)
            throw new InvalidOperationException($"Canvas native {operation} failed with status {(int)status} ({status}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePointerSample
    {
        public double X;
        public double Y;
        public double Pressure;
        public double TiltX;
        public double TiltY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRenderFrame
    {
        public uint Format;
        public uint CoordinateSpace;
        public double X;
        public double Y;
        public double Width;
        public double Height;
        public IntPtr Data;
        public nuint Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBuffer
    {
        public IntPtr Data;
        public nuint Length;
    }

    private enum CanvasStatus : int
    {
        Ok = 0,
        NoChange = 1,
        InvalidHandle = -1,
        InvalidArgument = -2,
        InvalidState = -3,
        EngineError = -4,
        Panic = -5,
    }

    private static class Native
    {
        private const string Library = "cakeos_canvas_rnote_poc";

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint cake_canvas_abi_version();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr cake_canvas_engine_new();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void cake_canvas_engine_free(IntPtr handle);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_engine_from_rnote(
            [In] byte[] data,
            nuint len,
            out IntPtr outHandle);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_stroke_tool(IntPtr handle, uint tool);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_begin_stroke(IntPtr handle, NativePointerSample sample);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_update_stroke(IntPtr handle, NativePointerSample sample);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_end_stroke(IntPtr handle, NativePointerSample sample);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte cake_canvas_can_undo(IntPtr handle);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte cake_canvas_can_redo(IntPtr handle);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_undo(IntPtr handle);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_redo(IntPtr handle);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_render_frame(IntPtr handle, out NativeRenderFrame frame);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void cake_canvas_render_frame_release(ref NativeRenderFrame frame);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_save_rnote(IntPtr handle, out NativeBuffer buffer);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void cake_canvas_buffer_release(ref NativeBuffer buffer);
    }
}
