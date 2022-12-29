using LibMsacServer.Entities;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace LibMsacServer
{
    public class MsacServer : IDisposable
    {
        public MsacServer(IPEndPoint endpoint, string uaOperatingSystem = "windows 7", string uaVersion = "5.3.4")
        {
            //Set
            this.uaOperatingSystem = uaOperatingSystem;
            this.uaVersion = uaVersion;

            //Bind and begin listening
            try
            {
                server = new Socket(SocketType.Stream, ProtocolType.Tcp);
                server.Bind(endpoint);
                server.Listen(1);
            } catch
            {
                server = null;
            }
        }

        private readonly Socket server;
        private readonly string uaOperatingSystem;
        private readonly string uaVersion;

        private ushort nextLotId;
        private bool active = true;

        public bool SocketReady => server != null;

        public event DirectFileCopyEventArgs OnDirectFileCopyRequest;
        public event AsyncSendEventArgs OnAsyncSendRequest;
        public event PsdUpdateEventArgs OnPsdUpdateRequest;
        public event SocketErrorEventArgs OnSocketError;

        private const string XML_END = "</HDRadio-Envelope>";

        public delegate void DirectFileCopyEventArgs(MsacServer server, string filename, byte[] data);
        public delegate void AsyncSendEventArgs(MsacServer server, HdOutgoingImage image);
        public delegate void PsdUpdateEventArgs(MsacServer server, HdPsd psd, HdXhdr xhdr);
        public delegate void SocketErrorEventArgs(MsacServer server, Exception ex);

        public void Dispose()
        {
            active = false;
            if (server != null)
            {
                server.Close();
                server.Dispose();
            }
        }

        public void Run()
        {
            //Start accepting clients
            while (active)
            {
                try
                {
                    //Get client and wrap
                    Socket client = server.Accept();
                    ClientContext ctx = new ClientContext(client);

                    //Process messages
                    try
                    {
                        while (ctx.Receive())
                        {
                            //Process all messages we might've received
                            while (true)
                            {
                                //We need to find the end of the XML data. We search for the "</HDRadio-Envelope>" end tag.
                                //This is a disgusting, but it appears to be how iBiquity did it too!
                                if (!ctx.Buffer.TryFindMatch(Encoding.UTF8.GetBytes(XML_END), 0, ctx.Available, out int endIndex))
                                {
                                    if (ctx.Available == ctx.Buffer.Length)
                                        throw new Exception("XML is either too long or the socket has entered an unstable state.");
                                    break;
                                }

                                //Extract XML and offset read data in the buffer
                                byte[] xml = ctx.Consume(endIndex + XML_END.Length);

                                //Parse XML
                                XmlDocument doc = new XmlDocument();
                                using (MemoryStream ms = new MemoryStream(xml))
                                    doc.Load(ms);

                                //Traverse XML and process
                                XmlNode envelope = doc["HDRadio-Envelope"];
                                XmlNode request = envelope["MSAC-Request"];
                                HandleMessage(ctx, request);
                            }
                        }
                    }
                    finally
                    {
                        //Close connection
                        client.Close();
                    }
                }
                catch (Exception ex)
                {
                    //Log error
                    OnSocketError?.Invoke(this, ex);
                }            
            }
        }

        private void HandleMessage(ClientContext ctx, XmlNode request)
        {
            //Get info and opcode
            XmlNode info = request["Msg-Info"];
            string opcode = info.Attributes["msgType"].Value;
            Console.WriteLine(opcode);
            
            //Switch on the opcode type
            switch (opcode)
            {
                case "Direct File Copy": HandleDirectFileCopy(ctx, info); break;
                case "Async Send": HandleAsyncSend(ctx, info); break;
                case "Sync Pre Send": HandleSyncPreSend(ctx, info); break;
                case "PSD Send": HandlePsdSend(ctx, request); break;
            }
        }

        private void HandleDirectFileCopy(ClientContext ctx, XmlNode info)
        {
            //Get required fields
            if (!info.Attributes.TryGetString("fileDestination", out string fileDestination) || !info.Attributes.TryGetInt("fileSize", out int fileSize) || !info.Attributes.TryGetInt("offset", out int offset))
                throw new Exception("Direct File Copy message did not have all required fields!");

            //Sanity check
            if (offset != 0)
                throw new Exception("\"Offset\" was expected to be 0, but it wasn't!");

            //Read image data. We'll start with the contents already in the FIFO buffer
            byte[] image = new byte[fileSize];
            offset = Math.Min(fileSize, ctx.Available);
            fileSize -= offset;
            ctx.Consume(offset, image, 0);

            //As required, read additional data directly from the socket (bypassing the FIFO)
            while (fileSize > 0)
            {
                int read = ctx.Socket.Receive(image, offset, fileSize, SocketFlags.None);
                if (read == 0)
                    throw new Exception("Socket lost connection while receiving image!");
                offset += read;
                fileSize -= read;
            }

            //Send event
            OnDirectFileCopyRequest?.Invoke(this, fileDestination, image);

            //Create and send confirmation message
            XmlDocument doc = new XmlDocument();
            XmlNode responseEnvelope = doc.AppendChild(doc.CreateNode(XmlNodeType.Element, "HDRadio-Envelope", null));
            XmlNode responseBody = responseEnvelope.AppendChild(doc.CreateNode(XmlNodeType.Element, "MSAC-Response", null));
            responseBody.AddAttribute("returnString", "OK");
            XmlNode responseInfo = responseBody.AppendChild(doc.CreateNode(XmlNodeType.Element, "Msg-Info", null));
            responseInfo.AddAttribute("msgType", "Direct File Copy");

            //Send response
            ctx.Send(doc);
        }

        private void HandleAsyncSend(ClientContext ctx, XmlNode info)
        {
            //Get required fields
            if (!info.Attributes.TryGetString("dataServiceName", out string dataServiceName) || !info.Attributes.TryGetString("fileName", out string fileName))
                throw new Exception("Async Send message did not have all required fields!");

            //Create the outgoing image
            HdOutgoingImage img = new HdOutgoingImage
            {
                DataServiceName = dataServiceName,
                FileName = fileName,
                UniqueTag = HdOutgoingImage.CreateUniqueTag(DateTime.UtcNow),
                LotId = nextLotId++
            };

            //Send event
            OnAsyncSendRequest?.Invoke(this, img);

            //Create and send response
            XmlDocument doc = new XmlDocument();
            XmlNode responseEnvelope = doc.AppendChild(doc.CreateNode(XmlNodeType.Element, "HDRadio-Envelope", null));
            XmlNode responseBody = responseEnvelope.AppendChild(doc.CreateNode(XmlNodeType.Element, "MSAC-Response", null));
            responseBody.AddAttribute("uniqueTag", img.UniqueTag);
            responseBody.AddAttribute("returnString", "OK");
            responseBody.AddAttribute("MSAC-OS", uaOperatingSystem);
            responseBody.AddAttribute("MSAC-Version", uaVersion);
            XmlNode responseInfo = responseBody.AppendChild(doc.CreateNode(XmlNodeType.Element, "Msg-Info", null));
            responseInfo.AddAttribute("msgType", "Async Send");
            responseInfo.AddAttribute("dataServiceName", img.DataServiceName);
            responseInfo.AddAttribute("state", "Pending");
            XmlNode responseLot = responseBody.AppendChild(doc.CreateNode(XmlNodeType.Element, "Lot-Info", null));
            responseLot.AddAttribute("lotId", img.LotId.ToString());

            //Send response
            ctx.Send(doc);
        }

        private void HandleSyncPreSend(ClientContext ctx, XmlNode info)
        {
            //Get required fields
            if (!info.Attributes.TryGetString("dataServiceName", out string dataServiceName) || !info.Attributes.TryGetString("fileName", out string fileName))
                throw new Exception("Sync Pre Send message did not have all required fields!");

            //Get optional fields
            int duration;
            if (!info.Attributes.TryGetInt("songDuration", out duration))
                duration = 0;

            //Create the outgoing image
            HdOutgoingImage img = new HdOutgoingImage
            {
                DataServiceName = dataServiceName,
                FileName = fileName,
                UniqueTag = HdOutgoingImage.CreateUniqueTag(DateTime.UtcNow),
                LotId = nextLotId++,
                Duration = duration
            };

            //Send event
            OnAsyncSendRequest?.Invoke(this, img);

            //Create and send response
            XmlDocument doc = new XmlDocument();
            XmlNode responseEnvelope = doc.AppendChild(doc.CreateNode(XmlNodeType.Element, "HDRadio-Envelope", null));
            XmlNode responseBody = responseEnvelope.AppendChild(doc.CreateNode(XmlNodeType.Element, "MSAC-Response", null));
            responseBody.AddAttribute("uniqueTag", img.UniqueTag);
            responseBody.AddAttribute("returnString", "OK");
            responseBody.AddAttribute("MSAC-OS", uaOperatingSystem);
            responseBody.AddAttribute("MSAC-Version", uaVersion);
            XmlNode responseInfo = responseBody.AppendChild(doc.CreateNode(XmlNodeType.Element, "Msg-Info", null));
            responseInfo.AddAttribute("msgType", "Sync Pre Send");
            responseInfo.AddAttribute("dataServiceName", img.DataServiceName);
            responseInfo.AddAttribute("state", "Pending");
            XmlNode responseLot = responseBody.AppendChild(doc.CreateNode(XmlNodeType.Element, "Lot-Info", null));
            responseLot.AddAttribute("lotId", img.LotId.ToString());

            //Send response
            ctx.Send(doc);
        }

        private void HandlePsdSend(ClientContext ctx, XmlNode request)
        {
            //Unpack
            XmlNode psdFields = request["PSD-Fields"];
            XmlNode psdXml = psdFields["core"];
            XmlNode xhdrXml = psdFields["xhdr"];

            //Unpack PSD
            HdPsd psd = new HdPsd();
            if (psdXml.Attributes.TryGetString("title", out string title))
                psd.Title = title;
            if (psdXml.Attributes.TryGetString("artist", out string artist))
                psd.Artist = artist;
            if (psdXml.Attributes.TryGetString("genre", out string genre))
                psd.Genre = genre;
            if (psdXml.Attributes.TryGetString("album", out string album))
                psd.Album = album;

            //Unpack XHDR
            HdXhdr xhdr = new HdXhdr();
            if (xhdrXml.Attributes.TryGetString("mimeType", out string mimeType))
                xhdr.MimeType = mimeType;
            if (xhdrXml.Attributes.TryGetBool("trigger", out bool trigger))
                xhdr.Trigger = trigger;
            if (xhdrXml.Attributes.TryGetBool("blankScreen", out bool blankScreen))
                xhdr.BlankScreen = blankScreen;
            if (xhdrXml.Attributes.TryGetBool("flushMemory", out bool flushMemory))
                xhdr.FlushMemory = flushMemory;
            if (xhdrXml.Attributes.TryGetInt("lotId", out int lotId))
                xhdr.LotId = lotId;

            //Send events
            OnPsdUpdateRequest?.Invoke(this, psd, xhdr);

            //Create and send confirmation message
            XmlDocument doc = new XmlDocument();
            XmlNode responseEnvelope = doc.AppendChild(doc.CreateNode(XmlNodeType.Element, "HDRadio-Envelope", null));
            XmlNode responseBody = responseEnvelope.AppendChild(doc.CreateNode(XmlNodeType.Element, "MSAC-Response", null));
            responseBody.AddAttribute("returnString", "OK");
            XmlNode responseInfo = responseBody.AppendChild(doc.CreateNode(XmlNodeType.Element, "Msg-Info", null));
            responseInfo.AddAttribute("msgType", "PSD Send");

            //Send response
            ctx.Send(doc);
        }
    }
}
