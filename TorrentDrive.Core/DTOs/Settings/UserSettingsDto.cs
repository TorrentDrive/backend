namespace TorrentDrive.Core.DTOs.Settings
{
    public record UserSettingsDto
    {
        /// <summary>
        /// Delete a job's local download directory once every file has been uploaded.
        /// </summary>
        public bool DeleteAfterUpload { get; init; }
    }

    public record UpdateUserSettingsRequestDto
    {
        public bool DeleteAfterUpload { get; init; }
    }
}
