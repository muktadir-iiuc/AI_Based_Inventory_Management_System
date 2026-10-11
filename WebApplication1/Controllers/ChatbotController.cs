using Microsoft.AspNetCore.Mvc;
using WebApplication1.Extensions;
using WebApplication1.Models.ViewModels;
using WebApplication1.Services;
using WebApplication1.Models.Saas;
using WebApplication1.Services.Saas;

namespace WebApplication1.Controllers;

[RequireFeature(Features.AiAssistant)]
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

        var answer = await chatbotService.AskAsync(request.Question, User.GetWarehouseIds(), User.CanSeePurchasePrice(),
            User.CanSeeCustomerLedger(), User.CanSeeSupplierLedger());
        return Json(answer);
    }
}
