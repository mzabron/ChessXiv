using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ChessXiv.Api.Email;

public sealed class BrevoEmailSender(
    HttpClient httpClient,
    IOptions<BrevoOptions> options,
    ILogger<BrevoEmailSender> logger) : IEmailSender
{
    private readonly BrevoOptions brevoOptions = options.Value;

    public async Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(brevoOptions.ApiKey)
            || string.IsNullOrWhiteSpace(brevoOptions.SenderEmail))
        {
            throw new InvalidOperationException("Brevo email sender is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v3/smtp/email");
        request.Headers.Add("api-key", brevoOptions.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = new
        {
            sender = new
            {
                email = brevoOptions.SenderEmail,
                name = brevoOptions.SenderName
            },
            to = new[]
            {
                new { email = toEmail }
            },
            subject,
            htmlContent = body
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError(
                "Brevo email API failed with status {StatusCode}. Response: {Response}",
                response.StatusCode,
                responseBody);

            throw new InvalidOperationException("Failed to send email via Brevo.");
        }
    }
}