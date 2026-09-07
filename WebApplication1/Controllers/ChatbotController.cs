using Microsoft.AspNetCore.Mvc;
using WebApplication1.Extensions;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;

namespace WebApplication1.Controllers;

public class ChatbotController(IChatbotService chatbotService) : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ask([FromBody] ChatbotAskRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest();
        }

        var answer = await chatbotService.AskAsync(request.Question, User.GetWarehouseId());
        return Json(answer);
    }
}
