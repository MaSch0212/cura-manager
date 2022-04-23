using CuraManager.Models;

namespace CuraManager.Services;

public interface ICuraService
{
    Task<bool> CreateCuraProject(PrintElement element);
    void OpenCura(PrintElement element, string printName, IEnumerable<string> modelsToAdd);
    void OpenCuraProject(string fileName);
    bool AreCuraPathsCorrect(CuraManagerSettings settings);
}
