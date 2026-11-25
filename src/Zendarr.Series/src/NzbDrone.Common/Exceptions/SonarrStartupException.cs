using System;

namespace NzbDrone.Common.Exceptions
{
    public class SonarrStartupException : NzbDroneException
    {
        public SonarrStartupException(string message, params object[] args)
            : base("Zendarr failed to start: " + string.Format(message, args))
        {
        }

        public SonarrStartupException(string message)
            : base("Zendarr failed to start: " + message)
        {
        }

        public SonarrStartupException()
            : base("Zendarr failed to start")
        {
        }

        public SonarrStartupException(Exception innerException, string message, params object[] args)
            : base("Zendarr failed to start: " + string.Format(message, args), innerException)
        {
        }

        public SonarrStartupException(Exception innerException, string message)
            : base("Zendarr failed to start: " + message, innerException)
        {
        }

        public SonarrStartupException(Exception innerException)
            : base("Zendarr failed to start: " + innerException.Message)
        {
        }
    }
}
