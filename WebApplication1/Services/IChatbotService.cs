using WebApplication1.Models.ViewModels;

namespace WebApplication1.Services;

public interface IChatbotService
{
    Task<ChatbotAnswer> AskAsync(string question, List<int>? warehouseIds);
}
