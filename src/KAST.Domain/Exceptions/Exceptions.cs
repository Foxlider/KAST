using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KAST.Core.Exceptions
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
}
