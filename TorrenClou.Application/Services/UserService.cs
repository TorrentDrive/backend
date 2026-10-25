using TorrenClou.Core.Entities;
using TorrenClou.Core.Interfaces;

namespace TorrenClou.Application.Services
{
    public class UserService(IUnitOfWork unitOfWork) : IUserService
    {
        public async Task<bool> UserExistsAsync(string email)
        {
            var user = await unitOfWork.Repository<User>().FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
            return user != null;
        }
        public async Task<User> CreateUser(string email, string name)
        {
            var user = new User
            {
                Email = email,
                FullName = name
            };
            unitOfWork.Repository<User>().Add(user);
            await unitOfWork.Complete();
            return user;

        }
        public async Task<User?> GetUserByEmailAsync(string email)
        {
            return await unitOfWork.Repository<User>().FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
        }

        public async Task<User?> GetUserByIdAsync(int userId)
        {
            return await unitOfWork.Repository<User>().GetByIdAsync(userId);
        }

        public async Task SetPasswordHashAsync(User user, string passwordHash)
        {
            user.PasswordHash = passwordHash;
            await unitOfWork.Complete();
        }
    }
}
