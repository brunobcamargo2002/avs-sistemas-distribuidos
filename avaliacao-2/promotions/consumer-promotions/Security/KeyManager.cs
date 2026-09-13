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

    public static RSA LoadPublicKey()
    {
        if (!File.Exists(PublicKeyPath))
        {
            throw new FileNotFoundException(
                $"Chave pública não encontrada em '{PublicKeyPath}'. Copie a chave pública do publisher antes de iniciar o consumer.");
        }

        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(PublicKeyPath));
        return rsa;
    }
}
