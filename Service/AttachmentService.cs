using Arthiva.Models;

namespace Arthiva.Services;

public class AttachmentService : IAttachmentService
{
    private readonly IGenericRepository<Attachment> _repo;

    public AttachmentService(IGenericRepository<Attachment> repo)
    {
        _repo = repo;
    }

    public async Task<List<Attachment>> GetByReferenceAsync(string referenceType, int referenceId)
    {
        var attachments = await _repo.FindAsync(a =>
            !a.IsDeleted && a.ReferenceType == referenceType && a.ReferenceId == referenceId);
        return attachments.OrderByDescending(a => a.CreatedAt).ToList();
    }

    public Task<Attachment?> GetByIdAsync(int id)
        => _repo.GetByIdAsync(id);

    public Task<int> AddAsync(Attachment attachment)
    {
        attachment.CreatedAt = DateTime.UtcNow;
        attachment.UpdatedAt = DateTime.UtcNow;
        return _repo.AddAsync(attachment);
    }

    public async Task<int> SoftDeleteAsync(int id)
    {
        var attachment = await _repo.GetByIdAsync(id);
        if (attachment is null) return 0;

        attachment.IsDeleted = true;
        attachment.UpdatedAt = DateTime.UtcNow;
        return await _repo.UpdateAsync(attachment);
    }
}
