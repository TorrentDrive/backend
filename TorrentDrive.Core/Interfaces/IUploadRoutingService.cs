using TorrentDrive.Core.DTOs.Storage;
using TorrentDrive.Core.Entities.Jobs;

namespace TorrentDrive.Core.Interfaces
{
    /// <summary>
    /// Decides which storage profile a job's upload goes to, and moves the job to a
    /// healthy profile when its current destination cannot accept the upload.
    /// </summary>
    public interface IUploadRoutingService
    {
        /// <summary>
        /// Resolves the destination for <paramref name="job"/>. Returns the job's current
        /// profile when that profile is usable; otherwise picks a healthy alternative of
        /// the same provider type, persists the change on the job, and records it on the
        /// timeline. The returned result has no target when nothing usable exists.
        /// </summary>
        /// <param name="requiredBytes">Payload size, used to skip profiles without room.</param>
        Task<StorageRouteResult> ResolveTargetAsync(
            UserJob job,
            long requiredBytes = 0,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Moves the job off <paramref name="job"/>'s current profile after a failure,
        /// excluding every profile already tried. Returns a result with no target when no
        /// alternative is available.
        /// </summary>
        Task<StorageRouteResult> FailoverAsync(
            UserJob job,
            Exception? cause,
            long requiredBytes = 0,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Pins the job to a specific profile chosen by the user. Clears upload progress
        /// tied to the previous destination and re-dispatches the upload when the job is
        /// already past the download phase.
        /// </summary>
        Task<StorageRouteResult> RouteToProfileAsync(
            UserJob job,
            int targetProfileId,
            int userId,
            bool allowFailover,
            CancellationToken cancellationToken = default);

        /// <summary>Profiles the job has already used, most recent last.</summary>
        Task<IReadOnlyList<int>> GetAttemptedProfileIdsAsync(int jobId);

        /// <summary>Clears the attempted-profile history for a job (used on manual retry).</summary>
        Task ClearAttemptHistoryAsync(int jobId);
    }
}
