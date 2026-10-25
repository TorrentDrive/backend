namespace TorrentDrive.Core.DTOs.Auth
{
    /// <summary>
    /// Whether this instance still needs its first-run setup. Deliberately says nothing
    /// else: the endpoint is anonymous, so it must not leak whether an account exists or
    /// what it is called.
    /// </summary>
    public record SetupStatusDto
    {
        public bool NeedsSetup { get; init; }
    }

    public record CreateAdminRequestDto
    {
        public string Email { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
    }

    public record ChangePasswordRequestDto
    {
        public string CurrentPassword { get; init; } = string.Empty;
        public string NewPassword { get; init; } = string.Empty;
    }
}
