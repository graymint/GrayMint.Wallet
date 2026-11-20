using Microsoft.AspNetCore.Mvc.Filters;

namespace GrayMint.Wallet.Server;

// GrayMint's handler answers 400 for an exception it is told no status of, as if the request were wrong in itself.
// A failure of the server's own, such as the database's, answers 500 here instead, so the caller sends the same
// request again, which its keys make safe: a commit may even have gone through before the failure.
internal sealed class ServerFailureFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var exception = context.Exception;
        if (exception.Data["HttpStatusCode"] == null && exception is not (ArgumentException or NotSupportedException))
            exception.Data["HttpStatusCode"] = StatusCodes.Status500InternalServerError;
    }
}
