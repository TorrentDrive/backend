using TorrentDrive.Core.Entities;
using TorrentDrive.Core.Interfaces;

namespace TorrentDrive.Application.Services.OAuth
{
    public class OAuthService(IUnitOfWork unitOfWork) : IOAuthService
    {
        public async Task<UserOAuthCredential?> GetUserOAuthCredentialByClientId(string clientId, int userId)
        {
            return await unitOfWork.Repository<UserOAuthCredential>().FirstOrDefaultAsync(c => c.UserId == userId && c.ClientId == clientId);

        }
        public async Task Update(UserOAuthCredential credential)
        {

            unitOfWork.Repository<UserOAuthCredential>().Update(credential);
            await unitOfWork.Complete();

        }

        public async Task<UserOAuthCredential> Add(UserOAuthCredential credential)
        {
            unitOfWork.Repository<UserOAuthCredential>().Add(credential);
            await unitOfWork.Complete();

            return credential;
        }

        public async Task<IReadOnlyCollection<UserOAuthCredential>> GetAll(int userId)
        {
            return await unitOfWork.Repository<UserOAuthCredential>().ListAsync(c => c.UserId == userId);



        }

        public Task<UserOAuthCredential?> GetUserOAuthCredentialById(int credId, int userId)
        {

            return unitOfWork.Repository<UserOAuthCredential>().FirstOrDefaultAsync(c => c.UserId == userId && c.Id == credId);

        }
    }
}
