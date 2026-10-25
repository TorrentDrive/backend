namespace TorrentDrive.GoogleDrive.Worker.Services
{


    public sealed class UploadResult
    {
        public int TotalFiles;
        public int FailedFiles;

        /// <summary>
        /// Last exception thrown while uploading a file. Used to classify the destination's
        /// health so the next attempt can be routed to a different drive.
        /// </summary>
        public Exception? LastError;

        /// <summary>
        /// True when nothing failed.
        ///
        /// Zero files is success, not failure: the caller has already rejected a download
        /// directory with no files in it, so reaching here with none left to process means
        /// every file was uploaded on an earlier attempt and skipped by the resume filter.
        /// Requiring TotalFiles > 0 made that case report "Failed to upload 0 of 0 files",
        /// which retried forever and never let the job reach COMPLETED — so its downloads
        /// were never reclaimed either.
        /// </summary>
        public bool AllFilesUploaded => FailedFiles == 0;
    }


}
