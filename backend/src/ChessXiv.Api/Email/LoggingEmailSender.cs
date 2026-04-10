namespace ChessXiv.Api.Email;

public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Email to {Email}. Subject: {Subject}. Body: {Body}", toEmail, subject, body);
        return Task.CompletedTask;
    }
}
