using TorrentDrive.Core.Entities.Jobs;
using TorrentDrive.Core.Entities.Torrents;

namespace TorrentDrive.Core.Entities
{
    public class User : BaseEntity
    {
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;

        /// <summary>
        /// Hashed login password, in the format produced by the password hashing service.
        ///
        /// Null on rows created before passwords moved into the database, and on rows
        /// created by the legacy environment-variable admin path. A null hash means this
        /// account cannot log in on its own — either setup has not run yet, or the account
        /// still depends on ADMIN_EMAIL/ADMIN_PASSWORD.
        /// </summary>
        public string? PasswordHash { get; set; }

        // Navigation properties
        public ICollection<UserStorageProfile> StorageProfiles { get; set; } = [];
        public ICollection<RequestedFile> UploadedTorrentFiles { get; set; } = [];
    }
}
