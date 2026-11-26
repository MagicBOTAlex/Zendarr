using System;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    public class TrackedDownload
    {
        public int DownloadClient { get; set; }
        public DownloadClientItem DownloadItem { get; set; }
        public DownloadClientItem ImportItem { get; set; }
        private TrackedDownloadState _state;

        public TrackedDownloadState State
        {
            get => _state;
            set
            {
                if (!TransitionTo(value))
                {
                    throw new InvalidOperationException($"Cannot transition tracked download from {_state} to {value}");
                }
            }
        }

        public TrackedDownloadStatus Status { get; private set; }
        public RemoteEpisode RemoteEpisode { get; set; }
        public RemoteMovie RemoteMovie { get; set; }
        public TrackedDownloadStatusMessage[] StatusMessages { get; private set; }
        public DownloadProtocol Protocol { get; set; }
        public string Indexer { get; set; }
        public DateTime? Added { get; set; }
        public bool IsTrackable { get; set; }
        public bool HasNotifiedManualInteractionRequired { get; set; }

        // In-memory count of consecutive failed import attempts, used to stop the automatic
        // import retry loop for downloads that cannot be imported (e.g. a destination file that
        // already exists). Reset on a successful import and when the app restarts.
        public int ImportAttempts { get; set; }

        // True when the import failed because a media file could not be read/probed (i.e. it is
        // corrupt). The queue uses it to offer a "delete and redownload" action.
        public bool CorruptFileDetected { get; set; }

        public TrackedDownload()
        {
            StatusMessages = Array.Empty<TrackedDownloadStatusMessage>();
        }

        // Terminal downloads are replaced with a new tracked object on a subsequent grab.
        // Other transitions include retry, manual import, and restoration from history.
        // Imported is the one terminal state that may still be written over another terminal
        // state: importing a file is a fact about the filesystem, so a download that was marked
        // Failed/Ignored and then imported by hand must record that it is now imported.
        public bool CanTransitionTo(TrackedDownloadState state)
        {
            if (!Enum.IsDefined(typeof(TrackedDownloadState), state))
            {
                return false;
            }

            if (_state == state || !IsTerminal(_state))
            {
                return true;
            }

            return state == TrackedDownloadState.Imported;
        }

        public bool TransitionTo(TrackedDownloadState state)
        {
            if (!CanTransitionTo(state))
            {
                return false;
            }

            _state = state;

            if (IsTerminal(state))
            {
                Status = TrackedDownloadStatus.Ok;
                StatusMessages = Array.Empty<TrackedDownloadStatusMessage>();
            }

            return true;
        }

        // A reused tracked object has not been re-evaluated, so it must not carry the Warning
        // (and its messages) produced by an earlier evaluation into a later stage. Status is
        // derived from State alone here: only FailedPending is an error, everything else is Ok.
        // A warning is transient and is set again by Warn() within the current evaluation.
        public void ResetStatusFromState()
        {
            StatusMessages = Array.Empty<TrackedDownloadStatusMessage>();
            Status = _state == TrackedDownloadState.FailedPending
                ? TrackedDownloadStatus.Error
                : TrackedDownloadStatus.Ok;
        }

        private static bool IsTerminal(TrackedDownloadState state)
        {
            return state is TrackedDownloadState.Imported or TrackedDownloadState.Failed or TrackedDownloadState.Ignored;
        }

        // The media subject is single-valued: a download is either a series download or a movie
        // download, never both. Assigning one clears the other so the two parallel queue services
        // can never emit a row for the same download twice.
        public void SetEpisode(RemoteEpisode remoteEpisode)
        {
            RemoteEpisode = remoteEpisode;

            if (remoteEpisode != null)
            {
                RemoteMovie = null;
            }
        }

        public void SetMovie(RemoteMovie remoteMovie)
        {
            RemoteMovie = remoteMovie;

            if (remoteMovie != null)
            {
                RemoteEpisode = null;
            }
        }

        public void Warn(string message, params object[] args)
        {
            var statusMessage = string.Format(message, args);
            Warn(new TrackedDownloadStatusMessage(DownloadItem.Title, statusMessage));
        }

        public void Warn(params TrackedDownloadStatusMessage[] statusMessages)
        {
            if (IsTerminal(State))
            {
                return;
            }

            Status = TrackedDownloadStatus.Warning;
            StatusMessages = statusMessages;
        }

        public void Fail()
        {
            if (!CanTransitionTo(TrackedDownloadState.FailedPending))
            {
                return;
            }

            Status = TrackedDownloadStatus.Error;
            State = TrackedDownloadState.FailedPending;

            // Set CanBeRemoved to allow the failed item to be removed from the client
            DownloadItem.CanBeRemoved = true;
        }
    }

    public enum TrackedDownloadState
    {
        Downloading,
        ImportBlocked,
        ImportPending,
        Importing,
        Imported,
        FailedPending,
        Failed,
        Ignored
    }

    public enum TrackedDownloadStatus
    {
        Ok,
        Warning,
        Error
    }
}
