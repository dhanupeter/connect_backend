using ConnectBackend.Models;

namespace ConnectBackend.Repositories.Interfaces;

public interface IChatRepository
{
    Task<IEnumerable<Conversation>> GetConversationsAsync(long userId);
    Task<IEnumerable<ChatMessage>> GetMessagesAsync(string conversationId, int limit = 50);
    Task<ChatMessage> SaveMessageAsync(ChatMessage message);
    Task<Conversation> CreateGroupAsync(long creatorUserId, CreateGroupDto dto);
    Task<Conversation?> UpdateGroupAsync(string conversationId, UpdateGroupDto dto);
    Task<bool> UpdateMemberRoleAsync(string conversationId, long userId, bool isAdmin);
    Task<bool> RemoveMemberAsync(string conversationId, long userId);
    Task<bool> AddMemberAsync(string conversationId, long userId, bool isAdmin);
    Task<IEnumerable<NearbyUserDto>> GetNearbyUsersAsync(double myLat, double myLng, double radiusKm = 100);
    Task<bool> UpdateUserLocationAsync(long userId, double lat, double lng, bool isDiscoverable);
    Task<bool> SetLocationSharingAsync(long userId, bool isDiscoverable);
    Task<IEnumerable<NearbyUserDto>> GetContactsAsync();
}
