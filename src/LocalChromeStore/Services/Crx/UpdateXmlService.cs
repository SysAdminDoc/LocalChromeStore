using System.IO;
using System.Text;
using System.Xml;

namespace LocalChromeStore.Services.Crx;

public static class UpdateXmlService
{
    private const string UpdateNamespace = "http://www.google.com/update2/response";

    public static string Create(string extensionId, Uri codebaseUrl, string version)
    {
        if (!Crx3PackageService.IsValidExtensionId(extensionId))
            throw new ArgumentException("Extension ID must be 32 characters in Chrome's a-p alphabet.", nameof(extensionId));
        ArgumentNullException.ThrowIfNull(codebaseUrl);
        if (!codebaseUrl.IsAbsoluteUri)
            throw new ArgumentException("Codebase URL must be absolute.", nameof(codebaseUrl));
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version is required.", nameof(version));

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            OmitXmlDeclaration = false
        };

        using var writerBuffer = new Utf8StringWriter();
        using (var writer = XmlWriter.Create(writerBuffer, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("gupdate", UpdateNamespace);
            writer.WriteAttributeString("protocol", "2.0");
            writer.WriteStartElement("app", UpdateNamespace);
            writer.WriteAttributeString("appid", extensionId);
            writer.WriteStartElement("updatecheck", UpdateNamespace);
            writer.WriteAttributeString("codebase", codebaseUrl.AbsoluteUri);
            writer.WriteAttributeString("version", version.Trim());
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return writerBuffer.ToString();
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
