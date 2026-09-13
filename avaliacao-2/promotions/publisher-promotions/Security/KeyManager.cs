using System.Security.Cryptography;

namespace publisher_promotions.Security;

public static class KeyManager
{
    private const string PrivateKeyPath = "Keys/private_key.pem";

    public static RSA LoadPrivateKey()
    {
        if (!File.Exists(PrivateKeyPath))
        {
            throw new FileNotFoundException(
                $"Chave privada não encontrada em '{PrivateKeyPath}'. Gere o par de chaves antes de iniciar o publisher.");
        }

        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(PrivateKeyPath));
        return rsa;
    }
}
