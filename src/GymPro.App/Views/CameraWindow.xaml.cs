using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GymPro.App.Infrastructure;
using GymPro.App.Theming;
using GymPro.Data.Services;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace GymPro.App.Views;

/// <summary>
/// Live webcam capture using the Windows camera API (no third-party library).
/// With several cameras attached the user picks one; the choice is remembered on this PC.
/// </summary>
[SuppressMessage("Design", "CA1001", Justification = "The camera is released in StopAsync when the window closes; windows aren't IDisposable.")]
public partial class CameraWindow : Window
{
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private WriteableBitmap? _frame;
    private byte[] _buffer = [];
    private int _rendering; // 1 while a frame is being copied to the UI: drop frames instead of queueing them
    private bool _closing;

    public CameraWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadCamerasAsync();
        Closing += async (_, _) =>
        {
            _closing = true;
            await StopAsync();
        };
    }

    /// <summary>The captured photo as JPEG (square, stored size), when the user pressed "Use this photo".</summary>
    public byte[]? Photo { get; private set; }

    public sealed record CameraInfo(string Id, string Name);

    public static async Task<IReadOnlyList<CameraInfo>> FindCamerasAsync()
    {
        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        return [.. devices.Where(d => d.IsEnabled).Select(d => new CameraInfo(d.Id, d.Name))];
    }

    private async Task LoadCamerasAsync()
    {
        IReadOnlyList<CameraInfo> cameras;
        try
        {
            cameras = await FindCamerasAsync();
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            ShowMessage("Windows didn't allow listing cameras: " + ex.Message);
            return;
        }

        if (cameras.Count == 0)
        {
            ShowMessage("No camera found. Connect a webcam, or use \"From file\" instead.");
            CameraBox.Visibility = Visibility.Collapsed;
            return;
        }

        CameraBox.ItemsSource = cameras;
        CameraBox.Visibility = cameras.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        var last = LocalUiSettings.Load().LastCameraId;
        CameraBox.SelectedItem = cameras.FirstOrDefault(c => c.Id == last) ?? cameras[0]; // triggers OnCameraChanged
    }

    private async void OnCameraChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CameraBox.SelectedItem is CameraInfo cam)
        {
            LocalUiSettings.Save(LocalUiSettings.Load() with { LastCameraId = cam.Id });
            await StartAsync(cam.Id);
        }
    }

    private async Task StartAsync(string deviceId)
    {
        await StopAsync();
        MessagePanel.Visibility = Visibility.Collapsed;
        CaptureButton.IsEnabled = false;
        try
        {
            _capture = new MediaCapture();
            await _capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = deviceId,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly, // don't fight other apps (e.g. Teams) for the camera
            });

            var source = _capture.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color)
                ?? throw new InvalidOperationException("This camera doesn't provide a colour video stream.");
            _reader = await _capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            _reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            _reader.FrameArrived += OnFrameArrived;
            var status = await _reader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success)
            {
                throw new InvalidOperationException($"The camera couldn't start ({status}). It may be in use by another app.");
            }

            CaptureButton.IsEnabled = true;
        }
        catch (UnauthorizedAccessException)
        {
            ShowMessage("Camera access is blocked. Allow it in Windows Settings > Privacy & security > Camera > \"Let desktop apps access your camera\".");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            ShowMessage("The camera couldn't start: " + ex.Message);
        }
    }

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (_closing || Interlocked.Exchange(ref _rendering, 1) == 1)
        {
            return; // UI still busy with the previous frame
        }

        using var frame = sender.TryAcquireLatestFrame();
        var source = frame?.VideoMediaFrame?.SoftwareBitmap;
        if (source is null)
        {
            _rendering = 0;
            return;
        }

        using var bgra = SoftwareBitmap.Convert(source, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var width = bgra.PixelWidth;
        var height = bgra.PixelHeight;
        var needed = width * height * 4;
        if (_buffer.Length != needed)
        {
            _buffer = new byte[needed];
        }

        bgra.CopyToBuffer(_buffer.AsBuffer());
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (_frame is null || _frame.PixelWidth != width || _frame.PixelHeight != height)
                {
                    _frame = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
                    Preview.Source = _frame;
                }

                _frame.WritePixels(new Int32Rect(0, 0, width, height), _buffer, width * 4, 0);
            }
            finally
            {
                _rendering = 0;
            }
        });
    }

    private void OnCapture(object sender, RoutedEventArgs e)
    {
        if (_frame is null)
        {
            return;
        }

        // Centre square of the (un-mirrored) frame, scaled to the stored photo size.
        var side = Math.Min(_frame.PixelWidth, _frame.PixelHeight);
        var crop = new CroppedBitmap(_frame.Clone(), new Int32Rect((_frame.PixelWidth - side) / 2, (_frame.PixelHeight - side) / 2, side, side));
        BitmapSource shot = crop;
        if (side > PhotoSizes.Photo)
        {
            var scale = (double)PhotoSizes.Photo / side;
            shot = new TransformedBitmap(crop, new ScaleTransform(scale, scale));
        }

        shot.Freeze();
        Photo = Images.EncodeJpeg(shot, PhotoSizes.PhotoQuality);
        Captured.Source = shot;
        Captured.Visibility = Visibility.Visible;
        Preview.Visibility = Guide.Visibility = Visibility.Collapsed;
        CaptureButton.Visibility = Visibility.Collapsed;
        RetakeButton.Visibility = UseButton.Visibility = Visibility.Visible;
        UseButton.Focus();
    }

    private void OnRetake(object sender, RoutedEventArgs e)
    {
        Photo = null;
        Captured.Visibility = Visibility.Collapsed;
        Preview.Visibility = Guide.Visibility = Visibility.Visible;
        CaptureButton.Visibility = Visibility.Visible;
        RetakeButton.Visibility = UseButton.Visibility = Visibility.Collapsed;
    }

    private void OnUse(object sender, RoutedEventArgs e) => DialogResult = Photo is not null;

    private void ShowMessage(string text)
    {
        MessageText.Text = text;
        MessagePanel.Visibility = Visibility.Visible;
        Guide.Visibility = Visibility.Collapsed;
        CaptureButton.IsEnabled = false;
    }

    private async Task StopAsync()
    {
        if (_reader is not null)
        {
            _reader.FrameArrived -= OnFrameArrived;
            await _reader.StopAsync();
            _reader.Dispose();
            _reader = null;
        }

        _capture?.Dispose();
        _capture = null;
    }
}
