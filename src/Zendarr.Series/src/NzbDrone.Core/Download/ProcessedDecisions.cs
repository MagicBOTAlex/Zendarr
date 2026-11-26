using System.Collections.Generic;
using NzbDrone.Core.DecisionEngine;

namespace NzbDrone.Core.Download
{
    public class ProcessedDecisions
    {
        public List<DownloadDecision> Grabbed { get; set; }
        public List<DownloadDecision> Pending { get; set; }
        public List<DownloadDecision> Rejected { get; set; }
        public List<DownloadDecision> Failed { get; set; }
        public List<DownloadDecision> Skipped { get; set; }

        public ProcessedDecisions(List<DownloadDecision> grabbed,
                                  List<DownloadDecision> pending,
                                  List<DownloadDecision> rejected,
                                  List<DownloadDecision> failed = null,
                                  List<DownloadDecision> skipped = null)
        {
            Grabbed = grabbed;
            Pending = pending;
            Rejected = rejected;
            Failed = failed ?? new List<DownloadDecision>();
            Skipped = skipped ?? new List<DownloadDecision>();
        }
    }
}
