using System.Text.Encodings.Web;
using System.Text.Json;
using payment_mock.Models;

namespace payment_mock.Data;

/// <summary>
/// Lê e grava as cobranças em charges.json a cada operação, no mesmo molde dos
/// demais arquivos do projeto. O lock protege o arquivo das requisições
/// concorrentes (criação da cobrança, página de checkout e webhook).
/// </summary>
public static class ChargeStore
{
    private const string ChargesPath = "charges.json";
    private static readonly Lock Gate = new();
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Add(Charge charge)
    {
        using (Gate.EnterScope())
        {
            var charges = Read();
            charges.Add(charge);
            Write(charges);
        }
    }

    public static Charge? Find(Guid chargeId)
    {
        using (Gate.EnterScope())
        {
            return Read().Find(charge => charge.ChargeId == chargeId);
        }
    }

    /// <summary>
    /// Troca o status apenas se a cobrança ainda estiver pendente, devolvendo a
    /// versão atualizada. Null significa cobrança inexistente ou já decidida.
    /// </summary>
    public static Charge? TryDecide(Guid chargeId, string status)
    {
        using (Gate.EnterScope())
        {
            var charges = Read();
            var charge = charges.Find(candidate => candidate.ChargeId == chargeId);

            if (charge is null || charge.Status != ChargeStatus.Pendente)
            {
                return null;
            }

            charge.Status = status;
            charge.DecidedAt = DateTimeOffset.UtcNow;
            Write(charges);

            return charge;
        }
    }

    /// <summary>Volta a cobrança para pendente quando o webhook não pôde ser entregue.</summary>
    public static void Reopen(Guid chargeId)
    {
        using (Gate.EnterScope())
        {
            var charges = Read();
            var charge = charges.Find(candidate => candidate.ChargeId == chargeId);

            if (charge is not null)
            {
                charge.Status = ChargeStatus.Pendente;
                charge.DecidedAt = null;
                Write(charges);
            }
        }
    }

    private static List<Charge> Read()
    {
        if (!File.Exists(ChargesPath))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<Charge>>(File.ReadAllText(ChargesPath), ReadOptions) ?? [];
    }

    private static void Write(List<Charge> charges)
    {
        File.WriteAllText(ChargesPath, JsonSerializer.Serialize(charges, WriteOptions));
    }
}
