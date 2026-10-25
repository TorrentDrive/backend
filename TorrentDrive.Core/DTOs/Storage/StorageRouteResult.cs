using TorrentDrive.Core.Entities.Jobs;
using TorrentDrive.Core.Enums;

namespace TorrentDrive.Core.DTOs.Storage
{
    /// <summary>
    /// Outcome of resolving which storage profile an upload should target.
    /// </summary>
    public class StorageRouteResult
    {
        /// <summary>Profile the upload should use. Null when no usable profile exists.</summary>
        public UserStorageProfile? Target { get; set; }

        /// <summary>True when <see cref="Target"/> differs from the job's previous profile.</summary>
        public bool Rerouted { get; set; }

        public int? PreviousProfileId { get; set; }
        public string? PreviousProfileName { get; set; }

        public StorageRouteReason Reason { get; set; } = StorageRouteReason.None;

        /// <summary>Explanation for logs, the job timeline, and the failure message.</summary>
        public string? Message { get; set; }

        public bool HasTarget => Target != null;

        public static StorageRouteResult Unchanged(UserStorageProfile target) =>
            new() { Target = target, Rerouted = false, Reason = StorageRouteReason.None };

        public static StorageRouteResult NoTarget(string message, StorageRouteReason reason) =>
            new() { Target = null, Rerouted = false, Reason = reason, Message = message };
    }
}
