using payment_mock.Data;
using payment_mock.Views;

namespace payment_mock.Endpoints;

/// <summary>
/// Página de checkout que o cliente abre no navegador para aprovar ou recusar.
/// </summary>
public static class CheckoutEndpoints
{
    public static RouteGroupBuilder MapCheckoutEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/checkout");

        group.MapGet("/{chargeId:guid}", (Guid chargeId) =>
            ChargeStore.Find(chargeId) is { } charge
                ? Results.Content(CheckoutPage.Render(charge), "text/html; charset=utf-8")
                : Results.NotFound());

        return group;
    }
}
