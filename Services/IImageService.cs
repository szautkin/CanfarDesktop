using CanfarDesktop.Models;

namespace CanfarDesktop.Services;

public interface IImageService
{
    Task<List<RawImage>> GetImagesAsync();
    Task<SessionContext?> GetContextAsync();
    Task<List<string>> GetRepositoriesAsync();
}
