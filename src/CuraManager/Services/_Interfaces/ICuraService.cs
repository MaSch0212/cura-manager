using System.Collections.Generic;
using System.Threading.Tasks;
using CuraManager.Models;

namespace CuraManager.Services
{
    public interface ICuraService
    {
        Task<bool> CreateCuraProject(PrintElement element);
        void OpenCura(PrintElement element, string printName, IEnumerable<string> modelsToAdd);
        void OpenCuraProject(string fileName);
    }
}
