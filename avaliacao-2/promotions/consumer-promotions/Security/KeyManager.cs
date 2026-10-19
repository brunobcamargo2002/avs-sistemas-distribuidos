using System.Security.Cryptography;

namespace consumer_promotions.Security;

/// <summary>
/// Carrega a chave pública RSA do publisher, usada para verificar a assinatura
/// das promoções recebidas. A chave deve existir previamente; se não existir,
/// a aplicação não deve iniciar.
/// </summary>
public static class KeyManager
{
    private const string PublicKeyPath = "Keys/public_key.pem";
    private const string OrdersPublicKeyPath = "Keys/orders_public_key.pem";

    public static RSA LoadPublicKey() => Load(PublicKeyPath);

    public static RSA LoadOrdersPublicKey() => Load(OrdersPublicKeyPath);

    private static RSA Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Public key not found: '{path}'.", path);
        }

        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(path));
        return rsa;
    }
}
