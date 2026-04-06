using System.Security.Cryptography;
using System.Security.Cryptography.Xml;

namespace ZatcaFatooraDotNet8Sample.Utilities;

public sealed class EcdsaSha256SignatureDescription : SignatureDescription
{
    public EcdsaSha256SignatureDescription()
    {
        KeyAlgorithm = typeof(ECDsa).AssemblyQualifiedName;
        DigestAlgorithm = "http://www.w3.org/2001/04/xmlenc#sha256";
        FormatterAlgorithm = typeof(EcdsaSignatureFormatter).AssemblyQualifiedName;
        DeformatterAlgorithm = typeof(EcdsaSignatureDeformatter).AssemblyQualifiedName;
    }

    public override AsymmetricSignatureFormatter CreateFormatter(AsymmetricAlgorithm key)
    {
        var formatter = new EcdsaSignatureFormatter();
        formatter.SetKey(key);
        formatter.SetHashAlgorithm("SHA256");
        return formatter;
    }

    public override AsymmetricSignatureDeformatter CreateDeformatter(AsymmetricAlgorithm key)
    {
        var deformatter = new EcdsaSignatureDeformatter();
        deformatter.SetKey(key);
        deformatter.SetHashAlgorithm("SHA256");
        return deformatter;
    }
}

public sealed class EcdsaSignatureFormatter : AsymmetricSignatureFormatter
{
    private ECDsa? _key;
    private string? _hashAlgorithm;

    public override void SetKey(AsymmetricAlgorithm key)
    {
        _key = key as ECDsa ?? throw new ArgumentException("Key must be ECDsa", nameof(key));
    }

    public override void SetHashAlgorithm(string name)
    {
        _hashAlgorithm = name;
    }

    public override byte[] CreateSignature(byte[] rgbHash)
    {
        if (_key == null)
            throw new CryptographicException("Key is not set");
        if (_hashAlgorithm != "SHA256")
            throw new CryptographicException("Only SHA256 is supported");

        return _key.SignHash(rgbHash);
    }
}

public sealed class EcdsaSignatureDeformatter : AsymmetricSignatureDeformatter
{
    private ECDsa? _key;
    private string? _hashAlgorithm;

    public override void SetKey(AsymmetricAlgorithm key)
    {
        _key = key as ECDsa ?? throw new ArgumentException("Key must be ECDsa", nameof(key));
    }

    public override void SetHashAlgorithm(string name)
    {
        _hashAlgorithm = name;
    }

    public override bool VerifySignature(byte[] rgbHash, byte[] rgbSignature)
    {
        if (_key == null)
            throw new CryptographicException("Key is not set");
        if (_hashAlgorithm != "SHA256")
            throw new CryptographicException("Only SHA256 is supported");

        return _key.VerifyHash(rgbHash, rgbSignature);
    }
}
