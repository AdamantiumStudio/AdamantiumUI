using System.Linq;
using System.Xml.Linq;
using Adamantium.UI.Core.Printing;
using Adamantium.UI.Platforms.Windows.Printing;
using NUnit.Framework;

namespace Adamantium.UITests.Printing;

/// <summary>A Windows printer's PrintCapabilities read into what the dialog offers, and the dialog's choices written
/// into its PrintTicket and read back as the driver put them right - in the Print Schema, as a driver writes it.</summary>
public class PrintSchemaTests
{
    private const string Capabilities = """
        <psf:PrintCapabilities xmlns:psf="http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework"
            xmlns:psk="http://schemas.microsoft.com/windows/2003/08/printing/printschemakeywords"
            xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema"
            xmlns:oem="http://example.com/oem" version="1">
          <psf:ParameterDef name="psk:JobCopiesAllDocuments">
            <psf:Property name="psf:MaxValue"><psf:Value xsi:type="xsd:integer">99</psf:Value></psf:Property>
            <psf:Property name="psf:MinValue"><psf:Value xsi:type="xsd:integer">1</psf:Value></psf:Property>
          </psf:ParameterDef>
          <psf:Feature name="psk:PageMediaSize">
            <psf:Option name="psk:ISOA4">
              <psf:ScoredProperty name="psk:MediaSizeWidth"><psf:Value xsi:type="xsd:integer">210000</psf:Value></psf:ScoredProperty>
              <psf:ScoredProperty name="psk:MediaSizeHeight"><psf:Value xsi:type="xsd:integer">297000</psf:Value></psf:ScoredProperty>
              <psf:Property name="psk:DisplayName"><psf:Value xsi:type="xsd:string">A4 Blatt</psf:Value></psf:Property>
            </psf:Option>
            <psf:Option name="psk:NorthAmericaLetter">
              <psf:ScoredProperty name="psk:MediaSizeWidth"><psf:Value xsi:type="xsd:integer">215900</psf:Value></psf:ScoredProperty>
              <psf:ScoredProperty name="psk:MediaSizeHeight"><psf:Value xsi:type="xsd:integer">279400</psf:Value></psf:ScoredProperty>
            </psf:Option>
            <psf:Option name="oem:Postcard">
              <psf:ScoredProperty name="psk:MediaSizeWidth"><psf:Value xsi:type="xsd:integer">100000</psf:Value></psf:ScoredProperty>
              <psf:ScoredProperty name="psk:MediaSizeHeight"><psf:Value xsi:type="xsd:integer">148000</psf:Value></psf:ScoredProperty>
              <psf:Property name="psk:DisplayName"><psf:Value xsi:type="xsd:string">Postcard</psf:Value></psf:Property>
            </psf:Option>
            <psf:Option name="psk:CustomMediaSize">
              <psf:Property name="psk:DisplayName"><psf:Value xsi:type="xsd:string">Custom</psf:Value></psf:Property>
            </psf:Option>
          </psf:Feature>
          <psf:Feature name="psk:DocumentCollate">
            <psf:Option name="psk:Collated"/>
            <psf:Option name="psk:Uncollated"/>
          </psf:Feature>
          <psf:Feature name="psk:JobDuplexAllDocumentsContiguously">
            <psf:Option name="psk:OneSided"/>
            <psf:Option name="psk:TwoSidedLongEdge"/>
          </psf:Feature>
          <psf:Feature name="psk:PageOutputColor">
            <psf:Option name="psk:Color"/>
            <psf:Option name="psk:Monochrome"/>
          </psf:Feature>
          <psf:Feature name="psk:PageResolution">
            <psf:Option name="oem:High">
              <psf:ScoredProperty name="psk:ResolutionX"><psf:Value xsi:type="xsd:integer">600</psf:Value></psf:ScoredProperty>
              <psf:ScoredProperty name="psk:ResolutionY"><psf:Value xsi:type="xsd:integer">600</psf:Value></psf:ScoredProperty>
            </psf:Option>
            <psf:Option name="oem:Draft">
              <psf:ScoredProperty name="psk:ResolutionX"><psf:Value xsi:type="xsd:integer">300</psf:Value></psf:ScoredProperty>
              <psf:ScoredProperty name="psk:ResolutionY"><psf:Value xsi:type="xsd:integer">300</psf:Value></psf:ScoredProperty>
            </psf:Option>
          </psf:Feature>
          <psf:Feature name="psk:JobInputBin">
            <psf:Option name="psk:AutoSelect">
              <psf:Property name="psk:DisplayName"><psf:Value xsi:type="xsd:string">Automatic</psf:Value></psf:Property>
            </psf:Option>
            <psf:Option name="psk:Manual">
              <psf:Property name="psk:DisplayName"><psf:Value xsi:type="xsd:string">Manual feed</psf:Value></psf:Property>
            </psf:Option>
          </psf:Feature>
          <psf:Property name="psk:PageImageableSize">
            <psf:Property name="psk:ImageableArea">
              <psf:Property name="psk:OriginWidth"><psf:Value xsi:type="xsd:integer">4233</psf:Value></psf:Property>
              <psf:Property name="psk:OriginHeight"><psf:Value xsi:type="xsd:integer">4233</psf:Value></psf:Property>
              <psf:Property name="psk:ExtentWidth"><psf:Value xsi:type="xsd:integer">201534</psf:Value></psf:Property>
              <psf:Property name="psk:ExtentHeight"><psf:Value xsi:type="xsd:integer">284300</psf:Value></psf:Property>
            </psf:Property>
          </psf:Property>
        </psf:PrintCapabilities>
        """;

    // The driver's default ticket: Letter, Draft - with prefixes of its own choosing.
    private const string Ticket = """
        <p:PrintTicket xmlns:p="http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework"
            xmlns:k="http://schemas.microsoft.com/windows/2003/08/printing/printschemakeywords"
            xmlns:o="http://example.com/oem" version="1">
          <p:Feature name="k:PageMediaSize"><p:Option name="k:NorthAmericaLetter"/></p:Feature>
          <p:Feature name="k:PageResolution"><p:Option name="o:Draft"/></p:Feature>
        </p:PrintTicket>
        """;

    private static PrintSchema Read() => PrintSchema.Read(XDocument.Parse(Capabilities), XDocument.Parse(Ticket));

    [Test]
    public void WhatThePrinterOffers_IsReadFromItsCapabilities()
    {
        var capabilities = Read().Capabilities;

        Assert.Multiple(() =>
        {
            Assert.That(capabilities.Papers.Select(paper => paper.Name), Is.EqualTo(new[] { "A4", "Letter", "Postcard" }),
                "a standard size by its standard name, the rest by the driver's; one with no size is not a paper");
            Assert.That(capabilities.Papers[0], Is.SameAs(PaperSize.A4));
            Assert.That(capabilities.DefaultPaper, Is.SameAs(PaperSize.Letter), "the default ticket's");
            Assert.That(capabilities.MaxCopies, Is.EqualTo(99));
            Assert.That(capabilities.Collates, Is.True);
            Assert.That(capabilities.Duplex, Is.EqualTo(new[] { DuplexMode.OneSided, DuplexMode.LongEdge }));
            Assert.That(capabilities.Colors, Is.EqualTo(new[] { PrintColorMode.Color, PrintColorMode.Grayscale }),
                "monochrome is the gray it offers");
            Assert.That(capabilities.Qualities, Is.EqualTo(new[] { new PrintQuality(300, 300), new PrintQuality(600, 600) }));
            Assert.That(capabilities.DefaultQuality, Is.EqualTo(new PrintQuality(300, 300)));
            Assert.That(capabilities.Trays, Is.EqualTo(new[] { "Automatic", "Manual feed" }));
            Assert.That(capabilities.MediaTypes, Is.Empty, "not offered: no row");
        });
    }

    [Test]
    public void TheChoices_AreWrittenAsTheOptionsTheDriverOffered_AndReadBack()
    {
        var schema = Read();
        var settings = new PrintSettings
        {
            Paper = PaperSize.A4,
            Orientation = PageOrientation.Landscape,
            Copies = 3,
            Collate = false,
            Duplex = DuplexMode.LongEdge,
            Color = PrintColorMode.Grayscale,
            Quality = new PrintQuality(600, 600),
            Tray = "Manual feed",
        };

        var ticket = schema.Write(XDocument.Parse(Ticket), settings);
        var text = ticket.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("k:ISOA4"), "written with the ticket's own prefixes");
            Assert.That(text, Does.Contain("210000"), "the paper's own size goes with it");
            Assert.That(text, Does.Not.Contain("A4 Blatt"), "its display name does not");
            Assert.That(text, Does.Contain("o:High"));
            Assert.That(text, Does.Contain("k:Landscape"));
            Assert.That(text, Does.Contain("k:Monochrome"));
        });

        var back = schema.ReadBack(ticket, new PrintSettings());
        Assert.Multiple(() =>
        {
            Assert.That(back.Paper, Is.SameAs(PaperSize.A4));
            Assert.That(back.Copies, Is.EqualTo(3));
            Assert.That(back.Collate, Is.False);
            Assert.That(back.Duplex, Is.EqualTo(DuplexMode.LongEdge));
            Assert.That(back.Color, Is.EqualTo(PrintColorMode.Grayscale));
            Assert.That(back.Quality, Is.EqualTo(new PrintQuality(600, 600)));
            Assert.That(back.Tray, Is.EqualTo("Manual feed"));
        });
    }

    // What the driver puts right comes back: it said one side for the paper chosen.
    [Test]
    public void WhatTheDriverPutRight_IsReadBack()
    {
        var schema = Read();
        var ticket = schema.Write(XDocument.Parse(Ticket), new PrintSettings { Duplex = DuplexMode.LongEdge });
        var duplex = ticket.Root!.Elements(PrintSchema.Framework + "Feature")
            .Single(feature => feature.Attribute("name")!.Value.EndsWith("JobDuplexAllDocumentsContiguously"));
        duplex.Element(PrintSchema.Framework + "Option")!.SetAttributeValue("name", "k:OneSided");

        Assert.That(schema.ReadBack(ticket, new PrintSettings { Duplex = DuplexMode.LongEdge }).Duplex,
            Is.EqualTo(DuplexMode.OneSided));
    }

    [Test]
    public void APapersMargins_AreWhatItsImageableAreaLeaves()
    {
        var margins = PrintSchema.MarginsIn(XDocument.Parse(Capabilities), PaperSize.A4);

        Assert.That(margins, Is.Not.Null);
        const double PerMicron = 96 / 25400.0;
        Assert.That(margins!.Value.Left, Is.EqualTo(4233 * PerMicron).Within(1e-6));
        Assert.That(margins.Value.Right, Is.EqualTo((210000 - 4233 - 201534) * PerMicron).Within(1e-3));
        Assert.That(margins.Value.Bottom, Is.EqualTo((297000 - 4233 - 284300) * PerMicron).Within(1e-3));
    }
}
