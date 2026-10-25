using System.ComponentModel.DataAnnotations;

using TorrentDrive.Core.Configuration;

namespace TorrentDrive.Core.Options
{
    /// <summary>Bound from the "Jwt" section. Signs and validates API tokens.</summary>
    public class JwtOptions
    {
        public const string SectionName = "Jwt";

        /// <summary>
        /// The signing key. Nothing validated this before: a missing key started
        /// the application happily and threw on the first login attempt, which
        /// is the worst possible place to discover it.
        /// </summary>
        [Required(AllowEmptyStrings = false, ErrorMessage = "A JWT signing key is required.")]
        [MinLength(32, ErrorMessage = "The JWT signing key must be at least 32 characters.")]
        [ConfigDoc("JWT_SECRET",
            Description = "Key used to sign API tokens. Rotating it invalidates every session.",
            Default = "generated on first run and kept on the postgres volume",
            Secret = true)]
        public string Key { get; set; } = string.Empty;

        [ConfigDoc("JWT_ISSUER",
            Description = "Issuer claim written into tokens and checked on validation.",
            Default = "TorrentDrive_API")]
        public string Issuer { get; set; } = "TorrentDrive_API";

        [ConfigDoc("JWT_AUDIENCE",
            Description = "Audience claim written into tokens and checked on validation.",
            Default = "TorrentDrive_Client")]
        public string Audience { get; set; } = "TorrentDrive_Client";
    }
}
