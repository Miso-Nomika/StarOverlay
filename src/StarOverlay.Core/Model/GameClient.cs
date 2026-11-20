namespace StarOverlay.Core.Model;

public sealed record GameClient(Guid Id, string DisplayName, string InstallPath, DateTimeOffset AddedAt);
