using System.Collections.Generic;
using NzbDrone.Core.DecisionEngine;

namespace NzbDrone.Core.Download
{
    /// <summary>
    /// Outcome of a decision processing pass. Grabbed, Pending, Rejected, Failed and Skipped
    /// are mutually exclusive: every processed decision appears in at most one of them.
    /// Decisions that failed to reach a download client are listed in <see cref="Failed"/>
    /// only; the subset queued for a later retry is additionally recorded in <see cref="Retry"/>.
    /// </summary>
    public class ProcessedDecisions
    {
        public List<DownloadDecision> Grabbed { get; set; }
        public List<DownloadDecision> Pending { get; set; }
        public List<DownloadDecision> Rejected { get; set; }
        public List<DownloadDecision> Failed { get; set; }
        public List<DownloadDecision> Skipped { get; set; }

        /// <summary>
        /// Decisions that are in <see cref="Failed"/> and were also queued with the pending
        /// release service for a later retry. This is not a separate outcome: it overlaps
        /// <see cref="Failed"/> and never overlaps <see cref="Pending"/>.
        /// </summary>
        public List<DownloadDecision> Retry { get; set; }

        public ProcessedDecisions(List<DownloadDecision> grabbed,
                                  List<DownloadDecision> pending,
                                  List<DownloadDecision> rejected,
                                  List<DownloadDecision> failed = null,
                                  List<DownloadDecision> skipped = null,
                                  List<DownloadDecision> retry = null)
        {
            Grabbed = grabbed;
            Pending = pending;
            Rejected = rejected;
            Failed = failed ?? new List<DownloadDecision>();
            Skipped = skipped ?? new List<DownloadDecision>();
            Retry = retry ?? new List<DownloadDecision>();
        }
    }
}
