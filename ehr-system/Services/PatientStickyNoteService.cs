using Microsoft.EntityFrameworkCore;
using EHR.Data;
using EHR.Models;
using EHR.Models.Generated;

namespace EHR.Services;

public interface IPatientStickyNoteService
{
    Task<List<PatientStickyNoteListDto>> GetStickyNotesAsync(int patientId);
    Task<PatientStickyNoteListDto?> CreateStickyNoteAsync(int patientId, PatientStickyNoteCreateDto dto, int userId, string userName);
    Task<bool> DeleteStickyNoteAsync(int stickyNoteId, int userId);
}

public class PatientStickyNoteService : IPatientStickyNoteService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;

    public PatientStickyNoteService(EhrDbContext context, ITenantProvider tenantProvider)
    {
        _context = context;
        _tenantProvider = tenantProvider;
    }

    public async Task<List<PatientStickyNoteListDto>> GetStickyNotesAsync(int patientId)
    {
        var query = _context.PatientStickyNotes
            .Where(n => n.PatientId == patientId && !n.IsDeleted);

        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(n => n.TenantId == _tenantProvider.TenantId.Value);
        }

        return await query
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new PatientStickyNoteListDto
            {
                PatientStickyNoteId = n.PatientStickyNoteId,
                PatientId = n.PatientId,
                Content = n.Content,
                CreatedByUserId = n.CreatedByUserId,
                CreatedByName = n.CreatedByName,
                CreatedAt = n.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<PatientStickyNoteListDto?> CreateStickyNoteAsync(int patientId, PatientStickyNoteCreateDto dto, int userId, string userName)
    {
        if (string.IsNullOrWhiteSpace(dto.Content))
            return null;

        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue) return null;

        // Verify patient exists and belongs to tenant
        var patient = await _context.Patients
            .FirstOrDefaultAsync(p => p.PatientId == patientId && p.TenantId == tenantId.Value && p.IsDeleted != true);
        if (patient == null) return null;

        var note = new PatientStickyNote
        {
            TenantId = tenantId.Value,
            PatientId = patientId,
            Content = dto.Content.Trim(),
            CreatedByUserId = userId,
            CreatedByName = userName,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };

        _context.PatientStickyNotes.Add(note);
        await _context.SaveChangesAsync();

        return new PatientStickyNoteListDto
        {
            PatientStickyNoteId = note.PatientStickyNoteId,
            PatientId = note.PatientId,
            Content = note.Content,
            CreatedByUserId = note.CreatedByUserId,
            CreatedByName = note.CreatedByName,
            CreatedAt = note.CreatedAt
        };
    }

    public async Task<bool> DeleteStickyNoteAsync(int stickyNoteId, int userId)
    {
        var note = await _context.PatientStickyNotes.FindAsync(stickyNoteId);
        if (note == null || note.IsDeleted) return false;

        if (_tenantProvider.TenantId.HasValue && note.TenantId != _tenantProvider.TenantId.Value)
            return false;

        note.IsDeleted = true;
        note.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }
}
