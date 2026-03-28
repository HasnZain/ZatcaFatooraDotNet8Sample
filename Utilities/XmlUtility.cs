using System.Security.Cryptography.Xml;
using System.Xml;

namespace ZatcaFatooraDotNet8Sample.Utilities;

public static class XmlUtility
{
    public static XmlDocument LoadXml(string xml)
    {
        var document = new XmlDocument
        {
            PreserveWhitespace = false
        };

        document.LoadXml(xml);
        return document;
    }

    public static byte[] Canonicalize(XmlDocument document)
    {
        var transform = new XmlDsigC14NTransform();
        transform.LoadInput(document);

        using var stream = (Stream)transform.GetOutput(typeof(Stream));
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
