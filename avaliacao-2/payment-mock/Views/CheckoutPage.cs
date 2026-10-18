using System.Globalization;
using payment_mock.Models;

namespace payment_mock.Views;

/// <summary>
/// Página de checkout servida pelo mock. Os botões chamam o próprio mock, que
/// então dispara o webhook para a URL informada pelo MS Pagamento.
/// </summary>
public static class CheckoutPage
{
    private static readonly CultureInfo Brazil = CultureInfo.GetCultureInfo("pt-BR");

    public static string Render(Charge charge)
    {
        var amount = charge.Amount.ToString("C2", Brazil);
        var decided = charge.Status != ChargeStatus.Pendente;

        return $$"""
        <!doctype html>
        <html lang="pt-BR">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>PagFake | Checkout</title>
            <style>
                :root { color-scheme: light; }
                * { box-sizing: border-box; }
                body {
                    margin: 0; min-height: 100vh; display: grid; place-items: center;
                    padding: 24px; background: #eef1f6;
                    font: 16px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; color: #1b2330;
                }
                .card {
                    width: 100%; max-width: 420px; background: #fff; border-radius: 16px;
                    padding: 28px; box-shadow: 0 12px 32px rgba(27, 35, 48, .12);
                }
                .brand { font-size: 13px; letter-spacing: .12em; text-transform: uppercase; color: #6b7a90; margin: 0 0 18px; }
                h1 { font-size: 20px; margin: 0 0 4px; }
                .amount { font-size: 34px; font-weight: 700; margin: 12px 0 20px; }
                dl { margin: 0 0 24px; font-size: 13px; color: #55637a; }
                dl div { display: flex; justify-content: space-between; gap: 12px; padding: 6px 0; border-top: 1px solid #eef1f6; }
                dt { font-weight: 500; }
                dd { margin: 0; font-family: ui-monospace, "Cascadia Code", monospace; font-size: 12px; }
                .actions { display: grid; gap: 10px; }
                button {
                    font: inherit; font-weight: 600; padding: 13px 16px; border: 0;
                    border-radius: 10px; cursor: pointer; color: #fff;
                }
                button:disabled { opacity: .45; cursor: not-allowed; }
                .approve { background: #12805c; }
                .reject { background: #b3261e; }
                .feedback { margin: 18px 0 0; padding: 12px 14px; border-radius: 10px; font-size: 14px; display: none; }
                .feedback.show { display: block; }
                .feedback.ok { background: #e6f4ef; color: #0d5c43; }
                .feedback.error { background: #fdeceb; color: #8c1d18; }
                .pill { display: inline-block; padding: 3px 10px; border-radius: 999px; font-size: 12px; font-weight: 600; }
                .pill.APROVADO { background: #e6f4ef; color: #0d5c43; }
                .pill.RECUSADO { background: #fdeceb; color: #8c1d18; }
                .pill.PENDENTE { background: #fff4e0; color: #8a5a00; }
            </style>
        </head>
        <body>
            <main class="card">
                <p class="brand">PagFake &middot; Simulador de pagamentos</p>
                <h1>Confirmação de pagamento</h1>
                <p class="amount">{{amount}}</p>
                <dl>
                    <div><dt>Cobrança</dt><dd>{{charge.ChargeId}}</dd></div>
                    <div><dt>Pedido</dt><dd>{{charge.OrderId}}</dd></div>
                    <div><dt>Status</dt><dd><span class="pill {{charge.Status}}" id="status">{{charge.Status}}</span></dd></div>
                </dl>
                <div class="actions">
                    <button class="approve" data-status="APROVADO" {{(decided ? "disabled" : "")}}>Pagamento Aprovado</button>
                    <button class="reject" data-status="RECUSADO" {{(decided ? "disabled" : "")}}>Pagamento Recusado</button>
                </div>
                <p class="feedback" id="feedback"></p>
            </main>
            <script>
                const chargeId = "{{charge.ChargeId}}";
                const feedback = document.querySelector("#feedback");
                const statusPill = document.querySelector("#status");
                const buttons = document.querySelectorAll("button[data-status]");

                function show(message, kind) {
                    feedback.textContent = message;
                    feedback.className = `feedback show ${kind}`;
                }

                buttons.forEach((button) => button.addEventListener("click", async () => {
                    const status = button.dataset.status;
                    buttons.forEach((other) => (other.disabled = true));
                    show("Enviando decisão ao lojista...", "ok");

                    try {
                        const response = await fetch(`/api/charges/${chargeId}/decide`, {
                            method: "POST",
                            headers: { "Content-Type": "application/json" },
                            body: JSON.stringify({ status }),
                        });
                        const result = await response.json();

                        if (!response.ok) throw new Error(result.detail ?? result.message ?? "Falha ao notificar o lojista.");

                        statusPill.textContent = status;
                        statusPill.className = `pill ${status}`;
                        show(`Webhook entregue: pagamento ${status.toLowerCase()}. Você já pode voltar à loja.`, "ok");
                    } catch (error) {
                        show(error.message, "error");
                        buttons.forEach((other) => (other.disabled = false));
                    }
                }));
            </script>
        </body>
        </html>
        """;
    }
}
