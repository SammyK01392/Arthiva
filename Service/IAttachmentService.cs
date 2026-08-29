using Arthiva.Models;

namespace Arthiva.Services;

public interface IAttachmentService
{
    /// <summary>referenceType e.g. "Transaction", "Bill", "EMI", "BorrowLend", "Goal"</summary>
    Task<List<Attachment>> GetByReferenceAsync(string referenceType, int referenceId);

    Task<Attachment?> GetByIdAsync(int id);

    Task<int> AddAsync(Attachment attachment);

    Task<int> SoftDeleteAsync(int id);
}
