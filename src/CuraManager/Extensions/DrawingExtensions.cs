using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CuraManager.Extensions;

public static class DrawingExtensions
{
    public static ImageSource ToImageSource(this Icon icon)
    {
        return Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            new Int32Rect(0, 0, icon.Size.Width, icon.Size.Height),
            BitmapSizeOptions.FromEmptyOptions()
        );
    }
}
