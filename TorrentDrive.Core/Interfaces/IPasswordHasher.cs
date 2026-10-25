namespace TorrentDrive.Core.Interfaces
{
    /// <summary>
    /// Hashes and verifies login passwords. Kept behind an interface so Core stays free of
    /// the ASP.NET Identity dependency the implementation uses, and so the algorithm can be
    /// replaced without touching every caller.
    /// </summary>
    public interface IPasswordHasher
    {
        string Hash(string password);

        /// <summary>
        /// Constant-time verification. Returns false for a null or empty stored hash rather
        /// than throwing, so callers can treat "no password set" as a failed login.
        /// </summary>
        bool Verify(string? hash, string password);
    }
}
