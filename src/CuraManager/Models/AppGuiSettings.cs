using MaSch.Presentation.Wpf.Common;

namespace CuraManager.Models;

public class AppGuiSettings
{
    public List<WindowPosition> WindowPositions { get; set; }
    public bool IsMainViewMenuExpanded { get; set; }
    public int PrintFilesPanelWidth { get; set; }

    public AppGuiSettings()
    {
        WindowPositions = new List<WindowPosition>();
        IsMainViewMenuExpanded = true;
        PrintFilesPanelWidth = 350;
    }
}
