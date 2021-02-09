using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MaSch.Presentation.Wpf;
using MaSch.Presentation.Wpf.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CuraManager.Models
{
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
}
