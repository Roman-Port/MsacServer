using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Xml;

namespace LibMsacServer
{
    class ClientContext
    {
        public ClientContext(Socket sock)
        {
            this.sock = sock;
            buffer = new byte[4096];
        }

        private Socket sock;
        private byte[] buffer;
        private int available;

        public Socket Socket => sock;
        public byte[] Buffer => buffer;
        public int Available => available;

        /// <summary>
        /// Sends an XML document as a response.
        /// </summary>
        /// <param name="document"></param>
        public void Send(XmlDocument document)
        {
            //Convert to bytes
            string xml = document.OuterXml;
            byte[] data = Encoding.UTF8.GetBytes(xml);

            //Send
            sock.Send(data, 0, data.Length, SocketFlags.None);
        }

        /// <summary>
        /// Sends an string as a response.
        /// </summary>
        /// <param name="document"></param>
        public void Send(string data)
        {
            //Convert to bytes
            byte[] bytes = Encoding.UTF8.GetBytes(data);

            //Send
            sock.Send(bytes, 0, bytes.Length, SocketFlags.None);
        }

        /// <summary>
        /// Consumes length bytes from the FIFO.
        /// </summary>
        /// <param name="length"></param>
        public byte[] Consume(int length)
        {
            byte[] result = new byte[length];
            Consume(length, result, 0);
            return result;
        }

        public void Consume(int length, byte[] result, int offset)
        {
            //Make sure this many are available
            if (length > available)
                throw new Exception("There are not enough available bytes in the FIFO to consume!");

            //Get the bytes we're consuming
            Array.Copy(buffer, 0, result, offset, length);

            //Copy over
            available -= length;
            Array.Copy(buffer, length, buffer, 0, available);
        }

        /// <summary>
        /// Receives from the socket into the FIFO and returns the number of readable bytes.
        /// </summary>
        /// <param name="callback"></param>
        public bool Receive()
        {
            //Read
            int read = sock.Receive(buffer, available, buffer.Length - available, SocketFlags.None);
            if (read == 0)
                return false;

            //Update counters
            available += read;

            return true;
        }
    }
}
