using MudBlazor;

namespace KAST.Server.Components.Common
{
    public partial class ErrorHandler
    {

        public List<Exception> _receivedExceptions = new();

        protected override Task OnErrorAsync(Exception exception)
        {
            _receivedExceptions.Add(exception);
            switch (exception)
            {
                case UnauthorizedAccessException:
                    Snackbar.Add("Authentication Failed", Severity.Error);
                    break;
            }
            return Task.CompletedTask;
        }

        public new void Recover()
        {
            _receivedExceptions.Clear();
            base.Recover();
        }
    }
}