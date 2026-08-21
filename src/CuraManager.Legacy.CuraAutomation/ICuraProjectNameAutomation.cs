using System.Diagnostics;

namespace CuraManager.Legacy.CuraAutomation;

/// <summary>
/// Drives the Cura window through UI automation to set a project name, because Cura
/// exposes no command-line argument for it.
/// </summary>
/// <remarks>
/// Legacy. Windows-only, and liable to break whenever Cura changes its UI tree.
/// Scheduled for removal in 2.0.
/// </remarks>
public interface ICuraProjectNameAutomation
{
    /// <param name="curaProcess">A started Cura process that has reached input idle.</param>
    /// <param name="executableFileName">
    /// File name without extension. <c>Cura</c> selects the 4.x strategy; anything else selects 5.x.
    /// </param>
    /// <param name="projectName">The name to type into Cura.</param>
    void SetProjectName(Process curaProcess, string executableFileName, string projectName);
}
