using System.Linq;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    public class UpdateCleanTitleForSeries : IHousekeepingTask
    {
        private readonly ISeriesRepository _seriesRepository;

        public UpdateCleanTitleForSeries(ISeriesRepository seriesRepository)
        {
            _seriesRepository = seriesRepository;
        }

        public void Clean()
        {
            var series = _seriesRepository.All().ToList();

            series.ForEach(s =>
            {
                var cleanTitle = s.Title.CleanSeriesTitle();
                if (s.CleanTitle != cleanTitle)
                {
                    s.CleanTitle = cleanTitle;

                    // Only write the field we changed. A whole-row update from this
                    // housekeeping snapshot could revert concurrent status changes
                    // (e.g. a Deleted marking) written after the load above.
                    _seriesRepository.SetFields(s, x => x.CleanTitle);
                }
            });
        }
    }
}
