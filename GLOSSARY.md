# TorrentDrive

TorrentDrive is a self-hosted cloud torrent client that downloads torrents to staging storage and transfers the selected files into a user's cloud storage destinations.

## Language

### Transfers & Torrents

**Torrent**:
The parsed metadata of a .torrent file or magnet link, identified by its infohash and containing the manifest of available files and their sizes.
_Avoid_: RequestedFile, RequestFile, torrent file

**Job**:
A single transfer request initiated by a user to download a Torrent (or a selected subset of its files) and upload the resulting files to a Storage Profile.
_Avoid_: UserJob, task, transfer, download

**Download Phase**:
The first stage of a Job's lifecycle, during which peer-to-peer pieces of the Torrent are fetched onto the Staging Volume.
_Avoid_: Stage 1, torrenting phase, leeching phase

**Upload Phase**:
The second stage of a Job's lifecycle, during which files on the Staging Volume are transferred to the target Storage Profile.
_Avoid_: Stage 2, cloud sync, sync phase

**Stale Job**:
A Job in an active Download Phase or Upload Phase whose worker heartbeat has not been refreshed within the staleness threshold, indicating a crashed or interrupted worker that requires automatic recovery.
_Avoid_: stuck job, orphan job, zombie job

**Force Start**:
An explicit user action that immediately dispatches a queued or retry-delayed Job for execution, bypassing normal queue order or retry backoff.
_Avoid_: manual trigger, immediate retry, jump queue

### Cloud Storage & Routing

**Storage Provider**:
A supported cloud storage platform type (such as Google Drive or S3-compatible object storage) to which a Job can upload files.
_Avoid_: provider type, cloud type, backend

**Storage Profile**:
A user-configured connection to a specific account, drive, or bucket on a Storage Provider, including its credentials, quota state, and health status.
_Avoid_: UserStorageProfile, drive, connected account, bucket, destination

**OAuth Credential**:
A reusable OAuth client application registration (client ID, client secret, and redirect URI) owned by a user that can authorize one or more Google Drive Storage Profiles.
_Avoid_: UserOAuthCredential, OAuth app, client credentials

**Failover**:
The automatic reassignment of a Job from an unhealthy or quota-exhausted Storage Profile to another healthy Storage Profile of the same Storage Provider.
_Avoid_: auto-reroute, fallback, profile switch

**Pinning**:
A user's explicit instruction to lock a Job to a specific Storage Profile, disabling Failover so the Job fails rather than switching destinations if that profile becomes unusable.
_Avoid_: hard routing, profile lock

### Local Storage & Maintenance

**Staging Volume**:
The local disk storage where a Job's downloaded files are held during the Download Phase and read from during the Upload Phase.
_Avoid_: block storage, downloads volume, temp directory

**Purge**:
The maintenance operation that deletes local directories on the Staging Volume belonging to completed or cancelled Jobs to reclaim disk space.
_Avoid_: cleanup, prune, garbage collection

### Instance Administration

**Instance Claiming**:
The one-time, irreversible operation on an unconfigured self-hosted deployment that creates the initial administrator account and permanently closes anonymous setup.
_Avoid_: first-run wizard, initial registration, admin bootstrap