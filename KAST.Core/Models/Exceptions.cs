using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KAST.Core.Models
{
    public class KastLogonFailedException : Exception
    {
        public KastLogonFailedException()
        { }

        public KastLogonFailedException(string message)
            : base(message)
        { }

        public KastLogonFailedException(string message, Exception inner)
            : base(message, inner)
        { }
    }

    public class KastDataNotFoundException : Exception
    {
        public KastDataNotFoundException()
        { }

        public KastDataNotFoundException(string message)
            : base(message)
        { }

        public KastDataNotFoundException(string message, Exception inner)
            : base(message, inner)
        { }
    }

    public class KastDataDuplicateException : Exception
    {
        public KastDataDuplicateException()
        { }

        public KastDataDuplicateException(string message)
            : base(message)
        { }

        public KastDataDuplicateException(string message, Exception inner)
            : base(message, inner)
        { }
    }
}
