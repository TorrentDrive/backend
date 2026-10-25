using TorrentDrive.Core.DTOs.Storage;

namespace TorrentDrive.Core.Interfaces
{
    public interface IS3StorageService
    {
        Task<StorageProfileResultDto> ConfigureS3StorageAsync(
            int userId,
            string profileName,
            string s3Endpoint,
            string s3AccessKey,
            string s3SecretKey,
            string s3BucketName,
            string s3Region,
            bool setAsDefault);
    }
}
