using System.Security.Cryptography;
using System.Security.Cryptography.Xml;

namespace ZatcaFatooraDotNet8Sample.Utilities;

/// <summary>
/// ZATCA UBL samples use Algorithm URI <c>http://www.w3.org/2006/12/xml-c14n11</c>. .NET maps known URIs
/// to <see cref="XmlDsigC14NTransform"/> (W3C C14N 1.0 implementation) via <see cref="CryptoConfig"/>.
/// </summary>
public sealed class ZatcaXmlDsigC14N11Transform : XmlDsigC14NTransform
{
    public const string AlgorithmUrl = "http://www.w3.org/2006/12/xml-c14n11";

    static ZatcaXmlDsigC14N11Transform()
    {
        CryptoConfig.AddAlgorithm(typeof(ZatcaXmlDsigC14N11Transform), AlgorithmUrl);
    }

    /// <summary>Registers the C14N 1.1 URI with <see cref="CryptoConfig"/> (runs the static constructor).</summary>
    public static void EnsureCryptoRegistered() { }

    public ZatcaXmlDsigC14N11Transform()
    {
        Algorithm = AlgorithmUrl;
    }
}
