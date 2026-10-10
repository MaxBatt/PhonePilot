using QRCoder;
using System.Windows.Media.Imaging;

namespace PhonePilot.Desktop;

internal sealed class QrPairingPanel : StackPanel
{
    private readonly Image _image = new() { HorizontalAlignment = HorizontalAlignment.Center, Stretch = Stretch.None, Margin = new Thickness(0, 12, 0, 12) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) };
    private readonly Button _restart = new() { Content = "Neuen QR-Code erzeugen", HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Func<QrPairingSession, CancellationToken, Task> _pair;
    private readonly Func<CancellationToken, Task> _refresh;
    private readonly CancellationToken _lifetime;
    private CancellationTokenSource? _attempt;
    private QrPairingSession? _session;

    public QrPairingPanel(Func<QrPairingSession, CancellationToken, Task> pair, Func<CancellationToken, Task> refresh, CancellationToken lifetime, bool startAutomatically = true)
    {
        _pair = pair; _refresh = refresh; _lifetime = lifetime;
        Margin = new Thickness(0, 18, 0, 0);
        Children.Add(new TextBlock { Text = "Am Smartphone: Entwickleroptionen → WLAN-Debugging → Gerät mit QR-Code koppeln. Scanne diesen Code. Beide Geräte müssen im selben erreichbaren WLAN sein.", TextWrapping = TextWrapping.Wrap });
        Children.Add(_image); Children.Add(_status); Children.Add(_restart);
        _restart.Click += (_, _) => { Stop(); Start(); };
        if (startAutomatically) { Loaded += (_, _) => Start(); Unloaded += (_, _) => Stop(); }
    }

    internal void Prepare()
    {
        _session = new QrPairingSession();
        byte[] png = PngByteQRCodeHelper.GetQRCode(_session.Payload, QRCodeGenerator.ECCLevel.Q, 6);
        using var stream = new MemoryStream(png);
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        _image.Source = bitmap;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        _status.Text = "Warte auf Scan … Der QR-Code gilt bis zu 3 Minuten.";
    }

    private async void Start()
    {
        if (_attempt is not null || _lifetime.IsCancellationRequested) return;
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        _attempt = attempt;
        try
        {
            Prepare();
            await _pair(_session!, attempt.Token);
            if (attempt.IsCancellationRequested) return;
            _image.Source = null; _session = null;
            _status.Text = "Kopplung erfolgreich. Das Gerät wird gesucht; anschließend kannst du es verbinden oder starten.";
            await _refresh(attempt.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!attempt.IsCancellationRequested) { _image.Source = null; _session = null; _status.Text = ex.Message; }
        }
        finally { if (ReferenceEquals(_attempt, attempt)) _attempt = null; }
    }

    private void Stop()
    {
        _attempt?.Cancel(); _attempt = null; _session = null; _image.Source = null;
    }
}
