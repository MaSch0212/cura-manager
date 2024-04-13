using MaSch.Presentation.Wpf.Common;

namespace CuraManager.Models;

public class CuraManagerGuiSettings
{
    public List<WindowPosition> WindowPositions { get; set; }
    public bool IsMainViewMenuExpanded { get; set; }
    public int PrintFilesPanelWidth { get; set; }

    public CuraManagerGuiSettings()
    {
        WindowPositions = new List<WindowPosition>();
        IsMainViewMenuExpanded = true;
        PrintFilesPanelWidth = 350;
    }
}
