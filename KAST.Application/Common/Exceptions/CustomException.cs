using System.Net;

namespace KAST.Application.Common.Exceptions
{
    public class CustomException : Exception
    {
        public List<string>? ErrorMessages { get; }

        public HttpStatusCode StatusCode { get; }

        public CustomException(string message, List<string>? errors = default, HttpStatusCode statusCode = HttpStatusCode.InternalServerError)
            : base(message)
        {
            ErrorMessages = errors;
            StatusCode = statusCode;
        }
    }
}