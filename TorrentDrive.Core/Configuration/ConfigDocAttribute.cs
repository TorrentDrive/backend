using System;

namespace TorrentDrive.Core.Configuration
{
    /// <summary>
    /// Describes one configuration value well enough to document it without
    /// anybody writing the documentation.
    ///
    /// The configuration reference used to be maintained by hand in five places
    /// — two READMEs, the deploy docs, the website, and .env.example — and no
    /// two agreed. They disagreed about which variables existed, which were
    /// required, and what the defaults were. Annotating the option properties
    /// means the reference is derived from the same declaration the application
    /// validates against, so it cannot drift from the code.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class ConfigDocAttribute : Attribute
    {
        /// <summary>
        /// The name an operator actually sets, e.g. "JWT_SECRET".
        ///
        /// This is not the same as the configuration key. entrypoint.sh
        /// translates friendly names into the Section__Key form .NET binds to,
        /// so JWT_SECRET becomes Jwt__Key. Documenting the .NET key would be
        /// documenting something nobody types.
        /// </summary>
        public string EnvName { get; }

        /// <summary>One sentence, in the second person, about what it does.</summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>
        /// What happens when it is unset, in words rather than a literal — some
        /// defaults are generated, derived from the request, or read from the
        /// database rather than being a constant.
        /// </summary>
        public string Default { get; init; } = string.Empty;

        /// <summary>
        /// True for anything whose value must never be printed, logged, or
        /// echoed into a support thread. The schema exporter marks these so the
        /// documentation can too.
        /// </summary>
        public bool Secret { get; init; }

        /// <summary>
        /// Set when a value exists only for installs that predate a newer
        /// mechanism. Carries the reason, which is the part people need.
        /// </summary>
        public string Deprecated { get; init; } = string.Empty;

        /// <summary>Version this was introduced in, when that is worth saying.</summary>
        public string Since { get; init; } = string.Empty;

        public ConfigDocAttribute(string envName)
        {
            EnvName = envName;
        }
    }
}
