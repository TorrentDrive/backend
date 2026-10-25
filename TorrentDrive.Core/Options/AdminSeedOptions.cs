using TorrentDrive.Core.Configuration;

namespace TorrentDrive.Core.Options
{
    /// <summary>
    /// The pre-wizard way of creating the single account.
    ///
    /// Setting these disables the first-run wizard, deliberately: an instance
    /// whose owner already has a way in must not offer itself to whoever
    /// reaches it first.
    /// </summary>
    public class AdminSeedOptions
    {
        [ConfigDoc("ADMIN_EMAIL",
            Description = "Legacy admin login. Setting it disables the first-run wizard.",
            Default = "unset; use the wizard instead",
            Deprecated = "Only for installs that predate the first-run wizard. New installs create the account in the browser.")]
        public string? Email { get; set; }

        [ConfigDoc("ADMIN_PASSWORD",
            Description = "Legacy admin password. Converted to a stored hash on first successful login.",
            Default = "unset; use the wizard instead",
            Secret = true,
            Deprecated = "Only for installs that predate the first-run wizard.")]
        public string? Password { get; set; }

        [ConfigDoc("ADMIN_NAME",
            Description = "Display name for the legacy admin account.",
            Default = "TorrentDrive Admin",
            Deprecated = "Only for installs that predate the first-run wizard.")]
        public string? Name { get; set; }
    }
}
