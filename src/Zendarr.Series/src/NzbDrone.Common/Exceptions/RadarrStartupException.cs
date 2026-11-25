using System;

namespace NzbDrone.Common.Exceptions
{
    public class RadarrStartupException : NzbDroneException
    {
        public RadarrStartupException(string message, params object[] args)
            : base("Zendarr failed to start: " + string.Format(message, args))
        {
        }

        public RadarrStartupException(string message)
            : base("Zendarr failed to start: " + message)
        {
        }

        public RadarrStartupException()
            : base("Zendarr failed to start")
        {
        }

        public RadarrStartupException(Exception innerException, string message, params object[] args)
            : base("Zendarr failed to start: " + string.Format(message, args), innerException)
        {
        }

        public RadarrStartupException(Exception innerException, string message)
            : base("Zendarr failed to start: " + message, innerException)
        {
        }

        public RadarrStartupException(Exception innerException)
            : base("Zendarr failed to start: " + innerException.Message)
        {
        }
    }
}
