namespace TorrentDrive.Core.DTOs.OAuth
{
    public class SavedCredentialsDto
    {
        public int CredentialId { get; set; }
        public string CredentialName { get; set; } = string.Empty;
    }
}