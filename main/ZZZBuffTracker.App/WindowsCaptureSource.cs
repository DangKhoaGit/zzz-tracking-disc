using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT;
using ZZZBuffTracker.Application;
using ZZZBuffTracker.Detection;

namespace ZZZBuffTracker.App;

/// <summary>WGC owns two GPU buffers; polling drains old frames at 10 Hz before CPU readback.</summary>
public sealed class WindowsCaptureSource(GraphicsCaptureItem item, IClock clock) : IFrameSource
{
    public async IAsyncEnumerable<GrayFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!GraphicsCaptureSession.IsSupported()) throw new NotSupportedException("Windows Graphics Capture is unavailable.");
        using var device = CreateDevice();
        var size = item.Size;
        if (size.Width < 1 || size.Height < 1 || size.Width > 7680 || size.Height > 4320)
            throw new InvalidOperationException("Capture dimensions unsupported (maximum 7680×4320).");
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
        using var session = pool.CreateCaptureSession(item);
        var closed = 0;
        void OnClosed(GraphicsCaptureItem sender, object args) => Interlocked.Exchange(ref closed, 1);
        item.Closed += OnClosed;
        try
        {
            session.IsCursorCaptureEnabled = false;
            session.StartCapture();
            long sequence = 0;
            var lastReceived = clock.Elapsed;
            using var ticker = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await ticker.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (Volatile.Read(ref closed) != 0) throw new InvalidOperationException("Capture target closed.");
                Direct3D11CaptureFrame? frame = pool.TryGetNextFrame();
                if (frame is null)
                {
                    if (clock.Elapsed - lastReceived > TimeSpan.FromSeconds(3)) throw new TimeoutException("No capture frame for 3 seconds; target may be minimized or unavailable.");
                    continue;
                }
                // Drain at most the pool capacity, disposing each superseded surface.
                var newer = pool.TryGetNextFrame();
                if (newer is not null) { frame.Dispose(); frame = newer; }
                using (frame)
                {
                    lastReceived = clock.Elapsed;
                    if (frame.ContentSize.Width != size.Width || frame.ContentSize.Height != size.Height)
                        throw new InvalidOperationException("Capture size changed; select target again and verify client/ROI.");
                    // WGC SystemRelativeTime and Stopwatch both use QPC. Account for frame age before readback.
                    var qpcNow = TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
                    var capturedAt = clock.Elapsed - (qpcNow - frame.SystemRelativeTime);
                    if (capturedAt < TimeSpan.Zero) capturedAt = TimeSpan.Zero;
                    using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Ignore);
                    cancellationToken.ThrowIfCancellationRequested();
                    var buffer = new Windows.Storage.Streams.Buffer(checked((uint)(bitmap.PixelWidth * bitmap.PixelHeight * 4)));
                    bitmap.CopyToBuffer(buffer);
                    var bgra = new byte[buffer.Length];
                    using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(bgra);
                    var pixels = new byte[size.Width * size.Height];
                    for (var y = 0; y < size.Height; y++)
                    for (var x = 0; x < size.Width; x++)
                    {
                        var i = (y * bitmap.PixelWidth + x) * 4;
                        pixels[y * size.Width + x] = (byte)((77 * bgra[i + 2] + 150 * bgra[i + 1] + 29 * bgra[i]) >> 8);
                    }
                    if (pixels.Max() - pixels.Min() < 3)
                        throw new InvalidOperationException("Capture image is flat/black; refusing to interpret it as icon absence.");
                    yield return new(sequence++, capturedAt, size.Width, size.Height, pixels);
                }
            }
        }
        finally { item.Closed -= OnClosed; }
    }

    private static IDirect3DDevice CreateDevice()
    {
        nint native = 0, context = 0, dxgi = 0, inspectable = 0;
        try
        {
            // BGRA support; hardware first. WARP is a supported software fallback.
            var hr = D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out native, out _, out context);
            if (hr < 0)
            {
                if (native != 0) { Marshal.Release(native); native = 0; }
                if (context != 0) { Marshal.Release(context); context = 0; }
                hr = D3D11CreateDevice(0, 5, 0, 0x20, 0, 0, 7, out native, out _, out context);
            }
            Marshal.ThrowExceptionForHR(hr);
            var iid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(native, in iid, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable));
            return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            if (inspectable != 0) Marshal.Release(inspectable);
            if (dxgi != 0) Marshal.Release(dxgi);
            if (context != 0) Marshal.Release(context);
            if (native != 0) Marshal.Release(native);
        }
    }
    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags,
        nint featureLevels, uint featureLevelCount, uint sdkVersion, out nint device, out int featureLevel, out nint context);
    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint device, out nint graphicsDevice);
}
