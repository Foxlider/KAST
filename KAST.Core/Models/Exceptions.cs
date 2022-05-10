#pragma warning disable S3925 // "ISerializable" should be implemented correctly
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

        public KastDataNotFoundException(Type t) : base($"{t.Name} not found.")
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
#pragma warning restore S3925 // "ISerializable" should be implemented correctly