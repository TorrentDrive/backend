namespace TorrentDrive.Core.Entities
{
    /// <summary>
    /// Per-user preferences edited from the Settings tab. Created on first read, so a
    /// user that has never opened Settings still gets the documented defaults.
    /// </summary>
    public class UserSettings : BaseEntity
    {
        public int UserId { get; set; }
        public User User { get; set; } = null!;

        /// <summary>
        /// Delete a job's local download directory once every file has been uploaded.
        /// Turning this off keeps the files on the downloads volume, which then only
        /// the Purge action reclaims.
        /// </summary>
        public bool DeleteAfterUpload { get; set; } = true;
    }
}
