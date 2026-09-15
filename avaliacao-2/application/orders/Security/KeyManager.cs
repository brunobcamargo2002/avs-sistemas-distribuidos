using System.Security.Cryptography;

namespace orders.Security;

/// <summary>
/// Carrega a chave privada do orders (para assinar pedido.criado / pedido.excluido)
/// e a chave pública do stock (para verificar pedido.estoque_ok / pedido.indisponivel).
/// As chaves devem existir previamente; se não existirem, a aplicação não deve iniciar.
/// </summary>
public static class KeyManager
{
    private const string PrivateKeyPath = "Keys/private_key.pem";
    private const string StockPublicKeyPath = "Keys/stock_public_key.pem";
    private const string PaymentPublicKeyPath = "Keys/payment_public_key.pem";
    private const string DeliveryPublicKeyPath = "Keys/delivery_public_key.pem";

    public static RSA LoadPrivateKey() => LoadFromPem(
        PrivateKeyPath,
        "Chave privada não encontrada em '{0}'. Gere o par de chaves antes de iniciar o orders.");

    public static RSA LoadStockPublicKey() => LoadFromPem(
        StockPublicKeyPath,
        "Chave pública do stock não encontrada em '{0}'. Copie a chave pública do stock antes de iniciar o orders.");

    public static RSA LoadPaymentPublicKey() => LoadFromPem(
        PaymentPublicKeyPath,
        "Chave pública do payment não encontrada em '{0}'. Copie a chave pública do payment antes de iniciar o orders.");

    public static RSA LoadDeliveryPublicKey() => LoadFromPem(
        DeliveryPublicKeyPath,
        "Chave pública do delivery não encontrada em '{0}'. Copie a chave pública do delivery antes de iniciar o orders.");

    private static RSA LoadFromPem(string path, string errorMessageFormat)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(string.Format(errorMessageFormat, path));
        }

        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(path));
        return rsa;
    }
}
