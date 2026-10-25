using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TorrentDrive.Core;
using TorrentDrive.Core.DTOs.Common;
using TorrentDrive.Infrastructure.Data;

namespace TorrentDrive.API.Controllers
{
    /// <summary>
    /// Reports what this build is and what schema it is running against.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous]
    public class VersionController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<VersionController> _logger;

        public VersionController(ApplicationDbContext context, ILogger<VersionController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            var info = new VersionInfo
            {
                Version = BuildInfo.Version,
                BuildSha = BuildInfo.Sha,
                BuildTime = BuildInfo.Time,
            };

            // The database may be unreachable; the version of the running code is
            // still useful, so degrade rather than fail.
            try
            {
                var applied = (await _context.Database.GetAppliedMigrationsAsync(ct)).ToList();
                var known = _context.Database.GetMigrations().ToList();

                info.DatabaseSchema = applied.LastOrDefault();
                info.PendingMigrations = known.Except(applied).Count();
                info.SchemaAhead = applied.Except(known).Any();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read migration state for /api/version");
            }

            return Ok(info);
        }
    }
}
