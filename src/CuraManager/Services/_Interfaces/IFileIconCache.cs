using System.Windows.Media;

namespace CuraManager.Services;

public interface IFileIconCache
{
    ImageSource GetFileIcon(string filePath);
}
