namespace WebApplication1.Models.ViewModels;

public class ChatbotAnswer
{
    public string Text { get; set; } = string.Empty;
    public List<string> Lines { get; set; } = [];
}

public class ChatbotAskRequest
{
    public string Question { get; set; } = string.Empty;
}
