using Nutrir.Core.DTOs;

namespace Nutrir.Core.Interfaces;

public interface ISessionNoteService
{
    Task<SessionNoteDto?> GetByIdAsync(int id);
    Task<SessionNoteDto?> GetByAppointmentIdAsync(int appointmentId);
    Task<List<SessionNoteSummaryDto>> GetByClientAsync(int clientId);
    Task<SessionNoteDto> CreateDraftAsync(int appointmentId, int clientId, string userId);
    Task<bool> UpdateAsync(int id, UpdateSessionNoteDto dto, string userId);
    Task<bool> FinalizeAsync(int id, string userId);
    Task<bool> SoftDeleteAsync(int id, string userId);
    Task<List<SessionNoteSummaryDto>> GetMissingNotesAsync();
}
