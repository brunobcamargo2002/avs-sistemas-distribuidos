using System.Security.Cryptography;

namespace stock.Security;

/// <summary>
/// Carrega a chave privada do stock (para assinar pedido.estoque_ok / pedido.indisponivel)
/// e a chave pública do orders (para verificar pedido.criado / pedido.excluido).
/// As chaves devem existir previamente; se não existirem, a aplicação não deve iniciar.
/// </summary>
public static class KeyManager
{
    private const string PrivateKeyPath = "Keys/private_key.pem";
    private const string OrdersPublicKeyPath = "Keys/orders_public_key.pem";

    public static RSA LoadPrivateKey() => LoadFromPem(
        PrivateKeyPath,
        "Chave privada não encontrada em '{0}'. Gere o par de chaves antes de iniciar o stock.");

    public static RSA LoadOrdersPublicKey() => LoadFromPem(
        OrdersPublicKeyPath,
        "Chave pública do orders não encontrada em '{0}'. Copie a chave pública do orders antes de iniciar o stock.");

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
