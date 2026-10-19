using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using orders.Services;
using RabbitMQ.Client.Exceptions;

namespace orders.Endpoints;

public static class PromotionInterestEndpoints
{
    private static readonly Regex CategoryPattern = new(
        @"^[a-z0-9_]+(?:\.[a-z0-9_]+)*$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static RouteGroupBuilder MapPromotionInterestEndpoints(this IEndpointRouteBuilder app, OrderService orderService)
    {
        var group = app.MapGroup("/api/promotion-interests").WithTags("Promotion interests");

        group.MapPost("/", async Task<Results<Accepted, ValidationProblem, ProblemHttpResult>> (
            CreatePromotionInterestRequest request, ILogger<OrderService> logger, CancellationToken cancellationToken) =>
        {
            var errors = ValidateEmail(request.Email);
            if (request.Categories is not { Count: > 0 and <= 100 } ||
                request.Categories.Any(category => string.IsNullOrWhiteSpace(category) ||
                    category.Trim().Length > 128 || !CategoryPattern.IsMatch(category.Trim())))
            {
                errors["categories"] = ["Informe de 1 a 100 categorias validas, como hardware.placa_de_video."];
            }

            if (errors.Count > 0)
            {
                return TypedResults.ValidationProblem(errors);
            }

            var categories = request.Categories!.Select(category => category!.Trim()).Distinct(StringComparer.Ordinal).ToArray();
            try
            {
                await orderService.PublishPromotionInterestAsync(request.Email!.Trim(), categories, false, cancellationToken);
                return TypedResults.Accepted((string?)null);
            }
            catch (Exception exception) when (exception is RabbitMQClientException or IOException or TimeoutException)
            {
                logger.LogError(exception, "Failed to publish promotion interest registration.");
                return TypedResults.Problem("Nao foi possivel confirmar a solicitacao. Tente novamente mais tarde.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).WithName("RegisterPromotionInterest");

        group.MapDelete("/", async Task<Results<Accepted, ValidationProblem, ProblemHttpResult>> (
            [FromBody] CancelPromotionInterestRequest request, ILogger<OrderService> logger, CancellationToken cancellationToken) =>
        {
            var errors = ValidateEmail(request.Email);
            if (errors.Count > 0)
            {
                return TypedResults.ValidationProblem(errors);
            }

            try
            {
                await orderService.PublishPromotionInterestAsync(request.Email!.Trim(), [], true, cancellationToken);
                return TypedResults.Accepted((string?)null);
            }
            catch (Exception exception) when (exception is RabbitMQClientException or IOException or TimeoutException)
            {
                logger.LogError(exception, "Failed to publish promotion interest cancellation.");
                return TypedResults.Problem("Nao foi possivel confirmar a solicitacao. Tente novamente mais tarde.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).WithName("CancelPromotionInterest");

        return group;
    }

    private static Dictionary<string, string[]> ValidateEmail(string? email)
    {
        var errors = new Dictionary<string, string[]>();
        var normalized = email?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 254 ||
            normalized.Contains('\r') || normalized.Contains('\n') ||
            !MailAddress.TryCreate(normalized, out var address) ||
            !string.Equals(address.Address, normalized, StringComparison.Ordinal))
        {
            errors["email"] = ["Informe um endereco de e-mail valido com ate 254 caracteres."];
        }

        return errors;
    }
}

public sealed record CreatePromotionInterestRequest(string? Email, List<string?>? Categories);
public sealed record CancelPromotionInterestRequest(string? Email);