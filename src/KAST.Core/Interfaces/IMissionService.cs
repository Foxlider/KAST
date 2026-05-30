using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface IMissionService
{
    // Missions
    Task<IReadOnlyList<Mission>> GetMissionsForInstanceAsync(int instanceId, CancellationToken ct = default);
    Task<Mission?> GetMissionByIdAsync(int id, CancellationToken ct = default);
    Task<Mission> UploadMissionAsync(int instanceId, string fileName, Stream pboStream, CancellationToken ct = default);
    Task<Mission> UpdateMissionAsync(Mission mission, CancellationToken ct = default);
    Task DeleteMissionAsync(int id, CancellationToken ct = default);
    Task<Stream?> GetMissionFileStreamAsync(int id, CancellationToken ct = default);

    // Tags
    Task<IReadOnlyList<MissionTag>> GetTagsForInstanceAsync(int instanceId, CancellationToken ct = default);
    Task<MissionTag> CreateTagAsync(int instanceId, string name, CancellationToken ct = default);
    Task DeleteTagAsync(int id, CancellationToken ct = default);
    Task AssignTagAsync(int missionId, int tagId, CancellationToken ct = default);
    Task RemoveTagAsync(int missionId, int tagId, CancellationToken ct = default);

    // Campaigns
    Task<IReadOnlyList<Campaign>> GetCampaignsForInstanceAsync(int instanceId, CancellationToken ct = default);
    Task<Campaign?> GetCampaignByIdAsync(int id, CancellationToken ct = default);
    Task<Campaign> CreateCampaignAsync(Campaign campaign, CancellationToken ct = default);
    Task<Campaign> UpdateCampaignAsync(Campaign campaign, CancellationToken ct = default);
    Task DeleteCampaignAsync(int id, CancellationToken ct = default);
    Task AddMissionToCampaignAsync(int campaignId, int missionId, int orderIndex, CancellationToken ct = default);
    Task RemoveMissionFromCampaignAsync(int campaignId, int missionId, CancellationToken ct = default);
    Task ReorderCampaignMissionsAsync(int campaignId, List<int> orderedMissionIds, CancellationToken ct = default);

    // Sets
    Task<IReadOnlyList<Set>> GetSetsForInstanceAsync(int instanceId, CancellationToken ct = default);
    Task<Set?> GetSetByIdAsync(int id, CancellationToken ct = default);
    Task<Set> CreateSetAsync(Set set, CancellationToken ct = default);
    Task<Set> UpdateSetAsync(Set set, CancellationToken ct = default);
    Task DeleteSetAsync(int id, CancellationToken ct = default);
    Task AddMissionToSetAsync(int setId, int missionId, CancellationToken ct = default);
    Task RemoveMissionFromSetAsync(int setId, int missionId, CancellationToken ct = default);

    // Search
    Task<IReadOnlyList<Mission>> SearchMissionsAsync(int instanceId, string? query, List<int>? tagIds, string? mapName, CancellationToken ct = default);
}
