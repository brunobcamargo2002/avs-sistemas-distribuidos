using System.Security.Cryptography;

namespace payment.Security;

public static class KeyManager
{
    private const string PrivateKeyPath = "Keys/private_key.pem";
    private const string OrdersPublicKeyPath = "Keys/orders_public_key.pem";

    public static RSA LoadPrivateKey() => Load(PrivateKeyPath, "Chave privada do payment não encontrada em '{0}'.");

    public static RSA LoadOrdersPublicKey() => Load(OrdersPublicKeyPath, "Chave pública do orders não encontrada em '{0}'.");

    private static RSA Load(string path, string message)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(string.Format(message, path));
        }

        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(path));
        return rsa;
    }
}
