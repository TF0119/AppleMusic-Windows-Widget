using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using Windows.Storage.Streams;

namespace AppleMusicWidget.Artwork;

/// <summary>
/// Decodes a GSMTC thumbnail to a frozen BitmapSource at widget display size
/// (callers pass 128 = 2x the 64px display for HiDPI). No caching — PLAN §6.2
/// keeps exactly one current artwork in the ViewModel.
/// </summary>
public static class ArtworkProvider
{
    public static async Task<BitmapSource?> LoadAsync(IRandomAccessStreamReference? thumb, int decodePixelWidth)
    {
        if (thumb is null) return null;
        try
        {
            using var stream = await thumb.OpenReadAsync();
            using var s = stream.AsStreamForRead(); // OnLoad reads it fully during EndInit
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = decodePixelWidth;
            bmp.StreamSource = s;
            bmp.EndInit();
            bmp.Freeze(); // safe to hand to the UI thread
            return bmp;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArtworkProvider] decode failed: {ex.Message}");
            return null;
        }
    }
}
