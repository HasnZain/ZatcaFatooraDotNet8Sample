using System.Security.Cryptography.Xml;
using System.Xml;

namespace ZatcaFatooraDotNet8Sample.Utilities;

public static class XmlUtility
{
    public static XmlDocument LoadXml(string xml, bool preserveWhitespace = false)
    {
        var document = new XmlDocument { PreserveWhitespace = preserveWhitespace };
        document.LoadXml(xml);
        return document;
    }

    /// <summary>Same canonicalization URI as ZATCA sample (<c>xml-c14n11</c>) for invoice hashing.</summary>
    public static byte[] Canonicalize(XmlDocument document)
    {
        ZatcaXmlDsigC14N11Transform.EnsureCryptoRegistered();
        var transform = new ZatcaXmlDsigC14N11Transform();
        transform.LoadInput(document);

        using var stream = (Stream)transform.GetOutput(typeof(Stream));
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
