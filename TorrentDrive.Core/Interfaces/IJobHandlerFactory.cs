using TorrentDrive.Core.Enums;

namespace TorrentDrive.Core.Interfaces
{
    /// <summary>
    /// Factory for resolving job handlers based on provider type and job type.
    /// Enables decoupled, extensible job processing without hardcoded dependencies.
    /// </summary>
    public interface IJobHandlerFactory
    {
        /// <summary>
        /// Gets the storage provider handler for the specified provider type.
        /// Returns null if no handler is registered for the provider type.
        /// </summary>
        IStorageProviderHandler? GetStorageProviderHandler(StorageProviderType providerType);

        /// <summary>
        /// Gets the job type handler for the specified job type.
        /// Returns null if no handler is registered for the job type.
        /// </summary>
        IJobTypeHandler? GetJobTypeHandler(JobType jobType);

        /// <summary>
        /// Gets the cancellation handler for the specified job type.
        /// Returns null if no handler is registered for the job type.
        /// </summary>
        IJobCancellationHandler? GetCancellationHandler(JobType jobType);

        /// <summary>
        /// Gets all registered storage provider handlers
        /// </summary>
        IEnumerable<IStorageProviderHandler> GetAllStorageProviderHandlers();

        /// <summary>
        /// Gets all registered job type handlers
        /// </summary>
        IEnumerable<IJobTypeHandler> GetAllJobTypeHandlers();

        /// <summary>
        /// Gets all registered cancellation handlers
        /// </summary>
        IEnumerable<IJobCancellationHandler> GetAllCancellationHandlers();
    }
}


