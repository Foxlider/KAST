using System.Diagnostics;
using System.Reflection;

namespace KAST.Core.Models
{
    public static class Utilities
    {
        private static readonly string[] Alist = { "Arma", "Amazing", "Advanced" };
        private static readonly string[] Tlist = { "Tool", "Thing" };

        public static string NameGenerator()
        {
            var _r = new Random();

            return $"Keelah's {Alist[_r.Next(0, Alist.Length)]} Server {Tlist[_r.Next(0, Tlist.Length)]}";
        }

        public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            DateTime dtDateTime = new(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dtDateTime;
        }

        public static string GetAppDataPath()
        {
            var appdata = Environment.SpecialFolder.LocalApplicationData;
            var assembly = Assembly.GetExecutingAssembly().GetName();
            var versionInfo = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            var path = Environment.GetFolderPath(appdata);
            return Path.Combine(path, versionInfo.CompanyName ?? throw new InvalidOperationException(), assembly.Name ?? throw new InvalidOperationException());
        }


        /// <summary>
        /// Wraps sharing violations that could occur on a file IO operation.
        /// </summary>
        /// <param name="action">The action to execute. May not be null.</param>
        public static void WrapSharingViolations(WrapSharingViolationsCallback action)
        {
            WrapSharingViolations(action, null, 10, 100);
        }

        /// <summary>
        /// Wraps sharing violations that could occur on a file IO operation.
        /// </summary>
        /// <param name="action">The action to execute. May not be null.</param>
        /// <param name="exceptionsCallback">The exceptions callback. May be null.</param>
        /// <param name="retryCount">The retry count.</param>
        /// <param name="waitTime">The wait time in milliseconds.</param>
        public static void WrapSharingViolations(WrapSharingViolationsCallback action, WrapSharingViolationsExceptionsCallback exceptionsCallback = null, int retryCount = 10, int waitTime = 100)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            for (int i = 0; i < retryCount; i++)
            {
                try
                {
                    action();
                    return;
                }
                catch (IOException ioe)
                {
                    if ((IsSharingViolation(ioe)) && (i < (retryCount - 1)))
                    {
                        bool wait = true;
                        if (exceptionsCallback != null)
                        {
                            wait = exceptionsCallback(ioe, i, retryCount, waitTime);
                        }
                        if (wait)
                        {
                            Thread.Sleep(waitTime);
                        }
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Defines a sharing violation wrapper delegate.
        /// </summary>
        public delegate void WrapSharingViolationsCallback();

        /// <summary>
        /// Defines a sharing violation wrapper delegate for handling exception.
        /// </summary>
        public delegate bool WrapSharingViolationsExceptionsCallback(IOException ioe, int retry, int retryCount, int waitTime);

        /// <summary>
        /// Determines whether the specified exception is a sharing violation exception.
        /// </summary>
        /// <param name="exception">The exception. May not be null.</param>
        /// <returns>
        ///     <c>true</c> if the specified exception is a sharing violation exception; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsSharingViolation(IOException exception)
        {
            if (exception == null)
                throw new ArgumentNullException(nameof(exception));

            int hr = GetHResult(exception, 0);
            return (hr == -2147024864); // 0x80070020 ERROR_SHARING_VIOLATION

        }

        /// <summary>
        /// Gets the HRESULT of the specified exception.
        /// </summary>
        /// <param name="exception">The exception to test. May not be null.</param>
        /// <param name="defaultValue">The default value in case of an error.</param>
        /// <returns>The HRESULT value.</returns>
        public static int GetHResult(IOException exception, int defaultValue)
        {
            if (exception == null)
                throw new ArgumentNullException(nameof(exception));

            try
            {
                const string name = "HResult";
                PropertyInfo pi = exception.GetType().GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance); // CLR2
                if (pi == null)
                {
                    pi = exception.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance); // CLR4
                }
                if (pi != null)
                    return (int)(pi.GetValue(exception, null) ?? -1);
            }
            catch (Exception ex)
            { Console.WriteLine(ex.Message); }
            return defaultValue;
        }
    }
}
