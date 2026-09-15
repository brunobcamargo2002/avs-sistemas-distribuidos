using System.Security.Cryptography;

namespace delivery.Security;

public static class KeyManager
{
    private const string PrivateKeyPath = "Keys/private_key.pem";
    private const string PaymentPublicKeyPath = "Keys/payment_public_key.pem";

    public static RSA LoadPrivateKey() => Load(PrivateKeyPath, "Chave privada do delivery não encontrada em '{0}'.");

    public static RSA LoadPaymentPublicKey() => Load(PaymentPublicKeyPath, "Chave pública do payment não encontrada em '{0}'.");

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
