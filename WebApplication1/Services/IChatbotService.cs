using WebApplication1.Models.ViewModels;

namespace WebApplication1.Services;

public interface IChatbotService
{
    /// <param name="canSeePurchasePrices">False for Sales Officers/Viewers: no costs or purchase figures in answers.</param>
    /// <param name="canSeeCustomerLedger">False for Purchase Officers: no customer dues/receivables.</param>
    /// <param name="canSeeSupplierLedger">False for Sales Officers/Viewers: no supplier dues/payables.</param>
    Task<ChatbotAnswer> AskAsync(string question, List<int>? warehouseIds, bool canSeePurchasePrices = true,
        bool canSeeCustomerLedger = true, bool canSeeSupplierLedger = true);
}
