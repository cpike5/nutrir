using Nutrir.Core.DTOs;

namespace Nutrir.Core.Interfaces;

public interface IClientHealthProfileService
{
    // --- Allergies ---
    Task<ClientAllergyDto> CreateAllergyAsync(CreateClientAllergyDto dto, string userId);
    Task<ClientAllergyDto?> GetAllergyByIdAsync(int id);
    Task<List<ClientAllergyDto>> GetAllergiesByClientIdAsync(int clientId);
    Task<bool> UpdateAllergyAsync(int id, UpdateClientAllergyDto dto, string userId);
    Task<bool> DeleteAllergyAsync(int id, string userId);

    // --- Medications ---
    Task<ClientMedicationDto> CreateMedicationAsync(CreateClientMedicationDto dto, string userId);
    Task<ClientMedicationDto?> GetMedicationByIdAsync(int id);
    Task<List<ClientMedicationDto>> GetMedicationsByClientIdAsync(int clientId);
    Task<bool> UpdateMedicationAsync(int id, UpdateClientMedicationDto dto, string userId);
    Task<bool> DeleteMedicationAsync(int id, string userId);

    // --- Conditions ---
    Task<ClientConditionDto> CreateConditionAsync(CreateClientConditionDto dto, string userId);
    Task<ClientConditionDto?> GetConditionByIdAsync(int id);
    Task<List<ClientConditionDto>> GetConditionsByClientIdAsync(int clientId);
    Task<bool> UpdateConditionAsync(int id, UpdateClientConditionDto dto, string userId);
    Task<bool> DeleteConditionAsync(int id, string userId);

    // --- Dietary Restrictions ---
    Task<ClientDietaryRestrictionDto> CreateDietaryRestrictionAsync(CreateClientDietaryRestrictionDto dto, string userId);
    Task<ClientDietaryRestrictionDto?> GetDietaryRestrictionByIdAsync(int id);
    Task<List<ClientDietaryRestrictionDto>> GetDietaryRestrictionsByClientIdAsync(int clientId);
    Task<bool> UpdateDietaryRestrictionAsync(int id, UpdateClientDietaryRestrictionDto dto, string userId);
    Task<bool> DeleteDietaryRestrictionAsync(int id, string userId);

    // --- Bulk read ---
    Task<ClientHealthProfileSummaryDto> GetHealthProfileSummaryAsync(int clientId);
}
